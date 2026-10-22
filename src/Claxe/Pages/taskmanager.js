// Task manager: live CPU / memory per tab (data from the app every 2 seconds).
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Sorune sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);
const send = (message) => { if (host) host.postMessage(message); };

const KIND = {
    sorune: "Sorune arayüzü",
    browser: "Tarayıcı çekirdeği",
    gpu: "Grafik (GPU)",
    utility: "Yardımcı işlem (ağ, ses…)",
    extension: "Eklenti",
    renderer: "Arka plan sayfası",
    other: "Diğer",
};

let sort = { key: "memory", dir: -1 };
let last = null;

function formatBytes(bytes) {
    if (!bytes) return "–";
    const mb = bytes / 1048576;
    return mb >= 1024 ? `${(mb / 1024).toLocaleString(i18n.locale, { maximumFractionDigits: 2 })} GB` : `${Math.round(mb).toLocaleString(i18n.locale)} MB`;
}

function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined) node.textContent = text;
    return node;
}

function chip(label, onClick, danger) {
    const button = el("button", "chip" + (danger ? " danger" : ""), t(label));
    button.type = "button";
    button.addEventListener("click", onClick);
    return button;
}

function rowName(row) {
    if (row.members.length) return row.members.map((m) => m.title).join(" · ");
    return t(KIND[row.kind] || "Diğer") + (row.detail ? ` (${row.detail})` : "");
}

function render() {
    if (!last) return;
    const rows = [...last.rows];
    const value = (row) => sort.key === "name" ? rowName(row).toLocaleLowerCase(i18n.locale) : row[sort.key];
    rows.sort((a, b) => (value(a) > value(b) ? 1 : value(a) < value(b) ? -1 : 0) * sort.dir);

    const maxMemory = Math.max(...rows.map((r) => r.memory), 1);
    const body = $("rows");
    body.replaceChildren();
    for (const row of rows) {
        const tr = el("tr", row.kind === "page" ? "page" : "system");

        const name = el("td", "name");
        if (row.members.length) {
            for (const member of row.members) {
                const line = el("div", "member");
                const badge = el("span", "badge " + member.kind, t(member.kind === "panel" ? "Panel" : member.isPrivate ? "Gizli" : "Sekme"));
                const title = el("span", "title", member.title || member.url || "");
                title.title = member.url || "";
                line.append(badge, title);
                if (member.sleeping) line.append(el("span", "sleep", t("uyuyor 💤")));
                name.append(line);
            }
        } else {
            name.append(el("div", "member", rowName(row)));
        }

        const memory = el("td", "num");
        const bar = el("div", "bar");
        const fill = el("span");
        fill.style.width = `${Math.round((row.memory / maxMemory) * 100)}%`;
        bar.append(fill);
        memory.append(el("span", "", formatBytes(row.memory)), bar);

        const cpu = el("td", "num" + (row.cpu >= 25 ? " hot" : ""), `%${row.cpu.toLocaleString(i18n.locale, { maximumFractionDigits: 1 })}`);
        const pid = el("td", "num dim", String(row.pid));

        const actions = el("td", "actions");
        const tab = row.members.find((m) => m.kind === "tab" && !(m.url || "").includes("/taskmanager.html"));
        if (tab) {
            actions.append(chip("Göster", () => send({ type: "taskmgr:show", id: tab.id })));
            if (!tab.sleeping) actions.append(chip("Uyut", () => send({ type: "taskmgr:sleep", id: tab.id })));
            actions.append(chip("Kapat", () => {
                for (const member of row.members.filter((m) => m.kind === "tab")) send({ type: "taskmgr:close", id: member.id });
            }, true));
        }

        tr.append(name, memory, cpu, pid, actions);
        body.append(tr);
    }

    const totalMemory = rows.reduce((n, r) => n + r.memory, 0);
    const totalCpu = rows.reduce((n, r) => n + r.cpu, 0);
    const tabs = rows.flatMap((r) => r.members.filter((m) => m.kind === "tab"));
    $("sum-memory").textContent = formatBytes(totalMemory);
    $("sum-cpu").textContent = `%${Math.min(100, totalCpu).toLocaleString(i18n.locale, { maximumFractionDigits: 1 })}`;
    $("sum-tabs").textContent = `${tabs.length} · ${tabs.filter((m) => m.sleeping).length}`;
    $("waiting").hidden = true;

    for (const th of document.querySelectorAll("th[data-sort]")) {
        th.classList.toggle("sorted", th.dataset.sort === sort.key);
        th.dataset.dir = sort.dir > 0 ? "▲" : "▼";
    }
}

for (const th of document.querySelectorAll("th[data-sort]")) {
    th.addEventListener("click", () => {
        sort = th.dataset.sort === sort.key ? { key: sort.key, dir: -sort.dir } : { key: th.dataset.sort, dir: th.dataset.sort === "name" ? 1 : -1 };
        render();
    });
}

function hexToRgb(hex) {
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex || "");
    return m ? `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}` : null;
}

if (host) {
    host.addEventListener("message", (event) => {
        const message = event.data || {};
        if (message.type === "taskmanager") {
            last = message;
            render();
        } else if (message.type === "config") {
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
        }
    });
}

// The app samples only while this page is open; coming back to it starts sampling again.
document.addEventListener("visibilitychange", () => { if (!document.hidden) send({ type: "taskmgr:ready" }); });
send({ type: "ready" });
send({ type: "taskmgr:ready" });
