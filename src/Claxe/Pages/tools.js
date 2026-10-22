// Sorune mini tools panel. Everything runs in this page; nothing typed here is sent anywhere
// (the only message to the app asks for the open page's address, for the QR tool).
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Sorune sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);
const send = (message) => { if (host) host.postMessage(message); };

function toast(text) {
    const node = $("toast");
    node.textContent = text;
    node.hidden = false;
    clearTimeout(toast.timer);
    toast.timer = setTimeout(() => { node.hidden = true; }, 1600);
}

async function copy(text) {
    try {
        await navigator.clipboard.writeText(text);
        toast(t("Kopyalandı"));
    } catch {
        toast(t("Kopyalanamadı"));
    }
}

function setStatus(id, text, kind) {
    const node = $(id);
    node.textContent = text;
    node.className = "status" + (kind ? " " + kind : "");
}

// ---------------------------------------------------------------- tabs

function showTool(name) {
    for (const button of $("tabs").querySelectorAll("button")) {
        button.setAttribute("aria-selected", String(button.dataset.tool === name));
    }
    for (const section of document.querySelectorAll(".tool")) section.hidden = section.id !== `tool-${name}`;
    try { localStorage.setItem("sorune-tool", name); } catch { /* storage off: fine */ }
    if (name === "qr") renderQr();
}

$("tabs").addEventListener("click", (event) => {
    const button = event.target.closest("button");
    if (button) showTool(button.dataset.tool);
});

// ---------------------------------------------------------------- JSON

function jsonError(error, text) {
    const match = /position (\d+)/.exec(error.message);
    if (!match) return error.message;
    const pos = Number(match[1]);
    const before = text.slice(0, pos);
    const line = before.split("\n").length;
    const column = pos - before.lastIndexOf("\n");
    return t("Satır {0}, sütun {1}: {2}", line, column, error.message);
}

function jsonTransform(indent) {
    const text = $("json-input").value;
    if (!text.trim()) {
        setStatus("json-status", "");
        return;
    }
    try {
        const value = JSON.parse(text);
        $("json-input").value = JSON.stringify(value, null, indent);
        setStatus("json-status", t("Geçerli JSON"), "ok");
    } catch (error) {
        setStatus("json-status", jsonError(error, text), "bad");
    }
}

$("json-format").addEventListener("click", () => jsonTransform(2));
$("json-minify").addEventListener("click", () => jsonTransform(0));
$("json-copy").addEventListener("click", () => copy($("json-input").value));
$("json-input").addEventListener("input", () => {
    const text = $("json-input").value;
    if (!text.trim()) { setStatus("json-status", ""); return; }
    try {
        JSON.parse(text);
        setStatus("json-status", t("Geçerli JSON"), "ok");
    } catch (error) {
        setStatus("json-status", jsonError(error, text), "bad");
    }
});

// ---------------------------------------------------------------- colour

function parseColor(text) {
    text = text.trim().toLowerCase();
    let m = /^#?([0-9a-f]{3}|[0-9a-f]{6})$/.exec(text);
    if (m) {
        let hex = m[1];
        if (hex.length === 3) hex = hex.split("").map((c) => c + c).join("");
        return [0, 2, 4].map((i) => parseInt(hex.slice(i, i + 2), 16));
    }
    m = /^rgba?\(\s*(\d{1,3})[\s,]+(\d{1,3})[\s,]+(\d{1,3})/.exec(text);
    if (m) {
        const rgb = m.slice(1, 4).map(Number);
        return rgb.every((v) => v <= 255) ? rgb : null;
    }
    m = /^hsla?\(\s*(-?\d+(?:\.\d+)?)(?:deg)?[\s,]+(\d+(?:\.\d+)?)%[\s,]+(\d+(?:\.\d+)?)%/.exec(text);
    if (m) return hslToRgb(Number(m[1]), Number(m[2]) / 100, Number(m[3]) / 100);
    return null;
}

function hslToRgb(h, s, l) {
    h = ((h % 360) + 360) % 360;
    const c = (1 - Math.abs(2 * l - 1)) * s;
    const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
    const m = l - c / 2;
    const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
    return [r, g, b].map((v) => Math.round((v + m) * 255));
}

function rgbToHsl([r, g, b]) {
    r /= 255; g /= 255; b /= 255;
    const max = Math.max(r, g, b), min = Math.min(r, g, b);
    const l = (max + min) / 2;
    if (max === min) return [0, 0, Math.round(l * 100)];
    const d = max - min;
    const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    const h = max === r ? (g - b) / d + (g < b ? 6 : 0) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
    return [Math.round(h * 60), Math.round(s * 100), Math.round(l * 100)];
}

function showColor(rgb, fromPicker) {
    const hex = "#" + rgb.map((v) => v.toString(16).padStart(2, "0")).join("");
    const [h, s, l] = rgbToHsl(rgb);
    $("color-swatch").style.background = hex;
    if (!fromPicker) $("color-picker").value = hex;
    const values = [["HEX", hex.toUpperCase()], ["RGB", `rgb(${rgb.join(", ")})`], ["HSL", `hsl(${h}, ${s}%, ${l}%)`]];
    $("color-values").replaceChildren(...values.map(([name, value]) => {
        const row = document.createElement("div");
        row.className = "value";
        const label = document.createElement("span");
        label.className = "name";
        label.textContent = name;
        const code = document.createElement("code");
        code.textContent = value;
        const button = document.createElement("button");
        button.type = "button";
        button.className = "chip";
        button.textContent = t("Kopyala");
        button.addEventListener("click", () => copy(value));
        row.append(label, code, button);
        return row;
    }));
}

$("color-picker").addEventListener("input", () => {
    const rgb = parseColor($("color-picker").value);
    $("color-input").value = $("color-picker").value;
    setStatus("color-status", "");
    showColor(rgb, true);
});
$("color-input").addEventListener("input", () => {
    const rgb = parseColor($("color-input").value);
    if (rgb) {
        setStatus("color-status", "");
        showColor(rgb, false);
    } else if ($("color-input").value.trim()) {
        setStatus("color-status", t("Tanınmayan renk. HEX, rgb() ya da hsl() yaz."), "bad");
    }
});

// ---------------------------------------------------------------- QR

let qrTimer = 0;

function renderQr() {
    const text = $("qr-input").value;
    const canvas = $("qr-canvas");
    $("qr-download").hidden = $("qr-copy").hidden = true;
    if (!text) {
        canvas.width = canvas.height = 0;
        setStatus("qr-status", t("Metin yazınca QR kod burada belirir."));
        return;
    }
    try {
        const qr = QrCode.encode(text, $("qr-ecl").value);
        QrCode.draw(canvas, qr, 520);
        setStatus("qr-status", t("Sürüm {0} · {1}×{1} kare · {2} bayt", qr.version, qr.size, new TextEncoder().encode(text).length), "ok");
        $("qr-download").href = canvas.toDataURL("image/png");
        $("qr-download").hidden = false;
        $("qr-copy").hidden = !window.ClipboardItem;
    } catch {
        canvas.width = canvas.height = 0;
        setStatus("qr-status", t("Metin bir QR koda sığmayacak kadar uzun. Kısalt ya da hata düzeltmeyi düşür."), "bad");
    }
}

$("qr-input").addEventListener("input", () => {
    clearTimeout(qrTimer);
    qrTimer = setTimeout(renderQr, 150);
});
$("qr-ecl").addEventListener("change", renderQr);
$("qr-current").addEventListener("click", () => send({ type: "tools:currentUrl" }));
$("qr-copy").addEventListener("click", () => {
    $("qr-canvas").toBlob(async (blob) => {
        try {
            await navigator.clipboard.write([new ClipboardItem({ "image/png": blob })]);
            toast(t("Kopyalandı"));
        } catch {
            toast(t("Kopyalanamadı"));
        }
    });
});

// ---------------------------------------------------------------- stopwatch + timer

const pad = (n, len = 2) => String(n).padStart(len, "0");
const sw = { running: false, start: 0, elapsed: 0, laps: [] };
let swFrame = 0;

function formatStopwatch(ms) {
    const total = Math.floor(ms / 10);
    const cs = total % 100, s = Math.floor(total / 100) % 60, m = Math.floor(total / 6000) % 60, h = Math.floor(total / 360000);
    return (h ? `${h}:${pad(m)}` : pad(m)) + `:${pad(s)}.${pad(cs)}`;
}

function swNow() { return sw.elapsed + (sw.running ? performance.now() - sw.start : 0); }

function swTick() {
    $("sw-display").textContent = formatStopwatch(swNow());
    if (sw.running) swFrame = requestAnimationFrame(swTick);
}

$("sw-start").addEventListener("click", () => {
    if (sw.running) {
        sw.elapsed = swNow();
        sw.running = false;
        cancelAnimationFrame(swFrame);
    } else {
        sw.start = performance.now();
        sw.running = true;
        swTick();
    }
    $("sw-start").textContent = sw.running ? t("Durdur") : t("Başlat");
    $("sw-lap").disabled = !sw.running;
    $("sw-display").classList.toggle("running", sw.running);
});
$("sw-lap").addEventListener("click", () => {
    const li = document.createElement("li");
    li.textContent = formatStopwatch(swNow());
    $("sw-laps").prepend(li);
});
$("sw-reset").addEventListener("click", () => {
    sw.running = false;
    sw.elapsed = 0;
    cancelAnimationFrame(swFrame);
    $("sw-laps").replaceChildren();
    $("sw-start").textContent = t("Başlat");
    $("sw-lap").disabled = true;
    $("sw-display").classList.remove("running");
    swTick();
});

const timer = { running: false, end: 0, left: 300000, handle: 0 };

function timerTotal() {
    const minutes = Math.max(0, Math.min(999, parseInt($("timer-min").value, 10) || 0));
    const seconds = Math.max(0, Math.min(59, parseInt($("timer-sec").value, 10) || 0));
    return (minutes * 60 + seconds) * 1000;
}

function showTimer(ms) {
    const s = Math.ceil(ms / 1000);
    $("timer-display").textContent = `${pad(Math.floor(s / 60))}:${pad(s % 60)}`;
}

function chime() {
    try {
        const audio = new AudioContext();
        [0, 0.3, 0.6].forEach((at) => {
            const osc = audio.createOscillator();
            const gain = audio.createGain();
            osc.frequency.value = 880;
            gain.gain.setValueAtTime(0.0001, audio.currentTime + at);
            gain.gain.exponentialRampToValueAtTime(0.2, audio.currentTime + at + 0.02);
            gain.gain.exponentialRampToValueAtTime(0.0001, audio.currentTime + at + 0.24);
            osc.connect(gain).connect(audio.destination);
            osc.start(audio.currentTime + at);
            osc.stop(audio.currentTime + at + 0.26);
        });
        setTimeout(() => audio.close(), 1300);
    } catch { /* no audio device */ }
}

function timerTick() {
    const left = Math.max(0, timer.end - Date.now());
    showTimer(left);
    if (left === 0) {
        clearInterval(timer.handle);
        timer.running = false;
        timer.left = timerTotal();
        $("timer-start").textContent = t("Başlat");
        $("timer-display").classList.remove("running");
        $("timer-display").classList.add("done");
        chime();
        toast(t("Süre doldu!"));
    }
}

$("timer-start").addEventListener("click", () => {
    $("timer-display").classList.remove("done");
    if (timer.running) {
        timer.left = Math.max(0, timer.end - Date.now());
        timer.running = false;
        clearInterval(timer.handle);
    } else {
        if (timer.left <= 0) timer.left = timerTotal();
        if (timer.left <= 0) return;
        timer.end = Date.now() + timer.left;
        timer.running = true;
        timer.handle = setInterval(timerTick, 200);
        timerTick();
    }
    $("timer-start").textContent = timer.running ? t("Duraklat") : t("Devam");
    $("timer-display").classList.toggle("running", timer.running);
});
$("timer-reset").addEventListener("click", () => {
    clearInterval(timer.handle);
    timer.running = false;
    timer.left = timerTotal();
    showTimer(timer.left);
    $("timer-start").textContent = t("Başlat");
    $("timer-display").classList.remove("running", "done");
});
for (const id of ["timer-min", "timer-sec"]) {
    $(id).addEventListener("input", () => {
        if (timer.running) return;
        timer.left = timerTotal();
        showTimer(timer.left);
        $("timer-start").textContent = t("Başlat");
    });
}

// ---------------------------------------------------------------- counter

function renderCount() {
    const text = $("count-input").value;
    const words = text.trim() ? text.trim().split(/\s+/u).length : 0;
    const stats = [
        [t("Karakter"), [...text].length],
        [t("Boşluksuz"), [...text.replace(/\s/gu, "")].length],
        [t("Kelime"), words],
        [t("Satır"), text ? text.split("\n").length : 0],
        [t("Cümle"), (text.match(/[^.!?…]+[.!?…]+/gu) || []).length],
        [t("Paragraf"), text.split(/\n\s*\n/u).filter((p) => p.trim()).length],
        [t("Bayt (UTF-8)"), new TextEncoder().encode(text).length],
        [t("Okuma süresi"), t("{0} dk", Math.max(words ? 1 : 0, Math.round(words / 200)))],
    ];
    $("count-stats").replaceChildren(...stats.map(([label, value]) => {
        const box = document.createElement("div");
        box.className = "stat";
        const strong = document.createElement("strong");
        strong.textContent = typeof value === "number" ? value.toLocaleString(i18n.locale) : value;
        const span = document.createElement("span");
        span.textContent = label;
        box.append(strong, span);
        return box;
    }));
}
$("count-input").addEventListener("input", renderCount);

// ---------------------------------------------------------------- URL

function renderUrlParts(text) {
    const root = $("url-parts");
    root.replaceChildren();
    let url;
    try { url = new URL(text.trim()); } catch { return; }
    const parts = [
        [t("Protokol"), url.protocol],
        [t("Alan adı"), url.hostname],
        [t("Port"), url.port],
        [t("Yol"), decodeURIComponent(url.pathname)],
        [t("Parça"), url.hash],
    ];
    for (const [key, value] of url.searchParams) parts.push([`?${key}`, value]);
    for (const [label, value] of parts) {
        if (!value) continue;
        const dt = document.createElement("dt");
        dt.textContent = label;
        const dd = document.createElement("dd");
        dd.textContent = value;
        root.append(dt, dd);
    }
}

$("url-encode").addEventListener("click", () => {
    const text = $("url-input").value;
    $("url-input").value = $("url-full").checked ? encodeURI(text) : encodeURIComponent(text);
    setStatus("url-status", t("Kodlandı"), "ok");
    renderUrlParts($("url-input").value);
});
$("url-decode").addEventListener("click", () => {
    const text = $("url-input").value;
    try {
        $("url-input").value = $("url-full").checked ? decodeURI(text) : decodeURIComponent(text.replace(/\+/g, " "));
        setStatus("url-status", t("Çözüldü"), "ok");
    } catch {
        setStatus("url-status", t("Geçersiz % dizisi var; çözülemedi."), "bad");
    }
    renderUrlParts($("url-input").value);
});
$("url-copy").addEventListener("click", () => copy($("url-input").value));
$("url-input").addEventListener("input", () => renderUrlParts($("url-input").value));

// ---------------------------------------------------------------- app messages

function hexToRgb(hex) {
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex || "");
    return m ? `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}` : null;
}

if (host) {
    host.addEventListener("message", (event) => {
        const message = event.data || {};
        if (message.type === "config") {
            const theme = message.theme || {};
            const root = document.documentElement.style;
            if (theme.accent) root.setProperty("--accent", theme.accent);
            if (theme.accentSecondary) root.setProperty("--accent2", theme.accentSecondary);
            const overlay = hexToRgb(theme.overlay);
            if (overlay) root.setProperty("--overlay", overlay);
        } else if (message.type === "toolsCurrentUrl") {
            if (message.url) {
                $("qr-input").value = message.url;
                renderQr();
            } else {
                toast(t("Açık bir web sayfası yok"));
            }
        }
    });
}

// ---------------------------------------------------------------- start

let first = "json";
try { first = localStorage.getItem("sorune-tool") || "json"; } catch { /* storage off */ }
showTool(document.querySelector(`[data-tool="${first}"]`) ? first : "json");
showColor(parseColor("#b583ff"), false);
$("color-input").value = "#b583ff";
renderCount();
showTimer(timer.left);
swTick();
send({ type: "ready" });
