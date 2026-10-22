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
        icon: "⏪",
        title: "Sekme zaman makinesi",
        text: "Ctrl+Shift+T artık Chrome gibi: kapattığın sekme eski yerine, sabitlemesi ve grubuyla geri gelir; liste Sorune kapansa bile kalır. Kitaplık'taki Zaman makinesinde son 30 günün kapatılan sekmeleri ve önceki oturumların tüm sekmeleri var.",
        action: "Zaman makinesini aç",
        open: "timemachine",
    },
    {
        icon: "👥",
        title: "Profiller",
        text: "İş, okul, oyun… Her profil kendi çerezleri, girişleri, geçmişi, ayarları ve eklentileriyle ayrı bir pencerede açılır. Araç çubuğundaki yuvarlak simgeden geçiş yap.",
        action: "Profil oluştur",
        open: "profiles",
    },
    {
        icon: "📊",
        title: "Görev yöneticisi",
        text: "Shift+Esc: hangi sekme ne kadar bellek ve işlemci kullanıyor, canlı olarak. Ağır sekmeyi oradan uyut ya da kapat.",
        action: "Görev yöneticisini aç",
        open: "taskmanager",
    },
    {
        icon: "🔍",
        title: "Bağlantı önizlemesi",
        text: "Bir bağlantıya sağ tıkla ve \"Bağlantıyı önizle\"yi seç: sayfa yeni sekme açmadan küçük bir pencerede açılır. Beğenirsen \"Sekmede aç\", beğenmezsen dışarı tıkla.",
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
