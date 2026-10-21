// Claxe settings page. Every change is sent to the app right away; the app validates
// it, saves it, applies it live and sends the new state back, which re-renders the page.
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Claxe sayfası çerçeve içinde açılamaz.");
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
        else if (key === "text") node.textContent = value;
        else if (key.startsWith("on")) node.addEventListener(key.slice(2), value);
        else node.setAttribute(key, value);
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
    $("glass-value").textContent = `Saydamlık %${Math.round((1 - opacity) * 100)}`;
    $("glass-reset").disabled = s.chromeOpacity == null;

    setSegmented("corner", s.cornerStyle);
    setSegmented("density", s.density);

    $("engine").replaceChildren(...state.searchEngines.map((e) =>
        el("button", { type: "button", "data-value": e.id, text: e.name })));
    setSegmented("engine", s.searchEngine);

    for (const sw of document.querySelectorAll(".switch")) {
        sw.setAttribute("aria-checked", String(Boolean(s[sw.dataset.key])));
    }

    if (dragging !== "volume") $("volume").value = Math.round(s.soundVolume * 100);
    $("volume-value").textContent = `%${Math.round(s.soundVolume * 100)}`;
    $("volume").disabled = !s.soundsEnabled;

    $("about-version").textContent = state.about.version || "-";
    $("about-webview").textContent = state.about.webView2 || "-";
    $("about-profile").textContent = state.about.profile;

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
    }, [el("div", {}, [el("span", { text: "+" }), document.createTextNode(state.customWallpaper ? "Başka görsel" : "Görsel yükle")])]));

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
            "aria-label": isTheme ? "Temanın rengi" : `Vurgu rengi ${color}`,
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
            case "settings":
                state = message;
                render();
                break;
            case "config":
                if (message.theme && message.theme.overlay) {
                    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(message.theme.overlay);
                    if (m) document.documentElement.style.setProperty("--overlay",
                        `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}`);
                }
                break;
            case "wallpaperError":
                $("wallpaper-error").textContent = message.message || "Görsel eklenemedi.";
                $("wallpaper-error").hidden = false;
                break;
        }
    });
}

send({ type: "ready" });
send({ type: "getSettings" });
