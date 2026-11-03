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
    renderUserThemes();
    renderWallpapers(s);
    renderSwatches(s);

    const opacity = s.chromeOpacity ?? state.themeDefaults.chromeOpacity;
    if (dragging !== "glass") $("glass").value = Math.round((0.97 - opacity) * 100);
    $("glass-value").textContent = t("Saydamlık %{0}", Math.round((1 - opacity) * 100));
    $("glass-reset").disabled = s.chromeOpacity == null;

    setSegmented("corner", s.cornerStyle);
    setSegmented("density", s.density);
    setSegmented("sleep-after", String(s.sleepAfterMinutes));
    setSegmented("panel-unload", String(s.panelUnloadMinutes));
    renderPersonal(s);

    $("download-folder").textContent = state.downloadFolder || t("Windows'un İndirilenler klasörü");
    $("download-folder-reset").hidden = !s.downloadFolder;

    // Kept when nothing changed, so that the thumb can travel between the same buttons.
    if ($("engine").querySelectorAll("button").length !== state.searchEngines.length) {
        $("engine").replaceChildren(...state.searchEngines.map((e) =>
            el("button", { type: "button", "data-value": e.id, text: e.name })));
    }
    setSegmented("engine", s.searchEngine);

    for (const sw of document.querySelectorAll(".switch")) {
        const checked = String(Boolean(s[sw.dataset.key]));
        if (sw.hasAttribute("aria-checked") && sw.getAttribute("aria-checked") !== checked && motionOn()) flickSwitch(sw);
        sw.setAttribute("aria-checked", checked);
    }

    if (dragging !== "volume") $("volume").value = Math.round(s.soundVolume * 100);
    $("volume-value").textContent = `%${Math.round(s.soundVolume * 100)}`;
    $("volume").disabled = !s.soundsEnabled;

    renderPanels();
    renderPermissions();
    renderSiteStyles();
    renderRestore();

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
    window.ClickSparks.set(s.animationsEnabled && s.clickSparks === true);
    applyLook(state.current.accent, state.current.wallpaperUrl);
    syncRanges();
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

function renderUserThemes() {
    const items = state.userThemes || [];
    const tiles = items.map((theme) => {
        const tile = el("button", {
            type: "button",
            class: "wp user-theme" + (theme.active ? " selected" : ""),
            "aria-pressed": String(Boolean(theme.active)),
            title: theme.name,
            onclick: () => send({ type: "userTheme:apply", id: theme.id }),
        }, [el("span", { class: "name" }, [el("span", { class: "dot" }), document.createTextNode(theme.name)])]);
        // User-typed names are shown as they are, never looked up in the dictionary.
        if (theme.picture) tile.style.backgroundImage = `url("${theme.picture}")`;
        else tile.style.background = `linear-gradient(135deg, ${theme.accent}, #1a1625)`;
        const dot = tile.querySelector(".dot");
        dot.style.background = theme.accent;
        dot.style.color = theme.accent;
        tile.append(el("span", {
            class: "remove",
            role: "button",
            tabindex: "0",
            title: "Temayı sil",
            "aria-label": "Temayı sil",
            text: "×",
            onclick: (event) => { event.stopPropagation(); send({ type: "userTheme:delete", id: theme.id }); },
        }));
        return tile;
    });
    if (!tiles.length) tiles.push(el("p", { class: "hint empty-note", text: "Henüz kayıtlı temanın yok. Görünümü beğenince aşağıdan bir ad verip kaydet." }));
    $("user-themes").replaceChildren(...tiles);
    $("user-theme-form").hidden = items.length >= (state.maxUserThemes || 24);
}

$("user-theme-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const name = $("user-theme-name").value.trim();
    $("user-theme-error").hidden = true;
    if (!name) return;
    send({ type: "userTheme:save", name });
    $("user-theme-name").value = "";
});

let restoreInfo = null;

function renderRestore() {
    const pending = Boolean(state.pendingRestore);
    $("restore-cancel").hidden = !pending;
    $("restore-restart").hidden = !pending;
    $("restore-pick").textContent = t(pending ? "Başka yedek seç" : "Yedek dosyası seç");
    $("restore-status").textContent = !pending
        ? t("Seçtiğin yedek, Sorune yeniden başlayınca bu profilin üstüne yazılır.")
        : restoreInfo
            ? t("{0} tarihli yedek (Sorune {1}) hazır. Yeniden başlatınca uygulanır.", new Date(restoreInfo.createdAt).toLocaleString(), restoreInfo.version || "?")
            : t("Bir yedek hazır. Yeniden başlatınca uygulanır.");
}

function formatSize(bytes) {
    return bytes >= 1048576 ? `${(bytes / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`;
}

$("backup-create").addEventListener("click", () => {
    $("backup-error").hidden = true;
    send({ type: "backup:create", includeHistory: $("backup-history").checked });
});
$("restore-pick").addEventListener("click", () => { $("backup-error").hidden = true; send({ type: "backup:restore" }); });
$("restore-cancel").addEventListener("click", () => { restoreInfo = null; send({ type: "backup:cancel" }); });
$("restore-restart").addEventListener("click", () => send({ type: "restartApp" }));

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

function renderSiteStyles() {
    const rows = (state.siteStyles || []).map((site) => {
        const parts = [];
        if (site.hidden > 0) parts.push(t("{0} öğe gizli", site.hidden));
        if (site.hasCss) parts.push(t("Özel CSS"));
        if (!site.enabled) parts.push(t("Kapalı"));
        // A web address is shown as it is, never looked up in the dictionary.
        return el("div", { class: "list-row" }, [
            el("span", { class: "grow" }, [
                el("span", { text: site.host }),
                el("span", { class: "sub", text: parts.join(" · ") || t("Renk ve yazı tipi") }),
            ]),
            el("button", {
                type: "button", class: "chip", text: site.enabled ? "Kapat" : "Aç",
                onclick: () => send({ type: "siteStyle:toggle", host: site.host }),
            }),
            el("button", {
                type: "button", class: "chip", text: "Sıfırla",
                onclick: () => send({ type: "siteStyle:remove", host: site.host }),
            }),
        ]);
    });
    if (rows.length === 0) rows.push(el("p", { class: "hint", text: "Henüz görünümünü değiştirdiğin bir site yok." }));
    $("site-styles").replaceChildren(...rows);
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

// ---------------------------------------------------------------- personalisation (new tab, mascot, sounds, palette)

function renderPersonal(s) {
    setSegmented("site-scheme", s.siteColorScheme);
    $("force-dark-restart").hidden = s.forceDarkSites === state.forceDarkActive;
    $("secure-dns-restart").hidden = Boolean(s.secureDns) === state.secureDnsActive;
    $("dns-providers").hidden = !s.secureDns;
    $("dns-providers").replaceChildren(...(state.dnsProviders || []).map((p) =>
        el("button", { type: "button", role: "radio", "aria-checked": String(p.id === s.dnsProvider), onclick: () => update("dnsProvider", p.id) },
            [el("strong", { text: p.name }), el("span", { text: p.note })])));
    setSegmented("nt-effect", s.newTabEffect);
    setSegmented("nt-backdrop", s.newTabBackdrop);
    setSegmented("nt-greeting-anim", s.newTabGreetingAnim);
    setSegmented("nt-align", s.newTabAlign);
    setSegmented("sound-pack", s.soundPack);

    if (dragging !== "nt-dim") $("nt-dim").value = Math.round((s.newTabDim ?? 0.45) * 100);
    $("nt-dim-value").textContent = s.newTabDim == null ? t("Temanınki") : `%${Math.round(s.newTabDim * 100)}`;
    $("nt-dim-reset").disabled = s.newTabDim == null;
    if (dragging !== "nt-blur") $("nt-blur").value = s.newTabBlur;
    $("nt-blur-value").textContent = s.newTabBlur ? `${s.newTabBlur} px` : t("Keskin");
    setSegmented("nt-clock-font", s.newTabClockFont);
    setSegmented("nt-label-font", s.newTabLabelFont);
    if (dragging !== "nt-cards") $("nt-cards").value = Math.round((s.newTabCards ?? 0.42) * 100);
    $("nt-cards-value").textContent = s.newTabCards == null ? t("Temanınki") : `%${Math.round(s.newTabCards * 100)}`;
    $("nt-cards-reset").disabled = s.newTabCards == null;
    if (dragging !== "vtabs-width") $("vtabs-width").value = s.verticalTabsWidth;
    $("vtabs-width-value").textContent = `${s.verticalTabsWidth} px`;
    $("vtabs-width-row").hidden = !s.verticalTabs;

    const current = (s.accentColor || "").toUpperCase();
    const palette = state.wallpaperPalette || [];
    $("wall-palette").replaceChildren(...palette.map((color) => {
        const swatch = el("button", {
            type: "button",
            class: "swatch" + (color.toUpperCase() === current ? " selected" : ""),
            title: color,
            "aria-label": t("Vurgu rengi {0}", color),
            onclick: () => update("accentColor", color.toUpperCase()),
        });
        swatch.style.background = color;
        swatch.style.color = color;
        return swatch;
    }));
    if (palette.length === 0) $("wall-palette").append(el("span", { class: "hint", text: "Bu duvar kâğıdında belirgin bir renk bulunamadı." }));

    const mascot = state.mascotUrl;
    $("mascot-preview").hidden = !mascot;
    if (mascot && $("mascot-preview").getAttribute("src") !== mascot) $("mascot-preview").src = mascot;
    $("mascot-remove").hidden = !mascot;
    $("mascot-peek-row").hidden = !mascot;
    if (dragging !== "mascot-peek") $("mascot-peek").value = Math.round(s.mascotPeek * 100);
}

$("force-dark-restart").addEventListener("click", () => send({ type: "restartApp" }));
$("secure-dns-restart").addEventListener("click", () => send({ type: "restartApp" }));
for (const [id, key] of [["nt-clock-font", "newTabClockFont"], ["nt-label-font", "newTabLabelFont"], ["nt-effect", "newTabEffect"], ["nt-backdrop", "newTabBackdrop"], ["nt-greeting-anim", "newTabGreetingAnim"], ["nt-align", "newTabAlign"], ["sound-pack", "soundPack"], ["site-scheme", "siteColorScheme"]]) {
    $(id).addEventListener("click", (event) => {
        const button = event.target.closest("button");
        if (button) update(key, button.dataset.value);
    });
}

function bindRange(id, send) {
    const throttled = throttle(send, 80);
    $(id).addEventListener("input", () => { dragging = id; throttled(Number($(id).value)); });
    $(id).addEventListener("change", () => { dragging = null; });
}
bindRange("nt-dim", (v) => update("newTabDim", v / 100));
bindRange("nt-blur", (v) => update("newTabBlur", v));
bindRange("nt-cards", (v) => update("newTabCards", v / 100));
$("nt-cards-reset").addEventListener("click", () => update("newTabCards", null));
bindRange("vtabs-width", (v) => update("verticalTabsWidth", v));
bindRange("mascot-peek", (v) => update("mascotPeek", v / 100));
$("nt-dim-reset").addEventListener("click", () => update("newTabDim", null));
$("mascot-pick").addEventListener("click", () => { $("mascot-error").hidden = true; send({ type: "pickMascot" }); });
$("mascot-remove").addEventListener("click", () => send({ type: "removeMascot" }));

// ---------------------------------------------------------------- profiles

const PROFILE_EMOJIS = ["🌸", "⭐", "💼", "🎮", "📚", "🎵", "🌙", "🔥", "🐱", "🦊", "🍓", "⚡"];
let profiles = { items: [], colors: [] };
let newProfile = { emoji: "🌸", color: null };
let deleteArmed = null;

function renderProfiles() {
    $("profiles").replaceChildren(...profiles.items.map((p) => {
        const avatar = el("span", { class: "profile-avatar", text: p.emoji });
        avatar.style.background = p.color;
        const name = el("input", { class: "rename-input profile-name", type: "text", maxlength: "30", "aria-label": "Profil adı" });
        name.value = p.name;
        name.addEventListener("change", () => { if (name.value.trim()) send({ type: "profiles:update", id: p.id, name: name.value.trim() }); });

        const state = p.isCurrent ? t("bu pencere") : p.running ? t("açık") : "";
        const actions = [];
        if (!p.isCurrent) actions.push(el("button", { type: "button", class: "chip", text: p.running ? "Öne getir" : "Aç", onclick: () => send({ type: "profiles:open", id: p.id }) }));
        if (!p.isDefault) {
            const armed = deleteArmed === p.id;
            actions.push(el("button", {
                type: "button",
                class: "chip danger" + (armed ? " confirm" : ""),
                text: armed ? "Emin misin? Tüm verisi silinir" : "Sil",
                onclick: () => {
                    if (deleteArmed !== p.id) { deleteArmed = p.id; renderProfiles(); setTimeout(() => { if (deleteArmed === p.id) { deleteArmed = null; renderProfiles(); } }, 3500); return; }
                    deleteArmed = null;
                    send({ type: "profiles:delete", id: p.id });
                },
            }));
        }
        const colors = el("div", { class: "swatches mini" }, profiles.colors.map((color) => {
            const swatch = el("button", { type: "button", class: "swatch" + (color.toUpperCase() === p.color.toUpperCase() ? " selected" : ""), "aria-label": color, onclick: () => send({ type: "profiles:update", id: p.id, color }) });
            swatch.style.background = color;
            swatch.style.color = color;
            return swatch;
        }));
        return el("div", { class: "list-row profile-row" }, [
            avatar,
            el("span", { class: "grow" }, [name, el("span", { class: "sub", text: [p.isDefault ? t("ana profil") : "", state].filter(Boolean).join(" · ") })]),
            colors,
            ...actions,
        ]);
    }));

    $("profile-emoji").replaceChildren(...PROFILE_EMOJIS.map((emoji) => {
        const b = el("button", { type: "button", class: "emoji" + (emoji === newProfile.emoji ? " selected" : ""), text: emoji, onclick: () => { newProfile.emoji = emoji; renderProfiles(); } });
        return b;
    }));
    const pick = newProfile.color || profiles.colors[profiles.items.length % Math.max(1, profiles.colors.length)];
    $("profile-colors").replaceChildren(...profiles.colors.map((color) => {
        const swatch = el("button", { type: "button", class: "swatch" + (color === pick ? " selected" : ""), "aria-label": color, onclick: () => { newProfile.color = color; renderProfiles(); } });
        swatch.style.background = color;
        swatch.style.color = color;
        return swatch;
    }));
}

$("profile-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const name = $("profile-name").value.trim();
    $("profile-error").hidden = true;
    if (!name) return;
    send({ type: "profiles:create", name, emoji: newProfile.emoji, color: newProfile.color || profiles.colors[profiles.items.length % profiles.colors.length] });
    $("profile-name").value = "";
    newProfile = { emoji: "🌸", color: null };
});

// ---------------------------------------------------------------- extensions

let extensions = [];

function renderExtensions(busy) {
    const installedNames = extensions.map((e) => e.name.toLowerCase());
    for (const button of document.querySelectorAll("[data-install]")) {
        const which = button.dataset.install;
        const installed = installedNames.some((n) => which === "ublock" ? n.includes("ublock") : n.includes("bitwarden"));
        button.textContent = installed ? t("Kurulu ✓") : t("Kur");
        button.disabled = busy || installed;
    }
    $("ext-folder").disabled = $("ext-file").disabled = busy;

    $("extensions").replaceChildren(...extensions.map((ext) => {
        const actions = [];
        if (ext.hasOptions) actions.push(el("button", { type: "button", class: "chip", text: "Seçenekler", onclick: () => send({ type: "ext:options", id: ext.id }) }));
        actions.push(el("button", { type: "button", class: "chip", text: ext.isEnabled ? "Kapat" : "Aç", onclick: () => send({ type: "ext:toggle", id: ext.id, enabled: !ext.isEnabled }) }));
        actions.push(el("button", { type: "button", class: "chip danger", text: "Kaldır", onclick: () => send({ type: "ext:remove", id: ext.id }) }));
        actions.forEach((a) => { a.disabled = Boolean(busy); });
        return el("div", { class: "list-row" }, [
            el("span", { class: "grow" }, [
                el("span", { text: ext.name }),
                el("span", { class: "sub", text: [ext.version ? `v${ext.version}` : "", ext.isEnabled ? t("Etkin") : t("Devre dışı"), ext.hasPopup ? t("kenar çubuğunda") : ""].filter(Boolean).join(" · ") }),
            ]),
            ...actions,
        ]);
    }));
    if (extensions.length === 0) $("extensions").append(el("p", { class: "hint", text: "Henüz eklenti yok." }));
}

for (const button of document.querySelectorAll("[data-install]")) {
    button.addEventListener("click", () => send({ type: "ext:install", which: button.dataset.install }));
}
$("ext-folder").addEventListener("click", () => send({ type: "ext:installFolder" }));
$("ext-file").addEventListener("click", () => send({ type: "ext:installFile" }));

function setSegmented(id, value) {
    for (const button of $(id).querySelectorAll("button")) {
        button.setAttribute("role", "radio");
        button.setAttribute("aria-checked", String(button.dataset.value === value));
    }
    placeThumb($(id), true);
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
$("panel-unload").addEventListener("click", (event) => {
    const button = event.target.closest("button");
    if (button && button.getAttribute("aria-checked") !== "true") update("panelUnloadMinutes", Number(button.dataset.value));
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

// The branched menu: folding groups whose lines lead to the section being read. The lines are drawn
// by script because their length depends on the row; the section itself is found as before: the last
// one whose top has passed near the top of the window (or the last once at the bottom of the page).
const BM_ROW = 34;
const BM_PAD = 6;
const BM_TRUNK = 14;
const BM_END = 32;
const BM_RADIUS = 10;
const SVG_NS = "http://www.w3.org/2000/svg";

const links = [...document.querySelectorAll(".nav a")];
const cards = [...document.querySelectorAll(".card")];
const groups = [...document.querySelectorAll(".bm-section")].map((section) => {
    const head = section.querySelector(".bm-head");
    const tree = section.querySelector(".bm-tree");
    const group = { section, head, body: section.querySelector(".bm-body"), reach: new Map() };
    if (!tree) return group;

    const kids = [...tree.querySelectorAll("a")];
    const height = BM_PAD * 2 + kids.length * BM_ROW;
    const rowY = (k) => BM_PAD + k * BM_ROW + BM_ROW / 2;
    const curve = (k) => `A ${BM_RADIUS} ${BM_RADIUS} 0 0 0 ${BM_TRUNK + BM_RADIUS} ${rowY(k)} H ${BM_END}`;
    const svg = document.createElementNS(SVG_NS, "svg");
    svg.setAttribute("class", "bm-lines");
    svg.setAttribute("width", String(BM_END));
    svg.setAttribute("height", String(height));
    svg.setAttribute("aria-hidden", "true");
    const path = (cls, d) => {
        const el = document.createElementNS(SVG_NS, "path");
        el.setAttribute("class", cls);
        el.setAttribute("d", d);
        svg.append(el);
        return el;
    };
    path("bm-base", `M ${BM_TRUNK} 0 V ${rowY(kids.length - 1) - BM_RADIUS}`);
    kids.forEach((_, k) => path("bm-base", `M ${BM_TRUNK} ${rowY(k) - BM_RADIUS} ${curve(k)}`));
    kids.forEach((kid, k) => {
        const el = path("bm-reach", `M ${BM_TRUNK} 0 V ${rowY(k) - BM_RADIUS} ${curve(k)}`);
        const length = rowY(k) - BM_RADIUS + (Math.PI * BM_RADIUS) / 2 + (BM_END - BM_TRUNK - BM_RADIUS);
        el.style.strokeDasharray = String(length);
        el.style.strokeDashoffset = String(length);
        group.reach.set(kid, { el, length });
    });
    tree.prepend(svg);

    head.addEventListener("click", () => {
        setGroupOpen(group, !section.hasAttribute("data-open"));
        placeMarker();
    });
    setGroupOpen(group, false);
    return group;
});

function setGroupOpen(group, open) {
    group.section.toggleAttribute("data-open", open);
    group.head.setAttribute("aria-expanded", String(open));
    group.body.inert = !open; // keyboard focus skips what is folded away
}

const groupOf = (link) => groups.find((group) => group.section.contains(link));

function placeMarker() {
    const marker = $("bm-marker");
    const group = activeLink && groupOf(activeLink);
    const shown = group && (group.section.hasAttribute("data-open") || activeLink === group.head);
    if (shown) marker.style.top = `${group.head.offsetTop + (group.head.offsetHeight - 16) / 2}px`;
    marker.toggleAttribute("data-on", Boolean(shown));
}

let activeLink = null;
function setActiveLink(link) {
    if (link === activeLink) return;
    const before = activeLink && groupOf(activeLink);
    const after = groupOf(link);
    activeLink = link;
    for (const other of links) {
        const on = other === link;
        other.classList.toggle("active", on);
        if (on) other.setAttribute("aria-current", "true");
        else other.removeAttribute("aria-current");
    }
    for (const group of groups) {
        for (const [kid, { el, length }] of group.reach) el.style.strokeDashoffset = String(kid === link ? 0 : length);
    }
    // Moving on to another group folds the last one. In the narrow layout the menu sits above the
    // content, where folding would move the page under the reader: there only the first one opens.
    if (before !== after && (!before || !narrowLayout.matches)) {
        if (before && before.body) setGroupOpen(before, false);
        if (after.body) setGroupOpen(after, true);
    }
    placeMarker();
}

const narrowLayout = window.matchMedia("(max-width: 820px)");
let navFrame = 0;
let navPinned = false; // a clicked section stays lit until the page moves on from where it landed
let navPinY = null;
function updateNav() {
    navFrame = 0;
    if (navPinned) {
        if (navPinY === null) navPinY = window.scrollY;
        if (Math.abs(window.scrollY - navPinY) < 2) return;
        navPinned = false;
    }
    const atBottom = window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 4;
    let current = cards[0];
    for (const card of cards) {
        if (card.getBoundingClientRect().top <= 140) current = card;
    }
    if (atBottom) current = cards[cards.length - 1];
    const link = links.find((other) => other.getAttribute("href") === `#${current.id}`);
    if (link) setActiveLink(link);
}
for (const link of links) {
    link.addEventListener("click", () => {
        navPinned = true;
        navPinY = null;
        setActiveLink(link);
    });
}
window.addEventListener("scroll", () => { if (!navFrame) navFrame = requestAnimationFrame(updateNav); }, { passive: true });
// The heads move while a group folds, and the marker follows them.
new ResizeObserver(placeMarker).observe($("bm"));
updateNav();

// ---------------------------------------------------------------- springy controls

function motionOn() {
    return !document.body.classList.contains("no-motion");
}

function flickSwitch(sw) {
    sw.classList.remove("flick");
    void sw.offsetWidth; // restarts the animation when flicked again quickly
    sw.classList.add("flick");
}
for (const sw of document.querySelectorAll(".switch")) {
    sw.addEventListener("animationend", () => sw.classList.remove("flick"));
}

// Segments: one thumb per group, placed by its four edges so that each edge can move at its own speed.
const segmentResize = new ResizeObserver((entries) => {
    for (const entry of entries) placeThumb(entry.target, false);
});
function placeThumb(group, animate) {
    let thumb = group.querySelector(":scope > .seg-thumb");
    if (!thumb) {
        thumb = document.createElement("span");
        thumb.className = "seg-thumb";
        thumb.setAttribute("aria-hidden", "true");
        group.classList.add("rubber");
        group.prepend(thumb);
        segmentResize.observe(group);
    }
    const on = group.querySelector('button[aria-checked="true"]');
    thumb.hidden = !on;
    if (!on) return;
    const left = on.offsetLeft;
    const previous = parseFloat(thumb.style.left);
    thumb.classList.remove("moving-left", "moving-right");
    if (animate && motionOn() && !Number.isNaN(previous) && previous !== left) {
        void thumb.offsetWidth;
        thumb.classList.add(left > previous ? "moving-right" : "moving-left");
    }
    thumb.style.left = `${left}px`;
    thumb.style.right = `${group.clientWidth - left - on.offsetWidth}px`;
    thumb.style.top = `${on.offsetTop}px`;
    thumb.style.bottom = `${group.clientHeight - on.offsetTop - on.offsetHeight}px`;
}
document.fonts.ready.then(() => {
    for (const group of document.querySelectorAll(".segmented.rubber")) placeThumb(group, false);
});

// Sliders: the filled part follows the value, and pulling past either end stretches the slider.
function syncRange(range) {
    const min = Number(range.min || 0);
    const max = Number(range.max || 100);
    const part = max > min ? (Number(range.value) - min) / (max - min) : 0;
    range.style.setProperty("--fill", `calc(8px + (100% - 16px) * ${part.toFixed(4)})`);
}
function syncRanges() {
    for (const range of document.querySelectorAll("input.elastic")) syncRange(range);
}
for (const range of document.querySelectorAll('input[type="range"]')) {
    range.classList.add("elastic");
    range.addEventListener("input", () => syncRange(range));
    range.addEventListener("pointerdown", () => {
        if (range.disabled || !motionOn()) return;
        const box = range.getBoundingClientRect();
        range.classList.add("pulling");
        const onMove = (event) => {
            const past = event.clientX < box.left ? box.left - event.clientX : Math.max(0, event.clientX - box.right);
            const stretch = Math.min(Math.sqrt(past) * 1.8, 34);
            range.style.transformOrigin = event.clientX < box.left ? "right center" : "left center";
            range.style.transform = stretch > 0 ? `scale(${1 + stretch / box.width}, ${1 - stretch / 120})` : "";
        };
        const onUp = () => {
            window.removeEventListener("pointermove", onMove);
            window.removeEventListener("pointerup", onUp);
            window.removeEventListener("pointercancel", onUp);
            range.classList.remove("pulling");
            range.style.transform = ""; // the spring in the stylesheet carries it back
        };
        window.addEventListener("pointermove", onMove, { passive: true });
        window.addEventListener("pointerup", onUp);
        window.addEventListener("pointercancel", onUp);
    });
}
syncRanges();

// Theme tiles lean towards the pointer.
for (const id of ["themes", "user-themes"]) {
    $(id).addEventListener("pointermove", (event) => {
        const tile = event.target.closest(".theme, .user-theme");
        if (!tile || !motionOn()) return;
        const box = tile.getBoundingClientRect();
        const x = (event.clientX - box.left) / box.width;
        const y = (event.clientY - box.top) / box.height;
        tile.classList.add("tilting");
        tile.style.setProperty("--ry", `${((x - 0.5) * 14).toFixed(2)}deg`);
        tile.style.setProperty("--rx", `${((0.5 - y) * 14).toFixed(2)}deg`);
        tile.style.setProperty("--hx", `${(x * 100).toFixed(1)}%`);
        tile.style.setProperty("--hy", `${(y * 100).toFixed(1)}%`);
    }, { passive: true });
    $(id).addEventListener("pointerout", (event) => {
        const tile = event.target.closest(".theme, .user-theme");
        if (tile && !tile.contains(event.relatedTarget)) tile.classList.remove("tilting");
    });
}

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
            case "extensions":
                extensions = message.items || [];
                renderExtensions(message.busy);
                $("ext-status").textContent = message.error
                    ? message.error
                    : message.installed ? t("{0} kuruldu.", message.installed) : "";
                $("ext-status").classList.toggle("error", Boolean(message.error));
                break;
            case "extensionsBusy":
                renderExtensions(message.busy);
                $("ext-status").classList.remove("error");
                $("ext-status").textContent = message.busy ? t("İşleniyor… (indirme birkaç saniye sürebilir)") : "";
                break;
            case "profiles":
                profiles = message;
                renderProfiles();
                break;
            case "backupBusy":
                $("backup-create").disabled = true;
                $("backup-status").textContent = t("Yedek hazırlanıyor…");
                break;
            case "backupDone":
                $("backup-create").disabled = false;
                $("backup-status").textContent = t("Kaydedildi: {0} dosya, {1}.", message.files, formatSize(message.bytes));
                break;
            case "backupError":
                $("backup-create").disabled = false;
                $("backup-status").textContent = "";
                $("backup-error").textContent = message.message || "";
                $("backup-error").hidden = false;
                break;
            case "restoreStaged":
                restoreInfo = message;
                renderRestore();
                break;
            case "userThemeError":
                $("user-theme-error").textContent = message.message || "";
                $("user-theme-error").hidden = false;
                break;
            case "profilesError":
                $("profile-error").textContent = message.message || "";
                $("profile-error").hidden = false;
                break;
            case "mascotError":
                $("mascot-error").textContent = message.message || t("Görsel eklenemedi.");
                $("mascot-error").hidden = false;
                break;
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
send({ type: "ext:list" });
send({ type: "profiles:list" });
// Profile windows opened or closed elsewhere: refresh when coming back to this page.
document.addEventListener("visibilitychange", () => { if (!document.hidden) send({ type: "profiles:list" }); });
