// First-run welcome tour. Uses the same messages as the settings page, so every choice here
// is an ordinary setting the user can change later.
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Sorune sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);
const send = (message) => { if (host) host.postMessage(message); };
const update = (key, value) => send({ type: "updateSetting", key, value });

const steps = [...document.querySelectorAll(".step")];
let current = 0;
let state = null;
let extensions = [];

function show(index) {
    current = Math.max(0, Math.min(steps.length - 1, index));
    steps.forEach((step, i) => { step.hidden = i !== current; });
    $("prev").style.visibility = current === 0 ? "hidden" : "visible";
    $("next").textContent = current === steps.length - 1 ? t("Başlayalım 🚀") : t("Devam");
    $("dots").replaceChildren(...steps.map((_, i) => {
        const dot = document.createElement("li");
        dot.className = i === current ? "active" : i < current ? "done" : "";
        return dot;
    }));
}

function finish() {
    send({ type: "whatsnew:open", target: "close" });
}

$("next").addEventListener("click", () => (current === steps.length - 1 ? finish() : show(current + 1)));
$("prev").addEventListener("click", () => show(current - 1));
$("skip").addEventListener("click", finish);
$("import").addEventListener("click", () => send({ type: "whatsnew:open", target: "import" }));
$("restart").addEventListener("click", () => send({ type: "restartApp" }));
document.addEventListener("keydown", (event) => {
    if (event.target instanceof HTMLButtonElement) return;
    if (event.key === "ArrowRight" || event.key === "Enter") current === steps.length - 1 ? finish() : show(current + 1);
    if (event.key === "ArrowLeft") show(current - 1);
});

for (const [id, key] of [["language", "language"], ["sound-pack", "soundPack"]]) {
    $(id).addEventListener("click", (event) => {
        const button = event.target.closest("button");
        if (button) update(key, button.dataset.value);
    });
}
for (const toggle of document.querySelectorAll(".toggle")) {
    toggle.addEventListener("click", () => update(toggle.dataset.key, toggle.getAttribute("aria-pressed") !== "true"));
}
for (const button of document.querySelectorAll("[data-install]")) {
    button.addEventListener("click", () => send({ type: "ext:install", which: button.dataset.install }));
}

function setChoice(id, value) {
    for (const button of $(id).querySelectorAll("button")) button.classList.toggle("selected", button.dataset.value === value);
}

function render() {
    if (!state) return;
    const s = state.settings;
    setChoice("language", s.language);
    $("language-hint").hidden = s.language === state.activeLanguage;
    setChoice("sound-pack", s.soundPack);
    for (const toggle of document.querySelectorAll(".toggle")) toggle.setAttribute("aria-pressed", String(Boolean(s[toggle.dataset.key])));

    $("themes").replaceChildren(...state.themes.map((theme) => {
        const card = document.createElement("button");
        card.type = "button";
        card.className = "theme" + (theme.id === s.theme ? " selected" : "");
        const img = document.createElement("img");
        img.src = theme.thumbnailUrl;
        img.alt = "";
        const name = document.createElement("span");
        name.textContent = theme.name;
        card.append(img, name);
        card.addEventListener("click", () => update("theme", theme.id));
        return card;
    }));

    const accent = state.current && state.current.accent;
    if (accent) document.documentElement.style.setProperty("--accent", accent);
    const wallpaper = state.current && state.current.wallpaperUrl;
    if (wallpaper) {
        document.documentElement.style.setProperty("--wallpaper", `url("${wallpaper}")`);
        $("wall").classList.add("ready");
    }
}

function renderExtensions(busy) {
    const names = extensions.map((e) => e.name.toLowerCase());
    for (const button of document.querySelectorAll("[data-install]")) {
        const installed = names.some((n) => n.includes(button.dataset.install === "ublock" ? "ublock" : "bitwarden"));
        button.textContent = installed ? t("Kurulu ✓") : t("Kur");
        button.disabled = busy || installed;
    }
}

if (host) {
    host.addEventListener("message", (event) => {
        const message = event.data || {};
        switch (message.type) {
            case "settings":
                state = message;
                render();
                break;
            case "extensions":
                extensions = message.items || [];
                renderExtensions(message.busy);
                $("ext-status").textContent = message.error || (message.installed ? t("{0} kuruldu.", message.installed) : "");
                break;
            case "extensionsBusy":
                renderExtensions(message.busy);
                $("ext-status").textContent = message.busy ? t("İşleniyor… (indirme birkaç saniye sürebilir)") : "";
                break;
        }
    });
}

show(0);
send({ type: "getSettings" });
send({ type: "ext:list" });
