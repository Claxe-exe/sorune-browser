// QR code encoder (byte mode, versions 1–40, error correction L/M/Q/H). Written for Sorune after
// the ISO/IEC 18004 algorithm as laid out in Project Nayuki's reference implementation (MIT).
// Runs fully offline: the text never leaves the page.
"use strict";

window.QrCode = (() => {
    const ECC_PER_BLOCK = {
        L: [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        M: [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28],
        Q: [-1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
        H: [-1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30],
    };
    const BLOCKS = {
        L: [-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25],
        M: [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49],
        Q: [-1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68],
        H: [-1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81],
    };
    const FORMAT_BITS = { L: 1, M: 0, Q: 3, H: 2 };

    const bit = (value, i) => ((value >>> i) & 1) !== 0;

    function rawDataModules(ver) {
        let result = (16 * ver + 128) * ver + 64;
        if (ver >= 2) {
            const numAlign = Math.floor(ver / 7) + 2;
            result -= (25 * numAlign - 10) * numAlign - 55;
            if (ver >= 7) result -= 36;
        }
        return result;
    }

    function dataCodewords(ver, ecl) {
        return Math.floor(rawDataModules(ver) / 8) - ECC_PER_BLOCK[ecl][ver] * BLOCKS[ecl][ver];
    }

    // ---- Reed–Solomon over GF(2^8) with polynomial 0x11D
    function gfMultiply(x, y) {
        let z = 0;
        for (let i = 7; i >= 0; i--) {
            z = (z << 1) ^ ((z >>> 7) * 0x11D);
            z ^= ((y >>> i) & 1) * x;
        }
        return z;
    }

    function rsDivisor(degree) {
        const result = new Array(degree).fill(0);
        result[degree - 1] = 1;
        let root = 1;
        for (let i = 0; i < degree; i++) {
            for (let j = 0; j < result.length; j++) {
                result[j] = gfMultiply(result[j], root);
                if (j + 1 < result.length) result[j] ^= result[j + 1];
            }
            root = gfMultiply(root, 0x02);
        }
        return result;
    }

    function rsRemainder(data, divisor) {
        const result = divisor.map(() => 0);
        for (const b of data) {
            const factor = b ^ result.shift();
            result.push(0);
            divisor.forEach((coef, i) => { result[i] ^= gfMultiply(coef, factor); });
        }
        return result;
    }

    function addEccAndInterleave(data, ver, ecl) {
        const numBlocks = BLOCKS[ecl][ver];
        const eccLen = ECC_PER_BLOCK[ecl][ver];
        const raw = Math.floor(rawDataModules(ver) / 8);
        const numShort = numBlocks - raw % numBlocks;
        const shortLen = Math.floor(raw / numBlocks);
        const divisor = rsDivisor(eccLen);
        const blocks = [];
        for (let i = 0, k = 0; i < numBlocks; i++) {
            const dat = data.slice(k, k + shortLen - eccLen + (i < numShort ? 0 : 1));
            k += dat.length;
            const ecc = rsRemainder(dat, divisor);
            if (i < numShort) dat.push(0);
            blocks.push(dat.concat(ecc));
        }
        const result = [];
        for (let i = 0; i < blocks[0].length; i++) {
            blocks.forEach((block, j) => {
                if (i !== shortLen - eccLen || j >= numShort) result.push(block[i]);
            });
        }
        return result;
    }

    // ---- Matrix
    function encode(text, ecl = "M") {
        const bytes = Array.from(new TextEncoder().encode(text));
        let ver = 1;
        for (; ver <= 40; ver++) {
            const used = 4 + (ver < 10 ? 8 : 16) + bytes.length * 8;
            if (used <= dataCodewords(ver, ecl) * 8) break;
        }
        if (ver > 40) throw new RangeError("too long");

        const bits = [];
        const append = (value, len) => { for (let i = len - 1; i >= 0; i--) bits.push((value >>> i) & 1); };
        append(0x4, 4);
        append(bytes.length, ver < 10 ? 8 : 16);
        bytes.forEach((b) => append(b, 8));
        const capacity = dataCodewords(ver, ecl) * 8;
        append(0, Math.min(4, capacity - bits.length));
        append(0, (8 - bits.length % 8) % 8);
        for (let pad = 0xEC; bits.length < capacity; pad ^= 0xEC ^ 0x11) append(pad, 8);
        const codewords = [];
        for (let i = 0; i < bits.length; i += 8) codewords.push(bits.slice(i, i + 8).reduce((acc, b) => (acc << 1) | b, 0));

        const size = ver * 4 + 17;
        const modules = Array.from({ length: size }, () => new Array(size).fill(false));
        const isFunction = Array.from({ length: size }, () => new Array(size).fill(false));
        const set = (x, y, dark) => { modules[y][x] = dark; isFunction[y][x] = true; };

        function drawFormat(mask) {
            const data = (FORMAT_BITS[ecl] << 3) | mask;
            let rem = data;
            for (let i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >>> 9) * 0x537);
            const b = ((data << 10) | rem) ^ 0x5412;
            for (let i = 0; i <= 5; i++) set(8, i, bit(b, i));
            set(8, 7, bit(b, 6));
            set(8, 8, bit(b, 7));
            set(7, 8, bit(b, 8));
            for (let i = 9; i < 15; i++) set(14 - i, 8, bit(b, i));
            for (let i = 0; i < 8; i++) set(size - 1 - i, 8, bit(b, i));
            for (let i = 8; i < 15; i++) set(8, size - 15 + i, bit(b, i));
            set(8, size - 8, true);
        }

        // Timing patterns, finders, alignment patterns, format and version areas.
        for (let i = 0; i < size; i++) {
            set(6, i, i % 2 === 0);
            set(i, 6, i % 2 === 0);
        }
        for (const [cx, cy] of [[3, 3], [size - 4, 3], [3, size - 4]]) {
            for (let dy = -4; dy <= 4; dy++) {
                for (let dx = -4; dx <= 4; dx++) {
                    const x = cx + dx, y = cy + dy;
                    const dist = Math.max(Math.abs(dx), Math.abs(dy));
                    if (x >= 0 && x < size && y >= 0 && y < size) set(x, y, dist !== 2 && dist !== 4);
                }
            }
        }
        const align = [];
        if (ver > 1) {
            const numAlign = Math.floor(ver / 7) + 2;
            const step = Math.floor((ver * 8 + numAlign * 3 + 5) / (numAlign * 4 - 4)) * 2;
            align.push(6);
            for (let pos = size - 7; align.length < numAlign; pos -= step) align.splice(1, 0, pos);
        }
        align.forEach((ax, i) => align.forEach((ay, j) => {
            if ((i === 0 && j === 0) || (i === 0 && j === align.length - 1) || (i === align.length - 1 && j === 0)) return;
            for (let dy = -2; dy <= 2; dy++) {
                for (let dx = -2; dx <= 2; dx++) set(ax + dx, ay + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
            }
        }));
        drawFormat(0);
        if (ver >= 7) {
            let rem = ver;
            for (let i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >>> 11) * 0x1F25);
            const b = (ver << 12) | rem;
            for (let i = 0; i < 18; i++) {
                const a = size - 11 + i % 3, c = Math.floor(i / 3);
                set(a, c, bit(b, i));
                set(c, a, bit(b, i));
            }
        }

        // Data in the zigzag order.
        const all = addEccAndInterleave(codewords, ver, ecl);
        let i = 0;
        for (let right = size - 1; right >= 1; right -= 2) {
            if (right === 6) right = 5;
            for (let vert = 0; vert < size; vert++) {
                for (let j = 0; j < 2; j++) {
                    const x = right - j;
                    const upward = ((right + 1) & 2) === 0;
                    const y = upward ? size - 1 - vert : vert;
                    if (!isFunction[y][x] && i < all.length * 8) {
                        modules[y][x] = bit(all[i >>> 3], 7 - (i & 7));
                        i++;
                    }
                }
            }
        }

        const maskRule = [
            (x, y) => (x + y) % 2 === 0,
            (x, y) => y % 2 === 0,
            (x) => x % 3 === 0,
            (x, y) => (x + y) % 3 === 0,
            (x, y) => (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0,
            (x, y) => (x * y) % 2 + (x * y) % 3 === 0,
            (x, y) => ((x * y) % 2 + (x * y) % 3) % 2 === 0,
            (x, y) => (((x + y) % 2) + (x * y) % 3) % 2 === 0,
        ];
        const applyMask = (m) => {
            for (let y = 0; y < size; y++) {
                for (let x = 0; x < size; x++) {
                    if (!isFunction[y][x] && maskRule[m](x, y)) modules[y][x] = !modules[y][x];
                }
            }
        };
        // Simplified penalty (runs, 2×2 blocks, balance): any mask is valid, this only helps scanners.
        const penalty = () => {
            let score = 0, dark = 0;
            for (let a = 0; a < size; a++) {
                let runRow = 1, runCol = 1;
                for (let b = 1; b < size; b++) {
                    if (modules[a][b] === modules[a][b - 1]) runRow++; else { if (runRow >= 5) score += runRow - 2; runRow = 1; }
                    if (modules[b][a] === modules[b - 1][a]) runCol++; else { if (runCol >= 5) score += runCol - 2; runCol = 1; }
                }
                if (runRow >= 5) score += runRow - 2;
                if (runCol >= 5) score += runCol - 2;
            }
            for (let y = 0; y < size; y++) {
                for (let x = 0; x < size; x++) {
                    if (modules[y][x]) dark++;
                    if (x < size - 1 && y < size - 1) {
                        const c = modules[y][x];
                        if (c === modules[y][x + 1] && c === modules[y + 1][x] && c === modules[y + 1][x + 1]) score += 3;
                    }
                }
            }
            const total = size * size;
            score += Math.floor(Math.abs(dark * 20 - total * 10) / total) * 10;
            return score;
        };
        let best = 0, bestScore = Infinity;
        for (let m = 0; m < 8; m++) {
            applyMask(m);
            drawFormat(m);
            const score = penalty();
            if (score < bestScore) { best = m; bestScore = score; }
            applyMask(m);
        }
        applyMask(best);
        drawFormat(best);
        return { size, version: ver, modules };
    }

    /** Draws the code with a 4-module quiet zone; dark on light for any scanner. */
    function draw(canvas, qr, pixels) {
        const quiet = 4;
        const scale = Math.max(1, Math.floor(pixels / (qr.size + quiet * 2)));
        const full = (qr.size + quiet * 2) * scale;
        canvas.width = canvas.height = full;
        const ctx = canvas.getContext("2d");
        ctx.fillStyle = "#ffffff";
        ctx.fillRect(0, 0, full, full);
        ctx.fillStyle = "#000000";
        for (let y = 0; y < qr.size; y++) {
            for (let x = 0; x < qr.size; x++) {
                if (qr.modules[y][x]) ctx.fillRect((x + quiet) * scale, (y + quiet) * scale, scale, scale);
            }
        }
    }

    return { encode, draw, dataCodewords };
})();
