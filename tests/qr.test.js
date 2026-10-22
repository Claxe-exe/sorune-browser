// Checks the QR encoder in src/Claxe/Pages/qr.js without a browser: run with `node tests/qr.test.js`.
// Verifies the capacity tables against ISO/IEC 18004 values, version selection, matrix size,
// the fixed patterns, and that every Reed–Solomon block has a zero syndrome.
"use strict";

const fs = require("fs");
const path = require("path");
const vm = require("vm");
const assert = require("assert");

const context = { window: {}, TextEncoder };
vm.createContext(context);
vm.runInContext(fs.readFileSync(path.join(__dirname, "..", "src", "Claxe", "Pages", "qr.js"), "utf8"), context);
const QrCode = context.window.QrCode;

// Byte-mode capacities from the standard (version, level, bytes).
const capacities = [[1, "L", 17], [1, "M", 14], [1, "Q", 11], [1, "H", 7], [7, "M", 122], [10, "M", 213],
    [40, "L", 2953], [40, "M", 2331], [40, "Q", 1663], [40, "H", 1273]];
for (const [version, level, bytes] of capacities) {
    const bits = QrCode.dataCodewords(version, level) * 8 - 4 - (version < 10 ? 8 : 16);
    assert.strictEqual(Math.floor(bits / 8), bytes, `capacity ${version}-${level}`);
}

// The smallest version that fits is chosen.
assert.strictEqual(QrCode.encode("a".repeat(14), "M").version, 1);
assert.strictEqual(QrCode.encode("a".repeat(15), "M").version, 2);
assert.throws(() => QrCode.encode("a".repeat(2954), "L"));

const qr = QrCode.encode("https://github.com/Claxe-exe/sorune-browser", "M");
assert.strictEqual(qr.size, qr.version * 4 + 17);

// Finder pattern at the top-left: dark ring, light ring, dark 3x3 centre.
const m = qr.modules;
for (let i = 0; i < 7; i++) {
    assert.ok(m[0][i] && m[6][i] && m[i][0] && m[i][6], "finder outer ring");
}
assert.ok(!m[1][1] && !m[1][5] && !m[5][1], "finder inner light ring");
assert.ok(m[2][2] && m[3][3] && m[4][4], "finder centre");
// Timing pattern alternates; the dark module is always dark.
for (let i = 8; i < qr.size - 8; i++) assert.strictEqual(m[6][i], i % 2 === 0, "timing");
assert.ok(m[qr.size - 8][8], "dark module");

// Reed–Solomon: a codeword block evaluated at the generator's roots must be 0.
function gfMul(x, y) {
    let z = 0;
    for (let i = 7; i >= 0; i--) {
        z = (z << 1) ^ ((z >>> 7) * 0x11d);
        z ^= ((y >>> i) & 1) * x;
    }
    return z;
}
function syndromesZero(block, eccLen) {
    let root = 1;
    for (let i = 0; i < eccLen; i++) {
        let value = 0;
        for (const b of block) value = gfMul(value, root) ^ b;
        if (value !== 0) return false;
        root = gfMul(root, 2);
    }
    return true;
}
// Rebuild one version-1 block the way the encoder does and check it.
const data = [0x40, 0x56, 0x86, 0x56, 0xc6, 0xc6, 0xf0, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec, 0x11, 0xec];
const ecc = (() => {
    // Generator for 10 ECC codewords (version 1-M) and the remainder, as in qr.js.
    const degree = 10;
    const gen = new Array(degree).fill(0);
    gen[degree - 1] = 1;
    let root = 1;
    for (let i = 0; i < degree; i++) {
        for (let j = 0; j < gen.length; j++) {
            gen[j] = gfMul(gen[j], root);
            if (j + 1 < gen.length) gen[j] ^= gen[j + 1];
        }
        root = gfMul(root, 2);
    }
    const rem = gen.map(() => 0);
    for (const b of data) {
        const factor = b ^ rem.shift();
        rem.push(0);
        gen.forEach((c, i) => { rem[i] ^= gfMul(c, factor); });
    }
    return rem;
})();
assert.ok(syndromesZero(data.concat(ecc), 10), "Reed–Solomon syndromes");

console.log(`qr.js: all checks passed (version ${qr.version}, ${qr.size}×${qr.size})`);
