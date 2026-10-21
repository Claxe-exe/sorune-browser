// Sorune converter panel. The app does the work; this page picks files and the target format.
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Sorune sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);

const state = {
    files: [],
    target: null,
    busy: false,
    lo: { installed: false, installing: false, progress: null, version: null },
};

function send(message) {
    if (host) host.postMessage(message);
}

const KIND_LABEL = { image: "IMG", pdf: "PDF", word: "DOC", slides: "PPT", sheet: "XLS", text: "TXT", other: "?" };

function formatSize(bytes) {
    if (bytes < 1024) return `${bytes} B`;
    const units = ["KB", "MB", "GB"];
    let value = bytes / 1024;
    let unit = 0;
    while (value >= 1024 && unit < units.length - 1) {
        value /= 1024;
        unit++;
    }
    return `${value.toLocaleString(i18n.locale, { maximumFractionDigits: value < 10 ? 1 : 0 })} ${units[unit]}`;
}

function kindBadge(kind) {
    const badge = document.createElement("span");
    badge.className = `kind ${kind}`;
    badge.textContent = KIND_LABEL[kind] || "?";
    return badge;
}

// ---------------------------------------------------------------- files

function addFiles(files) {
    for (const file of files) {
        if (!state.files.some((f) => f.path.toLowerCase() === file.path.toLowerCase())) state.files.push(file);
    }
    render();
}

/** Formats every selected file can become; "office" when any of them needs LibreOffice for it. */
function commonTargets() {
    if (state.files.length === 0) return [];
    let common = null;
    for (const file of state.files) {
        const formats = new Map(file.targets.map((t) => [t.format, t.office]));
        if (common === null) {
            common = formats;
            continue;
        }
        for (const [format, office] of [...common]) {
            if (!formats.has(format)) common.delete(format);
            else common.set(format, office || formats.get(format));
        }
    }
    return [...common].map(([format, office]) => ({ format, office }));
}

function render() {
    const list = $("files");
    list.replaceChildren();
    for (const file of state.files) {
        const row = document.createElement("li");
        row.className = "file";

        const text = document.createElement("div");
        text.className = "file-text";
        const name = document.createElement("span");
        name.className = "file-name";
        name.textContent = file.name;
        name.title = file.name;
        const sub = document.createElement("span");
        sub.className = "file-sub";
        sub.textContent = file.targets.length ? formatSize(file.size) : t("Bu dosya türü desteklenmiyor");
        if (!file.targets.length) sub.classList.add("bad");
        text.append(name, sub);

        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "icon-btn";
        remove.textContent = "×";
        remove.title = t("Listeden çıkar");
        remove.addEventListener("click", () => {
            state.files = state.files.filter((f) => f !== file);
            render();
        });

        row.append(kindBadge(file.kind), text, remove);
        list.append(row);
    }

    $("files-card").hidden = state.files.length === 0;
    $("file-count").textContent = state.files.length ? `(${state.files.length})` : "";

    const targets = commonTargets();
    if (!targets.some((t) => t.format === state.target)) state.target = null;
    const root = $("targets");
    root.replaceChildren();
    for (const target of targets) {
        const locked = target.office && !state.lo.installed;
        const button = document.createElement("button");
        button.type = "button";
        button.className = "target";
        button.textContent = target.format.toUpperCase();
        button.setAttribute("aria-checked", String(target.format === state.target));
        button.disabled = locked || state.busy;
        if (locked) {
            const lock = document.createElement("span");
            lock.className = "lock";
            lock.textContent = "🔒";
            button.append(lock);
            button.title = t("LibreOffice gerekli");
        }
        button.addEventListener("click", () => {
            state.target = target.format;
            render();
        });
        root.append(button);
    }

    $("no-targets").hidden = state.files.length === 0 || targets.length > 0;
    $("office-hint").hidden = !targets.some((t) => t.office) || state.lo.installed;
    const allImages = state.files.length > 1 && state.files.every((f) => f.kind === "image");
    $("merge-row").hidden = !(allImages && state.target === "pdf");
    $("convert").disabled = state.busy || !state.target;
    $("convert").textContent = state.target ? t("{0} olarak dönüştür", state.target.toUpperCase()) : t("Dönüştür");
}

// ---------------------------------------------------------------- results

function addResults(results) {
    const list = $("results");
    for (const result of results) {
        const row = document.createElement("li");
        row.className = "file";

        const text = document.createElement("div");
        text.className = "file-text";
        const name = document.createElement("span");
        name.className = "file-name";
        name.textContent = result.ok ? result.name : result.source;
        name.title = name.textContent;
        const sub = document.createElement("span");
        sub.className = "file-sub";
        sub.textContent = result.ok ? t("Hazır") : result.error || t("Dönüştürülemedi");
        sub.classList.toggle("bad", !result.ok);
        text.append(name, sub);
        row.append(kindBadge(result.ok ? "ok" : "other"), text);
        row.firstChild.textContent = result.ok ? "✓" : "!";
        row.firstChild.classList.add(result.ok ? "sheet" : "pdf");

        if (result.ok) {
            const actions = document.createElement("div");
            actions.className = "result-actions";
            const open = document.createElement("button");
            open.type = "button";
            open.className = "chip";
            open.textContent = t("Aç");
            open.addEventListener("click", () => send({ type: "converter:open", path: result.output }));
            const show = document.createElement("button");
            show.type = "button";
            show.className = "chip";
            show.textContent = t("Klasör");
            show.title = t("Klasörde göster");
            show.addEventListener("click", () => send({ type: "converter:show", path: result.output }));
            actions.append(open, show);
            row.append(actions);
        }
        list.prepend(row);
    }
    $("results-card").hidden = list.children.length === 0;
}

// ---------------------------------------------------------------- LibreOffice

function renderOffice() {
    const lo = state.lo;
    const status = $("lo-status");
    status.classList.toggle("ok", lo.installed);
    status.textContent = lo.installed
        ? t("LibreOffice hazır{0}. Word, PowerPoint ve Excel dosyaları çevrilebilir.", lo.version ? ` (${lo.version})` : "")
        : lo.installing
            ? t("Kuruluyor…")
            : t("LibreOffice kurulu değil.");

    $("lo-install").hidden = lo.installed || lo.installing;
    $("lo-progress").hidden = !lo.installing;
    const measurable = typeof lo.progress === "number";
    $("lo-bar").classList.toggle("indeterminate", !measurable);
    $("lo-bar-fill").style.width = measurable ? `${Math.round(lo.progress * 100)}%` : "";
    $("lo-progress-text").textContent = measurable
        ? t("LibreOffice indiriliyor… %{0}", Math.round(lo.progress * 100))
        : t("LibreOffice kuruluyor… Windows izin isterse onayla.");
}

// ---------------------------------------------------------------- events

$("pick").addEventListener("click", () => send({ type: "converter:pick" }));
$("clear").addEventListener("click", () => {
    state.files = [];
    render();
});
$("results-clear").addEventListener("click", () => {
    $("results").replaceChildren();
    $("results-card").hidden = true;
});

$("convert").addEventListener("click", () => {
    if (!state.target || state.busy) return;
    state.busy = true;
    $("progress").hidden = false;
    $("progress-text").textContent = t("Hazırlanıyor…");
    render();
    send({
        type: "converter:convert",
        paths: state.files.filter((f) => f.targets.some((t) => t.format === state.target)).map((f) => f.path),
        target: state.target,
        merge: $("merge").checked,
    });
});

$("page-pdf").addEventListener("click", () => {
    $("progress").hidden = true;
    send({ type: "converter:pageToPdf" });
});

$("lo-install-btn").addEventListener("click", () => {
    $("lo-error").hidden = true;
    send({ type: "converter:installLibreOffice" });
});
$("lo-download").addEventListener("click", () => send({ type: "converter:downloadLibreOffice" }));

// Drag and drop: the files go to the app as objects (it reads their real paths); without
// preventDefault WebView2 would navigate to the dropped file instead.
const drop = $("drop");
for (const type of ["dragenter", "dragover"]) {
    document.addEventListener(type, (event) => {
        event.preventDefault();
        drop.classList.add("over");
    });
}
document.addEventListener("dragleave", (event) => {
    if (!event.relatedTarget) drop.classList.remove("over");
});
document.addEventListener("drop", (event) => {
    event.preventDefault();
    drop.classList.remove("over");
    const files = event.dataTransfer && event.dataTransfer.files;
    if (files && files.length && host && host.postMessageWithAdditionalObjects) {
        host.postMessageWithAdditionalObjects({ type: "converter:drop" }, files);
    }
});
drop.addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        send({ type: "converter:pick" });
    }
});

// ---------------------------------------------------------------- messages from the app

function applyTheme(theme) {
    if (!theme) return;
    const root = document.documentElement.style;
    if (theme.accent) root.setProperty("--accent", theme.accent);
    if (theme.accent2) root.setProperty("--accent2", theme.accent2);
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(theme.overlay || "");
    if (m) root.setProperty("--overlay", `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}`);
}

if (host) {
    host.addEventListener("message", (event) => {
        const message = event.data || {};
        switch (message.type) {
            case "converterState":
                applyTheme(message.theme);
                state.lo = message.libreOffice || state.lo;
                renderOffice();
                render();
                break;
            case "converterFiles":
                addFiles(message.files || []);
                break;
            case "converterProgress":
                $("progress").hidden = false;
                $("progress-text").textContent = message.total > 1
                    ? t("{0}/{1} dönüştürülüyor: {2}", message.index + 1, message.total, message.name)
                    : t("Dönüştürülüyor: {0}", message.name);
                break;
            case "converterResults":
                state.busy = false;
                $("progress").hidden = true;
                addResults(message.results || []);
                render();
                break;
            case "converterInstallResult":
                if (!message.ok && message.error) {
                    $("lo-error").textContent = message.error;
                    $("lo-error").hidden = false;
                }
                break;
        }
    });
}

render();
renderOffice();
send({ type: "converter:ready" });
