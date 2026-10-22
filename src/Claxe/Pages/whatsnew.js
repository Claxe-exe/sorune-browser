// "What's new" page, opened once after Sorune updates itself.
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Sorune sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);

function send(message) {
    if (host) host.postMessage(message);
}

// Newest release first; "open" asks the app to show the feature.
const FEATURES = [
    {
        icon: "🖱️",
        title: "Orta tık artık doğru çalışıyor",
        text: "Bir bağlantıya orta tuşla (ya da Ctrl ile) tıklayınca sekme Chrome'daki gibi arka planda, açan sekmenin hemen yanında açılır; seni o sayfaya atmaz.",
    },
    {
        icon: "🎨",
        title: "Kendi temaların",
        text: "Beğendiğin görünümü bir adla kaydet: tema, duvar kâğıdı, vurgu rengi, cam, köşeler, yeni sekme efektleri ve ses paketi birlikte saklanır. İstediğin an tek tıkla geri dön.",
        action: "Temalarıma git",
        open: "userthemes",
    },
    {
        icon: "📦",
        title: "Yedekle ve taşı",
        text: "Ayarların, temaların, duvar kâğıtların, yer imlerin, panellerin ve widget'ların tek bir .sorune dosyasında. Yeni bilgisayarda dosyayı seç, Sorune yeniden başlayınca her şey yerinde.",
        action: "Yedek al",
        open: "backup",
    },
    {
        icon: "📑",
        title: "Dikey sekmeler",
        text: "İstersen sekmeler solda alt alta bir liste olur; çok sekmeyle çalışırken başlıklar okunur kalır. Varsayılan olarak kapalı, Ayarlar › Sekmeler'den aç.",
        action: "Dikey sekmeleri aç",
        open: "verticaltabs",
    },
    {
        icon: "🌙",
        title: "Sitelerde koyu mod",
        text: "Sitelere koyu ya da açık temalarını iste. Koyu teması olmayan siteler için deneysel \"Tüm siteleri karart\" seçeneği de var.",
        action: "Ayarı aç",
        open: "sitedark",
    },
    {
        icon: "📜",
        title: "Yama notları",
        text: "Mini araçlar panelinde yeni bir düğme: Sorune'nin ilk sürümünden bugüne her sürümde gelenler tek sayfada.",
        action: "Yama notlarını aç",
        open: "changelog",
    },
];

function render() {
    const root = $("features");
    root.replaceChildren();
    FEATURES.forEach((feature, index) => {
        const card = document.createElement("article");
        card.className = "feature";
        card.style.setProperty("--delay", `${0.08 + index * 0.06}s`);

        const icon = document.createElement("div");
        icon.className = "feature-icon";
        icon.textContent = feature.icon;

        const body = document.createElement("div");
        body.className = "feature-body";
        const title = document.createElement("h2");
        title.textContent = t(feature.title);
        const text = document.createElement("p");
        text.textContent = t(feature.text);
        body.append(title, text);

        if (feature.action) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "btn ghost";
            button.textContent = t(feature.action);
            button.addEventListener("click", () => send({ type: "whatsnew:open", target: feature.open }));
            body.append(button);
        }

        card.append(icon, body);
        root.append(card);
    });
}

$("done").addEventListener("click", () => send({ type: "whatsnew:open", target: "close" }));

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
        if (message.version) $("version").textContent = message.version;
    });
}

render();
send({ type: "ready" });
