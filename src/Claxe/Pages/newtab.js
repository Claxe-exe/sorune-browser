// Claxe new tab page. Talks to the app through WebView2 web messages; the app only
// accepts messages from this internal origin and sends back the theme + shortcuts.
"use strict";

// Claxe pages are never meant to live inside another site's frame.
if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Claxe sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);

const state = {
    shortcuts: [],
    maxShortcuts: 12,
    animations: true,
    wallpaper: null,
};

function send(message) {
    if (host) host.postMessage(message);
}

// ---------------------------------------------------------------- clock

const timeFormat = new Intl.DateTimeFormat("tr-TR", { hour: "2-digit", minute: "2-digit" });
const dateFormat = new Intl.DateTimeFormat("tr-TR", { day: "numeric", month: "long", weekday: "long" });

function greetingFor(hour) {
    if (hour >= 5 && hour < 12) return "Günaydın";
    if (hour >= 12 && hour < 18) return "İyi günler";
    if (hour >= 18 && hour < 23) return "İyi akşamlar";
    return "İyi geceler";
}

function tick() {
    const now = new Date();
    $("clock").textContent = timeFormat.format(now);
    $("date").textContent = dateFormat.format(now);
    $("greeting").textContent = greetingFor(now.getHours());
    // Wake up exactly at the next minute instead of polling every second.
    setTimeout(tick, 60000 - (now.getSeconds() * 1000 + now.getMilliseconds()) + 20);
}

// ---------------------------------------------------------------- search

$("search").addEventListener("submit", (event) => {
    event.preventDefault();
    const text = $("query").value.trim();
    if (text) send({ type: "navigate", text });
});

// ---------------------------------------------------------------- shortcuts

function letterIcon(title) {
    const span = document.createElement("span");
    span.textContent = (title.trim()[0] || "?").toLocaleUpperCase("tr-TR");
    return span;
}

function renderShortcuts() {
    const root = $("shortcuts");
    root.replaceChildren();

    for (const shortcut of state.shortcuts) {
        const tile = document.createElement("a");
        tile.className = "tile";
        tile.href = shortcut.url;
        tile.title = shortcut.url;

        const icon = document.createElement("span");
        icon.className = "tile-icon";
        const img = document.createElement("img");
        img.alt = "";
        img.referrerPolicy = "no-referrer";
        img.addEventListener("error", () => img.replaceWith(letterIcon(shortcut.title)), { once: true });
        img.src = new URL("/favicon.ico", shortcut.url).href;
        icon.append(img);

        const name = document.createElement("span");
        name.className = "tile-name";
        name.textContent = shortcut.title;

        const remove = document.createElement("button");
        remove.type = "button";
        remove.className = "tile-remove";
        remove.textContent = "×";
        remove.title = "Kısayolu kaldır";
        remove.setAttribute("aria-label", `${shortcut.title} kısayolunu kaldır`);
        remove.addEventListener("click", (event) => {
            event.preventDefault();
            event.stopPropagation();
            send({ type: "removeShortcut", url: shortcut.url });
        });

        tile.append(icon, name, remove);
        root.append(tile);
    }

    if (state.shortcuts.length < state.maxShortcuts) {
        const add = document.createElement("button");
        add.type = "button";
        add.className = "tile add";
        const icon = document.createElement("span");
        icon.className = "tile-icon";
        icon.textContent = "+";
        const name = document.createElement("span");
        name.className = "tile-name";
        name.textContent = "Kısayol ekle";
        add.append(icon, name);
        add.addEventListener("click", openDialog);
        root.append(add);
    }
}

// ---------------------------------------------------------------- add dialog

function openDialog() {
    $("shortcut-title").value = "";
    $("shortcut-url").value = "";
    $("dialog-error").hidden = true;
    $("dialog").hidden = false;
    $("shortcut-url").focus();
}

function closeDialog() {
    $("dialog").hidden = true;
}

$("dialog-cancel").addEventListener("click", closeDialog);
$("dialog").addEventListener("click", (event) => {
    if (event.target === $("dialog")) closeDialog();
});
document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !$("dialog").hidden) closeDialog();
});
$("dialog-form").addEventListener("submit", (event) => {
    event.preventDefault();
    const url = $("shortcut-url").value.trim();
    if (!url) return;
    // Cleared first: the app answers with "shortcutRejected" (keep open) or "shortcuts" (close).
    $("dialog-error").hidden = true;
    send({ type: "addShortcut", title: $("shortcut-title").value.trim(), url });
});

// ---------------------------------------------------------------- particles

// "petals" drift down and to the left (like the wallpapers' falling petals),
// "sparks" are small glowing embers rising, "bubbles" are thin rings floating up.
// Returns a function that stops the effect.
function startParticles(kind, colors) {
    const canvas = $("particles");
    const ctx = canvas.getContext("2d");
    const rising = kind === "sparks" || kind === "bubbles";
    let width = 0;
    let height = 0;
    let items = [];
    let frame = 0;

    function resize() {
        const ratio = window.devicePixelRatio || 1;
        width = window.innerWidth;
        height = window.innerHeight;
        canvas.width = Math.round(width * ratio);
        canvas.height = Math.round(height * ratio);
        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        const density = kind === "bubbles" ? 110 : 70;
        items = Array.from({ length: Math.min(26, Math.round(width / density)) }, () => spawn(true));
    }

    function spawn(anywhere) {
        const p = {
            size: kind === "bubbles" ? 5 + Math.random() * 12 : 4 + Math.random() * 6,
            angle: Math.random() * Math.PI * 2,
            spin: (Math.random() - 0.5) * 0.03,
            sway: Math.random() * Math.PI * 2,
            color: colors[Math.floor(Math.random() * colors.length)],
            alpha: 0.3 + Math.random() * 0.45,
        };
        if (rising) {
            p.x = Math.random() * width;
            p.y = anywhere ? Math.random() * height : height + 20;
            p.vx = (Math.random() - 0.5) * 0.2;
            p.vy = -(0.25 + Math.random() * (kind === "sparks" ? 0.7 : 0.4));
        } else {
            p.x = anywhere ? Math.random() * width : width + 20 + Math.random() * 60;
            p.y = anywhere ? Math.random() * height : -20 - Math.random() * height * 0.5;
            p.vx = -(0.25 + Math.random() * 0.45);
            p.vy = 0.35 + Math.random() * 0.55;
        }
        return p;
    }

    function draw() {
        ctx.clearRect(0, 0, width, height);
        for (const p of items) {
            p.sway += 0.015;
            p.x += p.vx + Math.sin(p.sway) * (rising ? 0.25 : 0.35);
            p.y += p.vy;
            p.angle += p.spin;
            const gone = rising ? p.y < -30 : (p.y > height + 20 || p.x < -30);
            if (gone) Object.assign(p, spawn(false));

            ctx.save();
            ctx.translate(p.x, p.y);
            ctx.globalAlpha = p.alpha;
            ctx.beginPath();
            if (kind === "sparks") {
                // Fades out as it rises; shadowBlur gives the ember glow.
                ctx.globalAlpha = p.alpha * Math.max(0, Math.min(1, p.y / (height * 0.6)));
                ctx.shadowColor = p.color;
                ctx.shadowBlur = 12;
                ctx.fillStyle = p.color;
                ctx.arc(0, 0, p.size * 0.4, 0, Math.PI * 2);
                ctx.fill();
            } else if (kind === "bubbles") {
                ctx.strokeStyle = p.color;
                ctx.lineWidth = 1.2;
                ctx.arc(0, 0, p.size, 0, Math.PI * 2);
                ctx.stroke();
                ctx.globalAlpha = p.alpha * 0.6;
                ctx.beginPath();
                ctx.fillStyle = "#ffffff";
                ctx.arc(-p.size * 0.35, -p.size * 0.35, p.size * 0.18, 0, Math.PI * 2);
                ctx.fill();
            } else {
                ctx.rotate(p.angle);
                ctx.fillStyle = p.color;
                ctx.ellipse(0, 0, p.size, p.size * 0.55, 0, 0, Math.PI * 2);
                ctx.fill();
            }
            ctx.restore();
        }
        // requestAnimationFrame pauses by itself while the tab is hidden.
        frame = requestAnimationFrame(draw);
    }

    window.addEventListener("resize", resize);
    resize();
    frame = requestAnimationFrame(draw);

    return () => {
        cancelAnimationFrame(frame);
        window.removeEventListener("resize", resize);
        ctx.clearRect(0, 0, width, height);
    };
}

let stopParticles = null;

// ---------------------------------------------------------------- config from the app

function hexToRgb(hex) {
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex || "");
    return m ? `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}` : null;
}

function applyConfig(config) {
    const theme = config.theme || {};
    const root = document.documentElement.style;
    if (theme.accent) root.setProperty("--accent", theme.accent);
    if (theme.accentSecondary) root.setProperty("--accent2", theme.accentSecondary);
    const overlay = hexToRgb(theme.overlay);
    if (overlay) root.setProperty("--overlay", overlay);

    const reducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    state.animations = config.animations !== false && !reducedMotion;
    document.body.classList.toggle("motion", state.animations);

    if (theme.wallpaper && theme.wallpaper !== state.wallpaper) {
        state.wallpaper = theme.wallpaper;
        const img = new Image();
        img.onload = () => {
            if (state.wallpaper !== theme.wallpaper) return; // a newer config won the race
            root.setProperty("--wallpaper", `url("${theme.wallpaper}")`);
            $("wall").classList.add("ready");
        };
        img.src = theme.wallpaper;
    }

    $("query").placeholder = `${config.searchEngine || "Web"} ile ara veya adres yaz`;
    state.maxShortcuts = config.maxShortcuts || state.maxShortcuts;
    state.shortcuts = config.shortcuts || [];
    renderShortcuts();

    // The config is re-sent whenever settings change, so restart rather than stack effects.
    if (stopParticles) {
        stopParticles();
        stopParticles = null;
    }
    if (state.animations && theme.particles && theme.particles !== "none") {
        stopParticles = startParticles(theme.particles, [theme.accent || "#b583ff", theme.accentSecondary || "#dd7fd0"]);
    }
}

if (host) {
    host.addEventListener("message", (event) => {
        const message = event.data || {};
        switch (message.type) {
            case "config":
                applyConfig(message);
                break;
            case "shortcuts":
                state.shortcuts = message.shortcuts || [];
                renderShortcuts();
                if (!$("dialog").hidden && $("dialog-error").hidden) closeDialog();
                break;
            case "shortcutRejected":
                $("dialog-error").hidden = false;
                break;
        }
    });
}

tick();
renderShortcuts();
send({ type: "ready" });
