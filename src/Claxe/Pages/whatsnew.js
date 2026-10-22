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
        icon: "🧩",
        title: "Eklentiler",
        text: "uBlock Origin Lite ve Bitwarden tek tıkla kurulur; istersen kendi eklentini klasörden ya da ZIP/CRX'ten ekle. Açılır penceresi olan eklentiler kenar çubuğunda görünür.",
        action: "Eklentileri aç",
        open: "extensions",
    },
    {
        icon: "🧸",
        title: "Kendi maskotun ve konuşma balonları",
        text: "Maskot artık konuşuyor: indirme bitince, ekran görüntüsü alınca, uzun süre oturunca ya da gece geç saatte. İstersen kendi karakterinin görselini yükleyip maskot yap.",
        action: "Maskotu ayarla",
        open: "mascot",
    },
    {
        icon: "🌌",
        title: "Yeni sekme senin",
        text: "Efekti (kar dahil), karartmayı, bulanıklığı ve yerleşimi seç; arama, kısayollar ve widget'ları gizleyebilirsin. Vurgu rengini duvar kâğıdından da alabilirsin.",
        action: "Yeni sekme ayarları",
        open: "newtab-settings",
    },
    {
        icon: "📸",
        title: "Ekran görüntüsü",
        text: "Araç çubuğundaki kamera: görünen alan, tam sayfa ya da sürükleyerek bölge. Resimler klasörüne kaydedilir ve panoya kopyalanır.",
    },
    {
        icon: "📖",
        title: "Okuma modu",
        text: "Makale sayfalarında araç çubuğunda kitap simgesi çıkar: yazıyı sade, temaya uygun bir sütunda oku; yazı tipi, boyut ve renk senin.",
    },
    {
        icon: "🔔",
        title: "Ses paketleri",
        text: "Arayüz sesleri için Klasik, Yumuşak, Retro ve Kristal paketleri.",
    },
    {
        icon: "⌨️",
        title: "Küçük iyileştirmeler",
        text: "Ctrl+K web sitelerinin içinde artık siteye ait (her yerde Ctrl+Shift+K paleti açar); resim içinde resim düğmesinin simgesi yenilendi.",
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
