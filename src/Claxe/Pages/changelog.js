// Patch notes: every Sorune release, newest first. Opened from the mini tools panel.
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Sorune sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);
const english = window.i18n && window.i18n.lang === "en";

function send(message) {
    if (host) host.postMessage(message);
}

const RELEASES = window.RELEASES || [];

/** "**bold**" parts become <strong>; everything else stays plain text (no HTML from strings). */
function richText(text) {
    const fragment = document.createDocumentFragment();
    text.split("**").forEach((part, index) => {
        if (!part) return;
        if (index % 2) {
            const strong = document.createElement("strong");
            strong.textContent = part;
            fragment.append(strong);
        } else {
            fragment.append(document.createTextNode(part));
        }
    });
    return fragment;
}

function pick(pair) {
    return english ? pair[1] : pair[0];
}

function anchor(version) {
    return "v" + version.replace(/[^0-9a-z]+/gi, "-");
}

function formatDate(iso) {
    const date = new Date(iso + "T12:00:00");
    return date.toLocaleDateString((window.i18n && window.i18n.locale) || "tr-TR", { day: "numeric", month: "long", year: "numeric" });
}

function render(current) {
    const jump = $("jump");
    const list = $("releases");
    jump.replaceChildren();
    list.replaceChildren();

    RELEASES.forEach((release, index) => {
        const link = document.createElement("a");
        link.href = "#" + anchor(release.version);
        link.textContent = release.version;
        if (release.version === current) link.className = "current";
        jump.append(link);

        const card = document.createElement("article");
        card.className = "release" + (index === 0 ? " latest" : "");
        card.id = anchor(release.version);
        card.style.setProperty("--delay", `${Math.min(index, 6) * 0.05}s`);

        const head = document.createElement("header");
        head.className = "release-head";
        const title = document.createElement("h2");
        title.textContent = release.version;
        const name = document.createElement("span");
        name.className = "name";
        name.textContent = pick(release.name);
        head.append(title, name);
        if (release.version === current) {
            const badge = document.createElement("span");
            badge.className = "badge";
            badge.textContent = t("Kullandığın sürüm");
            head.append(badge);
        }
        const date = document.createElement("span");
        date.className = "date";
        date.textContent = formatDate(release.date);
        head.append(date);

        const items = document.createElement("ul");
        for (const item of release.items) {
            const li = document.createElement("li");
            li.append(richText(pick(item)));
            items.append(li);
        }

        card.append(head, items);
        list.append(card);
    });
}

function hexToRgb(hex) {
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex || "");
    return m ? `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}` : null;
}

if (host) {
    host.addEventListener("message", (event) => {
        const message = event.data || {};
        if (message.type !== "config") return;
        const theme = message.theme || {};
        const root = document.documentElement.style;
        if (theme.accent) root.setProperty("--accent", theme.accent);
        if (theme.accentSecondary) root.setProperty("--accent2", theme.accentSecondary);
        const overlay = hexToRgb(theme.overlay);
        if (overlay) root.setProperty("--overlay", overlay);
        if (theme.wallpaper) {
            root.setProperty("--wallpaper", `url("${theme.wallpaper}")`);
            $("wall").classList.add("ready");
        }
        document.body.classList.toggle("motion", message.animations !== false);
        // The config comes again on every look change: re-render only for a new version.
        const version = message.version ? String(message.version).split("+")[0] : null;
        if (version && version !== $("version").textContent) {
            $("version").textContent = version;
            render(version);
        }
    });
}

render(null);
send({ type: "ready" });
