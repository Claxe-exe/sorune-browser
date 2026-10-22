// Claxe settings page. Every change is sent to the app right away; the app validates
// it, saves it, applies it live and sends the new state back, which re-renders the page.
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Sorune sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);

const ACCENT_PALETTE = ["#B583FF", "#63D2FF", "#E9C46A", "#FF4D6A", "#4ADE80", "#F472B6", "#FB923C", "#F5F5F7"];

let state = null;
let dragging = null; // id of the slider being dragged; it is not re-rendered meanwhile

function send(message) {
    if (host) host.postMessage(message);
}

function update(key, value) {
    send({ type: "updateSetting", key, value });
}

function throttle(fn, ms) {
    let last = 0;
    let timer = null;
    return (...args) => {
        const now = Date.now();
        clearTimeout(timer);
        if (now - last >= ms) {
            last = now;
            fn(...args);
        } else {
            timer = setTimeout(() => { last = Date.now(); fn(...args); }, ms - (now - last));
        }
    };
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

// ---------------------------------------------------------------- render

function render() {
    if (!state) return;
    const s = state.settings;

    renderThemes(s);
    renderWallpapers(s);
    renderSwatches(s);

    const opacity = s.chromeOpacity ?? state.themeDefaults.chromeOpacity;
    if (dragging !== "glass") $("glass").value = Math.round((0.97 - opacity) * 100);
    $("glass-value").textContent = t("Saydamlık %{0}", Math.round((1 - opacity) * 100));
    $("glass-reset").disabled = s.chromeOpacity == null;

    setSegmented("corner", s.cornerStyle);
    setSegmented("density", s.density);
    setSegmented("sleep-after", String(s.sleepAfterMinutes));

    $("download-folder").textContent = state.downloadFolder || t("Windows'un İndirilenler klasörü");
    $("download-folder-reset").hidden = !s.downloadFolder;

    $("engine").replaceChildren(...state.searchEngines.map((e) =>
        el("button", { type: "button", "data-value": e.id, text: e.name })));
    setSegmented("engine", s.searchEngine);

    for (const sw of document.querySelectorAll(".switch")) {
        sw.setAttribute("aria-checked", String(Boolean(s[sw.dataset.key])));
    }

    if (dragging !== "volume") $("volume").value = Math.round(s.soundVolume * 100);
    $("volume-value").textContent = `%${Math.round(s.soundVolume * 100)}`;
    $("volume").disabled = !s.soundsEnabled;

    renderPanels();
    renderPermissions();

    setSegmented("language", s.language);
    $("language-restart").hidden = s.language === state.activeLanguage;

    const db = state.defaultBrowser;
    const registered = Boolean(db.registeredExe);
    const moved = registered && db.registeredExe.toLowerCase() !== (db.currentExe || "").toLowerCase();
    $("default-browser-hint").textContent = !registered
        ? t("Linkler Sorune'de açılsın. Windows, seçimi Ayarlar › Varsayılan uygulamalar ekranında senin yapmanı ister.")
        : moved
            ? t("Kayıtlı Sorune başka bir yerde: {0}. Bu kopyayı kaydetmek için tekrar bas.", db.registeredExe)
            : t("Sorune tarayıcı olarak kayıtlı. Windows ayarlarında Sorune'yi seçtiysen linkler burada açılır.");
    $("default-browser-set").textContent = t(registered && !moved ? "Windows ayarlarını aç" : "Varsayılan yap");
    $("default-browser-remove").hidden = !registered;

    $("about-version").textContent = state.update.current || state.about.version || "-";
    const update = state.update;
    const updateText = {
        NotInstalled: t("Kurulum dosyasıyla kurulduğunda otomatik güncellenir."),
        Idle: "",
        Checking: t("Denetleniyor…"),
        UpToDate: t("Sorune güncel."),
        Downloading: t("{0} indiriliyor…", update.available),
        ReadyToRestart: t("{0} hazır.", update.available),
        Failed: t("Güncelleme sunucusuna ulaşılamadı."),
    }[update.state] ?? "";
    $("update-status").textContent = updateText ? updateText + " " : "";
    $("update-check").hidden = !["Idle", "UpToDate", "Failed"].includes(update.state);
    $("update-apply").hidden = update.state !== "ReadyToRestart";
    $("about-webview").textContent = state.about.webView2 || "-";
    $("about-profile").textContent = state.about.profile;

    const theme = state.themes.find((x) => x.id === s.theme);
    $("hero-theme").textContent = theme ? theme.name : "";
    $("hero-version").textContent = state.update.current || state.about.version || "";
    $("hero-update").hidden = update.state !== "ReadyToRestart";

    document.body.classList.toggle("no-motion", !s.animationsEnabled);
    applyLook(state.current.accent, state.current.wallpaperUrl);
}

function renderThemes(s) {
    $("themes").replaceChildren(...state.themes.map((theme) => {
        const card = el("button", {
            type: "button",
            class: "theme" + (theme.id === s.theme ? " selected" : ""),
            "aria-pressed": String(theme.id === s.theme),
            title: theme.description || theme.name,
            onclick: () => { if (theme.id !== s.theme) update("theme", theme.id); },
        }, [
            el("img", { alt: "", src: theme.thumbnailUrl || "" }),
            el("span", { class: "meta" }, [
                el("span", { class: "dot" }),
                el("span", { text: theme.name }),
            ]),
            el("span", { class: "check", text: "✓" }),
        ]);
        const dot = card.querySelector(".dot");
        dot.style.background = theme.accent;
        dot.style.color = theme.accent;
        return card;
    }));
}

function renderWallpapers(s) {
    const tiles = [];
    const currentTheme = state.themes.find((t) => t.id === s.theme);

    tiles.push(wallTile(null, "Temanın kendi", currentTheme && currentTheme.thumbnailUrl, s.wallpaper == null));
    for (const wp of state.wallpapers) {
        tiles.push(wallTile(wp.id, wp.name, wp.thumbnailUrl, s.wallpaper === wp.id));
    }

    if (state.customWallpaper) {
        const tile = wallTile(state.customWallpaper.id, "Kendi görselin", state.customWallpaper.url,
            s.wallpaper === state.customWallpaper.id);
        tile.append(el("span", {
            class: "remove",
            role: "button",
            tabindex: "0",
            title: "Görseli kaldır",
            "aria-label": "Kendi görselini kaldır",
            text: "×",
            onclick: (event) => { event.stopPropagation(); send({ type: "removeCustomWallpaper" }); },
        }));
        tiles.push(tile);
    }

    tiles.push(el("button", {
        type: "button",
        class: "wp upload",
        onclick: () => { $("wallpaper-error").hidden = true; send({ type: "pickWallpaper" }); },
    }, [el("div", {}, [el("span", { text: "+" }), document.createTextNode(t(state.customWallpaper ? "Başka görsel" : "Görsel yükle"))])]));

    $("wallpapers").replaceChildren(...tiles);
}

function wallTile(id, name, thumb, selected) {
    const tile = el("button", {
        type: "button",
        class: "wp" + (selected ? " selected" : ""),
        "aria-pressed": String(selected),
        title: name,
        onclick: () => { if (!selected) update("wallpaper", id); },
    }, [el("span", { class: "name", text: name })]);
    if (thumb) tile.style.backgroundImage = `url("${thumb}")`;
    return tile;
}

function renderSwatches(s) {
    const current = (s.accentColor || state.themeDefaults.accent).toUpperCase();
    const themeAccent = state.themeDefaults.accent.toUpperCase();
    const colors = [themeAccent, ...ACCENT_PALETTE.filter((c) => c !== themeAccent)];

    const nodes = colors.map((color) => {
        const isTheme = color === themeAccent;
        const swatch = el("button", {
            type: "button",
            class: "swatch" + (color === current ? " selected" : ""),
            title: isTheme ? "Temanın rengi" : color,
            "aria-label": isTheme ? "Temanın rengi" : t("Vurgu rengi {0}", color),
            onclick: () => update("accentColor", isTheme ? null : color),
        });
        swatch.style.background = color;
        swatch.style.color = color;
        return swatch;
    });

    const isCustom = !colors.includes(current);
    const picker = el("input", { type: "color", "aria-label": "Özel vurgu rengi" });
    picker.value = current.toLowerCase();
    const sendColor = throttle((value) => update("accentColor", value.toUpperCase()), 120);
    picker.addEventListener("input", () => sendColor(picker.value));
    nodes.push(el("label", { class: "custom-color" + (isCustom ? " selected" : ""), title: "Özel renk" }, [picker]));

    $("swatches").replaceChildren(...nodes);
}

function renderPanels() {
    setSegmented("sidebar-position", state.settings.sidebarPosition);
    const last = state.panels.length - 1;
    $("panels").replaceChildren(...state.panels.map((panel, index) => {
        const name = el("span", { text: panel.name });
        const rename = () => {
            const input = el("input", { class: "rename-input", type: "text", maxlength: "30", "aria-label": "Panel adı" });
            input.value = panel.name;
            let done = false;
            const finish = (save) => {
                if (done) return;
                done = true;
                const value = input.value.trim();
                if (save && value && value !== panel.name) send({ type: "renamePanel", id: panel.id, name: value });
                else renderPanels();
            };
            input.addEventListener("keydown", (e) => {
                if (e.key === "Enter") finish(true);
                if (e.key === "Escape") finish(false);
            });
            input.addEventListener("blur", () => finish(true));
            name.replaceWith(input);
            input.focus();
            input.select();
        };
        const move = (delta) => () => send({ type: "movePanel", id: panel.id, delta });
        const up = el("button", { type: "button", class: "chip", text: "↑", title: "Yukarı taşı", "aria-label": t("{0} yukarı", panel.name), onclick: move(-1) });
        const down = el("button", { type: "button", class: "chip", text: "↓", title: "Aşağı taşı", "aria-label": t("{0} aşağı", panel.name), onclick: move(1) });
        up.disabled = index === 0;
        down.disabled = index === last;

        return el("div", { class: "list-row" }, [
            el("span", { class: "grow" }, [name, el("span", { class: "sub", text: panel.url })]),
            up,
            down,
            el("button", { type: "button", class: "chip", text: "✎", title: "Yeniden adlandır", "aria-label": t("{0} yeniden adlandır", panel.name), onclick: rename }),
            el("button", {
                type: "button", class: "chip danger", text: "Kaldır", "aria-label": t("{0} panelini kaldır", panel.name),
                onclick: () => send({ type: "removePanel", id: panel.id }),
            }),
        ]);
    }));
    $("panel-form").hidden = state.panels.length >= state.maxPanels;
}

function renderPermissions() {
    const rows = state.permissions.map((p) => el("div", { class: "list-row" }, [
        el("span", { class: "grow" }, [
            el("span", { text: p.origin.replace(/^https?:\/\//, "") }),
            el("span", { class: "sub", text: p.what }),
        ]),
        el("span", { class: "badge " + (p.allowed ? "allow" : "deny"), text: p.allowed ? "İzin verildi" : "Engellendi" }),
        el("button", {
            type: "button", class: "chip", text: "Sıfırla",
            onclick: () => send({ type: "forgetPermission", origin: p.origin, kind: p.kind }),
        }),
    ]));
    if (rows.length === 0) rows.push(el("p", { class: "hint", text: "Hatırlanan bir site izni yok." }));
    $("permissions").replaceChildren(...rows);
    $("permissions-clear").disabled = state.permissions.length === 0;
}

function setSegmented(id, value) {
    for (const button of $(id).querySelectorAll("button")) {
        button.setAttribute("role", "radio");
        button.setAttribute("aria-checked", String(button.dataset.value === value));
    }
}

function applyLook(accent, wallpaper) {
    if (accent) document.documentElement.style.setProperty("--accent", accent);
    if (wallpaper && wallpaper !== applyLook.wallpaper) {
        applyLook.wallpaper = wallpaper;
        const img = new Image();
        img.onload = () => {
            if (applyLook.wallpaper !== wallpaper) return;
            document.documentElement.style.setProperty("--wallpaper", `url("${wallpaper}")`);
            $("wall").classList.add("ready");
        };
        img.src = wallpaper;
    }
}

// ---------------------------------------------------------------- inputs

for (const id of ["corner", "density", "engine"]) {
    const key = { corner: "cornerStyle", density: "density", engine: "searchEngine" }[id];
    $(id).addEventListener("click", (event) => {
        const button = event.target.closest("button");
        if (button && button.getAttribute("aria-checked") !== "true") update(key, button.dataset.value);
    });
}

for (const sw of document.querySelectorAll(".switch")) {
    sw.addEventListener("click", () => update(sw.dataset.key, sw.getAttribute("aria-checked") !== "true"));
}

$("sleep-after").addEventListener("click", (event) => {
    const button = event.target.closest("button");
    if (button && button.getAttribute("aria-checked") !== "true") update("sleepAfterMinutes", Number(button.dataset.value));
});
$("download-folder-pick").addEventListener("click", () => send({ type: "pickDownloadFolder" }));
$("download-folder-reset").addEventListener("click", () => send({ type: "resetDownloadFolder" }));

const sendGlass = throttle((value) => update("chromeOpacity", Math.round((0.97 - value / 100) * 100) / 100), 80);
$("glass").addEventListener("input", () => { dragging = "glass"; sendGlass(Number($("glass").value)); });
$("glass").addEventListener("change", () => { dragging = null; });
$("glass-reset").addEventListener("click", () => update("chromeOpacity", null));

$("volume").addEventListener("input", () => {
    dragging = "volume";
    $("volume-value").textContent = `%${$("volume").value}`;
});
$("volume").addEventListener("change", () => {
    dragging = null;
    update("soundVolume", Number($("volume").value) / 100);
});

// ---------------------------------------------------------------- panels and privacy

$("panel-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const url = $("panel-url").value.trim();
    if (!url) return;
    $("panel-error").hidden = true;
    send({ type: "addPanel", name: $("panel-name").value.trim(), url });
    $("panel-name").value = "";
    $("panel-url").value = "";
});

// ---------------------------------------------------------------- import

let importSources = [];

function renderImportSources() {
    const select = $("import-source");
    const options = importSources.map((s) => {
        const what = [s.hasBookmarks && t("yer imleri"), s.hasHistory && t("geçmiş")].filter(Boolean).join(", ");
        return el("option", { value: s.id, text: `${s.browser} — ${s.profile} (${what})` });
    });
    if (options.length === 0) options.push(el("option", { value: "", text: "Bu bilgisayarda başka bir tarayıcı bulunamadı" }));
    select.replaceChildren(...options);
    $("import-button").disabled = importSources.length === 0;
}

$("import-button").addEventListener("click", () => {
    const source = $("import-source").value;
    const bookmarks = $("import-bookmarks").checked;
    const history = $("import-history").checked;
    if (!source || (!bookmarks && !history)) return;
    $("import-button").disabled = true;
    $("import-button").textContent = t("Aktarılıyor…");
    $("import-status").hidden = true;
    send({ type: "importData", source, bookmarks, history });
});

$("language").addEventListener("click", (event) => {
    const button = event.target.closest("button");
    if (button && button.getAttribute("aria-checked") !== "true") update("language", button.dataset.value);
});
$("language-restart").addEventListener("click", () => send({ type: "restartApp" }));
$("update-check").addEventListener("click", () => send({ type: "checkUpdates" }));
$("update-apply").addEventListener("click", () => send({ type: "applyUpdate" }));
$("hero-update").addEventListener("click", () => send({ type: "applyUpdate" }));
$("default-browser-set").addEventListener("click", () => send({ type: "registerDefaultBrowser" }));
$("default-browser-remove").addEventListener("click", () => send({ type: "unregisterDefaultBrowser" }));

$("sidebar-position").addEventListener("click", (event) => {
    const button = event.target.closest("button");
    if (button && button.getAttribute("aria-checked") !== "true") update("sidebarPosition", button.dataset.value);
});

let clearRange = "all";
$("clear-range").addEventListener("click", (event) => {
    const button = event.target.closest("button");
    if (!button) return;
    clearRange = button.dataset.value;
    setSegmented("clear-range", clearRange);
});

// Two clicks: the first arms the button, the second (within 4 s) clears.
let clearArmed = null;
$("clear-button").addEventListener("click", () => {
    const button = $("clear-button");
    const kinds = ["history", "cookies", "cache", "downloads"].filter((k) => $(`clear-${k}`).checked);
    if (kinds.length === 0) return;
    if (!clearArmed) {
        button.textContent = t("Emin misin? Tekrar bas");
        button.classList.add("confirm");
        clearArmed = setTimeout(() => {
            clearArmed = null;
            button.textContent = t("Verileri temizle");
            button.classList.remove("confirm");
        }, 4000);
        return;
    }
    clearTimeout(clearArmed);
    clearArmed = null;
    button.textContent = t("Temizleniyor…");
    button.disabled = true;
    const message = { type: "clearData", range: clearRange };
    for (const kind of kinds) message[kind] = true;
    send(message);
});

$("permissions-clear").addEventListener("click", () => send({ type: "clearPermissions" }));

// Highlight the section being read: the last one whose top has passed near the top of the
// window (or the last section once the page is scrolled to the bottom).
const links = [...document.querySelectorAll(".nav a")];
const cards = [...document.querySelectorAll(".card")];
let navFrame = 0;
function updateNav() {
    navFrame = 0;
    const atBottom = window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 4;
    let current = cards[0];
    for (const card of cards) {
        if (card.getBoundingClientRect().top <= 140) current = card;
    }
    if (atBottom) current = cards[cards.length - 1];
    for (const link of links) link.classList.toggle("active", link.getAttribute("href") === `#${current.id}`);
}
window.addEventListener("scroll", () => { if (!navFrame) navFrame = requestAnimationFrame(updateNav); }, { passive: true });
updateNav();

// ---------------------------------------------------------------- messages from the app

if (host) {
    host.addEventListener("message", (event) => {
        const message = event.data || {};
        switch (message.type) {
            case "settings": {
                const first = state === null;
                state = message;
                render();
                // Opened as sorune://ayarlar#section: jump there once the content has its real height.
                if (first && location.hash) document.querySelector(location.hash)?.scrollIntoView();
                break;
            }
            case "importSources":
                importSources = message.items || [];
                renderImportSources();
                break;
            case "importResult": {
                const button = $("import-button");
                button.disabled = false;
                button.textContent = t("İçe aktar");
                $("import-status").textContent = message.ok
                    ? t("Tamamlandı: {0} yer imi, {1} geçmiş kaydı eklendi (zaten olanlar atlandı).", message.bookmarks, message.history)
                    : t("İçe aktarılamadı: {0}", message.error);
                $("import-status").hidden = false;
                break;
            }
            case "panelRejected":
                $("panel-error").hidden = false;
                break;
            case "dataCleared": {
                const button = $("clear-button");
                button.disabled = false;
                button.textContent = t("Verileri temizle");
                button.classList.remove("confirm");
                $("clear-status").textContent = message.ok ? t("Seçilen veriler temizlendi.") : t("Bazı veriler temizlenemedi, ayrıntılar günlükte.");
                $("clear-status").hidden = false;
                break;
            }
            case "config":
                if (message.theme && message.theme.overlay) {
                    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(message.theme.overlay);
                    if (m) document.documentElement.style.setProperty("--overlay",
                        `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}`);
                }
                break;
            case "wallpaperError":
                $("wallpaper-error").textContent = message.message || t("Görsel eklenemedi.");
                $("wallpaper-error").hidden = false;
                break;
        }
    });
}

send({ type: "ready" });
send({ type: "getSettings" });
send({ type: "importSources" });
