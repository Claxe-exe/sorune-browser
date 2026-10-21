// Claxe library: history, bookmarks and downloads. Data lives in the app; this page
// asks for it and renders. Every action is a message the app validates.
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Claxe sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);
const VIEWS = { "gecmis": "Geçmiş", "yer-imleri": "Yer imleri", "indirmeler": "İndirmeler" };

let historyItems = [];
let bookmarks = [];
let downloads = [];

function send(message) {
    if (host) host.postMessage(message);
}

function el(tag, props = {}, children = []) {
    const node = document.createElement(tag);
    for (const [key, value] of Object.entries(props)) {
        if (key === "class") node.className = value;
        else if (key === "text") node.textContent = value;
        else if (key.startsWith("on")) node.addEventListener(key.slice(2), value);
        else node.setAttribute(key, value);
    }
    node.append(...children);
    return node;
}

function hostOf(url) {
    try { return new URL(url).host.replace(/^www\./, ""); } catch { return url; }
}

function monogram(text) {
    return el("span", { class: "mono-icon", text: (text.trim()[0] || "?").toLocaleUpperCase("tr-TR") });
}

function debounce(fn, ms) {
    let timer = null;
    return (...args) => { clearTimeout(timer); timer = setTimeout(() => fn(...args), ms); };
}

// ---------------------------------------------------------------- views

function currentView() {
    const hash = location.hash.slice(1);
    return VIEWS[hash] ? hash : "gecmis";
}

function showView() {
    const view = currentView();
    for (const id of Object.keys(VIEWS)) $(`view-${id}`).hidden = id !== view;
    for (const link of document.querySelectorAll(".nav a")) {
        link.classList.toggle("active", link.getAttribute("href") === `#${view}`);
    }
    document.title = VIEWS[view];

    if (view === "gecmis") send({ type: "history", text: $("history-search").value });
    if (view === "yer-imleri") send({ type: "bookmarks" });
    if (view === "indirmeler") send({ type: "downloads" });
}

window.addEventListener("hashchange", showView);

// ---------------------------------------------------------------- history

const dayFormat = new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long", year: "numeric", weekday: "long" });
const timeFormat = new Intl.DateTimeFormat("tr-TR", { hour: "2-digit", minute: "2-digit" });

function dayLabel(date) {
    const today = new Date();
    const yesterday = new Date();
    yesterday.setDate(today.getDate() - 1);
    if (date.toDateString() === today.toDateString()) return "Bugün";
    if (date.toDateString() === yesterday.toDateString()) return "Dün";
    return dayFormat.format(date);
}

function renderHistory(more) {
    const list = $("history-list");
    list.replaceChildren();
    let lastDay = "";
    for (const entry of historyItems) {
        const date = new Date(entry.visitedAt);
        const day = dayLabel(date);
        if (day !== lastDay) {
            list.append(el("h3", { class: "day", text: day }));
            lastDay = day;
        }
        const title = entry.title || hostOf(entry.url);
        list.append(el("div", { class: "item" }, [
            el("span", { class: "time", text: timeFormat.format(date) }),
            monogram(hostOf(entry.url)),
            el("span", { class: "item-text" }, [
                el("a", { class: "item-title", href: entry.url, text: title, title: entry.url }),
                el("span", { class: "item-sub", text: hostOf(entry.url) }),
            ]),
            el("button", {
                type: "button", class: "icon-btn danger", title: "Geçmişten kaldır", "aria-label": `${title} geçmişten kaldır`, text: "×",
                onclick: () => send({ type: "deleteHistory", ids: [entry.id] }),
            }),
        ]));
    }
    $("history-more").hidden = !more;
    $("history-empty").hidden = historyItems.length > 0;
}

$("history-search").addEventListener("input", debounce(() => send({ type: "history", text: $("history-search").value }), 200));
$("history-more").addEventListener("click", () => {
    const last = historyItems[historyItems.length - 1];
    if (last) send({ type: "history", text: $("history-search").value, beforeId: last.id });
});

// ---------------------------------------------------------------- bookmarks

function renderBookmarks() {
    const needle = $("bookmark-search").value.trim().toLocaleLowerCase("tr-TR");
    const items = needle
        ? bookmarks.filter((b) => (b.title + " " + b.url).toLocaleLowerCase("tr-TR").includes(needle))
        : bookmarks;

    // Folders (alphabetical) first, then bookmarks without a folder.
    const folders = [...new Set(bookmarks.map((b) => b.folder).filter(Boolean))].sort((a, b) => a.localeCompare(b, "tr"));
    const groups = [...folders.map((f) => [f, items.filter((b) => b.folder === f)]), ["", items.filter((b) => !b.folder)]]
        .filter(([, list]) => list.length > 0);

    const nodes = [];
    for (const [folder, list] of groups) {
        if (folder || folders.length > 0) nodes.push(folderHeader(folder, list.length));
        nodes.push(...list.map((bookmark) => bookmarkRow(bookmark, folders)));
    }
    $("bookmark-list").replaceChildren(...nodes);
    $("bookmark-empty").hidden = items.length > 0;
}

function folderHeader(folder, count) {
    const label = el("span", { text: folder ? `📁 ${folder} (${count})` : `Klasörsüz (${count})` });
    const header = el("h3", { class: "day folder-head" }, [label]);
    if (folder) {
        header.append(el("button", {
            type: "button", class: "icon-btn", title: "Klasörü yeniden adlandır", text: "✎",
            onclick: () => inlineInput(label, folder, (value) => send({ type: "renameBookmarkFolder", from: folder, to: value })),
        }));
    }
    return header;
}

/** Replaces a node with a text box; Enter commits, Escape cancels. */
function inlineInput(node, value, commit, options = []) {
    const input = el("input", { class: "rename", type: "text", maxlength: "200" });
    input.value = value;
    if (options.length) {
        const list = el("datalist", { id: "folder-options" }, options.map((o) => el("option", { value: o })));
        input.setAttribute("list", "folder-options");
        node.after(list);
    }
    let done = false;
    const finish = (save) => {
        if (done) return;
        done = true;
        if (save && input.value.trim() !== value) commit(input.value.trim());
        else renderBookmarks();
    };
    input.addEventListener("keydown", (e) => {
        if (e.key === "Enter") finish(true);
        if (e.key === "Escape") finish(false);
    });
    input.addEventListener("blur", () => finish(true));
    node.replaceWith(input);
    input.focus();
    input.select();
}

function bookmarkRow(bookmark, folders) {
    {
        const title = el("a", { class: "item-title", href: bookmark.url, text: bookmark.title, title: bookmark.url });
        const text = el("span", { class: "item-text" }, [title, el("span", { class: "item-sub", text: bookmark.url })]);

        const rename = () => {
            const input = el("input", { class: "rename", type: "text", maxlength: "200", "aria-label": "Yeni ad" });
            input.value = bookmark.title;
            const commit = () => {
                const value = input.value.trim();
                if (value && value !== bookmark.title) send({ type: "renameBookmark", id: bookmark.id, title: value });
                else renderBookmarks();
            };
            input.addEventListener("keydown", (e) => {
                if (e.key === "Enter") commit();
                if (e.key === "Escape") renderBookmarks();
            });
            input.addEventListener("blur", commit);
            title.replaceWith(input);
            input.focus();
            input.select();
        };

        const sub = text.lastChild;
        const move = () => inlineInput(sub, bookmark.folder || "",
            (value) => send({ type: "setBookmarkFolder", id: bookmark.id, folder: value }), folders);

        return el("div", { class: "item" }, [
            monogram(bookmark.title),
            text,
            el("button", { type: "button", class: "icon-btn", title: "Klasöre taşı (boş bırakırsan klasörden çıkar)", "aria-label": "Klasöre taşı", text: "📁", onclick: move }),
            el("button", { type: "button", class: "icon-btn", title: "Yeniden adlandır", "aria-label": "Yeniden adlandır", text: "✎", onclick: rename }),
            el("button", {
                type: "button", class: "icon-btn danger", title: "Yer imini sil", "aria-label": `${bookmark.title} yer imini sil`, text: "×",
                onclick: () => send({ type: "removeBookmark", id: bookmark.id }),
            }),
        ]);
    }
}

$("bookmark-search").addEventListener("input", renderBookmarks);

// ---------------------------------------------------------------- downloads

const sizeFormat = new Intl.NumberFormat("tr-TR", { maximumFractionDigits: 1 });

function formatSize(bytes) {
    if (bytes == null) return "?";
    const units = ["B", "KB", "MB", "GB"];
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
    return `${sizeFormat.format(value)} ${units[unit]}`;
}

function downloadAction(action, id) {
    return () => send({ type: "download", action, id });
}

function renderDownloads() {
    $("download-list").replaceChildren(...downloads.map((d) => {
        const active = d.status === "inProgress" || d.status === "paused";
        const missing = d.status === "completed" && !d.fileExists;
        const children = [el("span", { class: "item-title", text: d.fileName || hostOf(d.url), title: d.filePath || d.url })];

        if (active) {
            const known = d.totalBytes > 0;
            const bar = el("div", { class: "bar" + (known ? "" : " indeterminate") }, [el("span")]);
            bar.firstChild.style.width = known ? `${Math.min(100, (d.receivedBytes / d.totalBytes) * 100)}%` : "30%";
            children.push(bar);
            children.push(el("span", {
                class: "item-sub status",
                text: `${d.status === "paused" ? "Duraklatıldı · " : ""}${formatSize(d.receivedBytes)} / ${known ? formatSize(d.totalBytes) : "bilinmiyor"}`,
            }));
        } else {
            let status = "";
            let cls = "item-sub status";
            if (d.status === "completed") { status = missing ? "Dosya taşınmış ya da silinmiş" : `${formatSize(d.totalBytes)} · ${hostOf(d.url)}`; cls += " done"; }
            if (d.status === "cancelled") { status = "İptal edildi"; cls += " done"; }
            if (d.status === "failed") { status = d.error || "Başarısız"; cls += " failed"; }
            children.push(el("span", { class: cls, text: status }));
        }

        const actions = [];
        if (d.status === "inProgress") actions.push(["Duraklat", "pause"], ["İptal", "cancel"]);
        if (d.status === "paused") actions.push(["Devam et", "resume"], ["İptal", "cancel"]);
        if (d.status === "completed" && !missing) actions.push(["Aç", "open"], ["Klasörde göster", "showInFolder"]);
        if (!active) actions.push(["Listeden kaldır", "remove"]);
        children.push(el("div", { class: "actions" }, actions.map(([label, action]) =>
            el("button", { type: "button", class: "chip", text: label, onclick: downloadAction(action, d.id) }))));

        return el("div", { class: "item download" + (missing ? " missing" : "") }, [
            monogram(d.fileName || "?"),
            el("span", { class: "item-text" }, children),
        ]);
    }));
    $("download-empty").hidden = downloads.length > 0;
}

$("downloads-clear").addEventListener("click", () => send({ type: "download", action: "clear" }));

// ---------------------------------------------------------------- look + messages

function applyConfig(config) {
    const theme = config.theme || {};
    const root = document.documentElement.style;
    if (theme.accent) root.setProperty("--accent", theme.accent);
    if (theme.accentSecondary) root.setProperty("--accent2", theme.accentSecondary);
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(theme.overlay || "");
    if (m) root.setProperty("--overlay", `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}`);
    document.body.classList.toggle("no-motion", config.animations === false);
    if (theme.wallpaper && theme.wallpaper !== applyConfig.wallpaper) {
        applyConfig.wallpaper = theme.wallpaper;
        const img = new Image();
        img.onload = () => {
            root.setProperty("--wallpaper", `url("${theme.wallpaper}")`);
            $("wall").classList.add("ready");
        };
        img.src = theme.wallpaper;
    }
}

if (host) {
    host.addEventListener("message", (event) => {
        const message = event.data || {};
        switch (message.type) {
            case "config":
                applyConfig(message);
                break;
            case "history":
                historyItems = message.append ? historyItems.concat(message.items) : message.items;
                renderHistory(message.more);
                break;
            case "historyChanged":
                send({ type: "history", text: $("history-search").value });
                break;
            case "bookmarks":
                bookmarks = message.items || [];
                renderBookmarks();
                break;
            case "downloads":
                downloads = message.items || [];
                renderDownloads();
                break;
        }
    });
}

send({ type: "ready" });
showView();
