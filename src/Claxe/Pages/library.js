// Claxe library: history, bookmarks and downloads. Data lives in the app; this page
// asks for it and renders. Every action is a message the app validates.
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Sorune sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);
const VIEWS = { "gecmis": "Geçmiş", "yer-imleri": "Yer imleri", "indirmeler": "İndirmeler", "zaman": "Zaman makinesi" };

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
        else if (key === "text") node.textContent = t(value);
        else if (key.startsWith("on")) node.addEventListener(key.slice(2), value);
        else node.setAttribute(key, ["title", "aria-label", "placeholder"].includes(key) ? t(value) : value);
    }
    node.append(...children);
    return node;
}

function hostOf(url) {
    try { return new URL(url).host.replace(/^www\./, ""); } catch { return url; }
}

function monogram(text) {
    const icon = el("span", { class: "mono-icon", text: (text.trim()[0] || "?").toLocaleUpperCase("tr-TR") });
    // Each site gets its own steady hue, so a long list is easier to scan.
    let hash = 0;
    for (const ch of text) hash = (hash * 31 + ch.codePointAt(0)) | 0;
    icon.style.setProperty("--hue", String(Math.abs(hash) % 360));
    return icon;
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
    document.title = t(VIEWS[view]);

    if (view === "gecmis") send({ type: "history", text: $("history-search").value });
    if (view === "yer-imleri") send({ type: "bookmarks" });
    if (view === "indirmeler") send({ type: "downloads" });
    if (view === "zaman") send({ type: "tm:list" });
}

window.addEventListener("hashchange", showView);

// ---------------------------------------------------------------- history

const dayFormat = new Intl.DateTimeFormat(i18n.locale, { day: "numeric", month: "long", year: "numeric", weekday: "long" });
const timeFormat = new Intl.DateTimeFormat(i18n.locale, { hour: "2-digit", minute: "2-digit" });

function dayLabel(date) {
    const today = new Date();
    const yesterday = new Date();
    yesterday.setDate(today.getDate() - 1);
    if (date.toDateString() === today.toDateString()) return t("Bugün");
    if (date.toDateString() === yesterday.toDateString()) return t("Dün");
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
                type: "button", class: "icon-btn danger", title: "Geçmişten kaldır", "aria-label": t("{0} geçmişten kaldır", title), text: "×",
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
    const label = el("span", { text: folder ? `📁 ${folder} (${count})` : t("Klasörsüz ({0})", count) });
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
                type: "button", class: "icon-btn danger", title: "Yer imini sil", "aria-label": t("{0} yer imini sil", bookmark.title), text: "×",
                onclick: () => send({ type: "removeBookmark", id: bookmark.id }),
            }),
        ]);
    }
}

$("bookmark-search").addEventListener("input", renderBookmarks);

// ---------------------------------------------------------------- downloads

const sizeFormat = new Intl.NumberFormat(i18n.locale, { maximumFractionDigits: 1 });

function formatSize(bytes) {
    if (bytes == null) return "?";
    const units = ["B", "KB", "MB", "GB"];
    let value = bytes;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) { value /= 1024; unit++; }
    return `${sizeFormat.format(value)} ${units[unit]}`;
}

function formatDuration(seconds) {
    seconds = Math.max(1, Math.round(seconds));
    if (seconds < 60) return t("{0} sn", seconds);
    const minutes = Math.round(seconds / 60);
    if (minutes < 60) return t("{0} dk", minutes);
    return t("{0} sa {1} dk", Math.floor(minutes / 60), minutes % 60);
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
            const parts = [];
            if (d.status === "paused") parts.push(t("Duraklatıldı"));
            if (known) parts.push(`%${Math.floor((d.receivedBytes / d.totalBytes) * 100)}`);
            parts.push(`${formatSize(d.receivedBytes)} / ${known ? formatSize(d.totalBytes) : t("bilinmiyor")}`);
            if (d.status === "inProgress" && d.speed > 0) parts.push(`${formatSize(d.speed)}/s`);
            if (d.remainingSeconds != null) parts.push(t("{0} kaldı", formatDuration(d.remainingSeconds)));
            children.push(el("span", { class: "item-sub status", text: parts.join(" · ") }));
        } else {
            let status = "";
            let cls = "item-sub status";
            if (d.status === "completed") { status = missing ? t("Dosya taşınmış ya da silinmiş") : `${formatSize(d.totalBytes)} · ${hostOf(d.url)}`; cls += " done"; }
            if (d.status === "cancelled") { status = t("İptal edildi"); cls += " done"; }
            if (d.status === "failed") { status = d.error || t("Başarısız"); cls += " failed"; }
            children.push(el("span", { class: cls, text: status }));
        }

        const actions = [];
        if (d.status === "inProgress") actions.push(["Duraklat", "pause"], ["İptal", "cancel"]);
        // "Continue" only when WebView2 says the download can really resume.
        if (d.status === "paused") actions.push(...(d.canResume ? [["Devam et", "resume"]] : []), ["İptal", "cancel"]);
        if (d.status === "failed" && d.canResume) actions.push(["Devam et", "resume"]);
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

// ---------------------------------------------------------------- time machine

const timeOnly = new Intl.DateTimeFormat(i18n.locale, { hour: "2-digit", minute: "2-digit" });
const sessionDate = new Intl.DateTimeFormat(i18n.locale, { weekday: "long", day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
const openSessions = new Set();

function renderTimeMachine(state) {
    const closedRoot = $("tm-closed");
    closedRoot.replaceChildren();
    let lastDay = "";
    for (const tab of state.closed || []) {
        const when = new Date(tab.closedAt);
        const day = dayLabel(when);
        if (day !== lastDay) {
            closedRoot.append(el("div", { class: "day", text: day }));
            lastDay = day;
        }
        closedRoot.append(el("div", { class: "item" }, [
            el("span", { class: "time", text: timeOnly.format(when) }),
            monogram(tab.title || hostOf(tab.url)),
            el("span", { class: "item-text" }, [
                el("span", { class: "item-title", text: tab.title || tab.url, title: tab.url }),
                el("span", { class: "item-sub", text: [hostOf(tab.url), tab.group ? t("grup: {0}", tab.group) : ""].filter(Boolean).join(" · ") }),
            ]),
            el("button", { type: "button", class: "chip", text: "Geri aç", onclick: () => send({ type: "tm:reopen", id: tab.id }) }),
        ]));
    }
    $("tm-closed-empty").hidden = (state.closed || []).length > 0;

    const sessionsRoot = $("tm-sessions");
    sessionsRoot.replaceChildren();
    for (const session of state.sessions || []) {
        const expanded = openSessions.has(session.id);
        const head = el("div", { class: "item tm-session" }, [
            el("span", { class: "mono-icon", text: String(session.tabs.length) }),
            el("span", { class: "item-text" }, [
                el("span", { class: "item-title", text: sessionDate.format(new Date(session.savedAt)) }),
                el("span", { class: "item-sub", text: session.tabs.slice(0, 4).map((x) => x.title || hostOf(x.url)).join(" · ") }),
            ]),
            el("button", { type: "button", class: "chip", text: expanded ? "Gizle" : "Sekmeler", onclick: () => {
                if (expanded) openSessions.delete(session.id); else openSessions.add(session.id);
                renderTimeMachine(state);
            } }),
            el("button", { type: "button", class: "chip", text: "Hepsini aç", onclick: () => send({ type: "tm:openSession", id: session.id }) }),
            el("button", { type: "button", class: "icon-btn danger", text: "×", title: "Listeden kaldır", onclick: () => send({ type: "tm:removeSession", id: session.id }) }),
        ]);
        sessionsRoot.append(head);
        if (expanded) {
            for (const tab of session.tabs) {
                sessionsRoot.append(el("div", { class: "item tm-child" }, [
                    monogram(tab.title || hostOf(tab.url)),
                    el("span", { class: "item-text" }, [
                        el("span", { class: "item-title", text: tab.title || tab.url, title: tab.url }),
                        el("span", { class: "item-sub", text: hostOf(tab.url) }),
                    ]),
                    el("button", { type: "button", class: "chip", text: "Aç", onclick: () => send({ type: "tm:openSession", id: session.id, url: tab.url }) }),
                ]));
            }
        }
    }
    $("tm-sessions-empty").hidden = (state.sessions || []).length > 0;
}

let tmClearArmed = false;
$("tm-clear").addEventListener("click", () => {
    // Two clicks: the first one asks.
    if (!tmClearArmed) {
        tmClearArmed = true;
        $("tm-clear").textContent = t("Emin misin? Tekrar bas");
        $("tm-clear").classList.add("confirm");
        setTimeout(() => {
            tmClearArmed = false;
            $("tm-clear").textContent = t("Hepsini temizle");
            $("tm-clear").classList.remove("confirm");
        }, 3000);
        return;
    }
    tmClearArmed = false;
    send({ type: "tm:clear" });
});

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
            case "timemachine":
                renderTimeMachine(message);
                break;
        }
    });
}

send({ type: "ready" });
showView();
