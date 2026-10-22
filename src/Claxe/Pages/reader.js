// Reading mode. The article arrives as plain blocks from the app and is drawn with textContent
// only; images are https URLs shown as <img> (CSP: img-src https:). Nothing from the site runs here.
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Sorune sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);
const send = (message) => { if (host) host.postMessage(message); };

// Per-viewer preferences: look, font, size, width (local only).
const prefs = { look: "theme", serif: false, size: 19, wide: false };
try { Object.assign(prefs, JSON.parse(localStorage.getItem("sorune-reader") || "{}")); } catch { /* defaults */ }

function savePrefs() {
    try { localStorage.setItem("sorune-reader", JSON.stringify(prefs)); } catch { /* storage off */ }
}

function applyPrefs() {
    document.body.className = `look-${prefs.look}` + (prefs.serif ? " serif" : "") + (prefs.wide ? " wide" : "");
    document.documentElement.style.setProperty("--size", `${prefs.size}px`);
    for (const button of $("looks").querySelectorAll("button")) {
        button.setAttribute("aria-checked", String(button.dataset.look === prefs.look));
    }
}

$("looks").addEventListener("click", (event) => {
    const button = event.target.closest("button");
    if (!button) return;
    prefs.look = button.dataset.look;
    applyPrefs();
    savePrefs();
});
$("font").addEventListener("click", () => { prefs.serif = !prefs.serif; applyPrefs(); savePrefs(); });
$("smaller").addEventListener("click", () => { prefs.size = Math.max(14, prefs.size - 1); applyPrefs(); savePrefs(); });
$("bigger").addEventListener("click", () => { prefs.size = Math.min(28, prefs.size + 1); applyPrefs(); savePrefs(); });
$("width").addEventListener("click", () => { prefs.wide = !prefs.wide; applyPrefs(); savePrefs(); });
$("back").addEventListener("click", () => send({ type: "reader:back" }));

function render(article) {
    const root = $("article");
    root.replaceChildren();
    if (!article || !Array.isArray(article.blocks) || article.blocks.length === 0) {
        $("empty").hidden = false;
        return;
    }

    document.title = article.title || t("Okuma modu");
    $("title").textContent = article.title || "";
    $("site").textContent = article.site || "";
    const words = article.blocks.filter((b) => b.text).reduce((n, b) => n + b.text.split(/\s+/u).length, 0);
    $("meta").textContent = [article.byline, t("{0} dk okuma", Math.max(1, Math.round(words / 200)))].filter(Boolean).join(" · ");

    let list = null;
    for (const block of article.blocks) {
        if (block.t !== "li") list = null;
        let node;
        switch (block.t) {
            case "h2":
            case "h3":
                // The page title is shown once at the top.
                if (block.text === article.title) continue;
                node = document.createElement(block.t);
                node.textContent = block.text;
                break;
            case "p":
                node = document.createElement("p");
                node.textContent = block.text;
                break;
            case "li":
                if (!list || list.tagName !== (block.ordered ? "OL" : "UL")) {
                    list = document.createElement(block.ordered ? "ol" : "ul");
                    root.append(list);
                }
                node = document.createElement("li");
                node.textContent = block.text;
                list.append(node);
                continue;
            case "quote":
                node = document.createElement("blockquote");
                node.textContent = block.text;
                break;
            case "pre":
                node = document.createElement("pre");
                node.textContent = block.text;
                break;
            case "caption":
                node = document.createElement("p");
                node.className = "caption";
                node.textContent = block.text;
                break;
            case "img":
                if (typeof block.src !== "string" || !block.src.startsWith("https://")) continue;
                node = document.createElement("img");
                node.src = block.src;
                node.alt = block.alt || "";
                node.loading = "lazy";
                node.referrerPolicy = "no-referrer";
                node.addEventListener("error", () => node.remove(), { once: true });
                break;
            default:
                continue;
        }
        root.append(node);
    }
}

function hexToRgb(hex) {
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex || "");
    return m ? `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}` : null;
}

if (host) {
    host.addEventListener("message", (event) => {
        const message = event.data || {};
        if (message.type === "article") {
            render(message.article);
        } else if (message.type === "config") {
            const theme = message.theme || {};
            const root = document.documentElement.style;
            if (theme.accent) root.setProperty("--accent", theme.accent);
            const overlay = hexToRgb(theme.overlay);
            if (overlay) root.setProperty("--overlay", overlay);
            if (theme.wallpaper) {
                root.setProperty("--wallpaper", `url("${theme.wallpaper}")`);
                $("wall").classList.add("ready");
            }
        }
    });
}

applyPrefs();
send({ type: "ready" });
send({ type: "reader:ready" });
