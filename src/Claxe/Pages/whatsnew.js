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
        icon: "🔄",
        title: "Dosya dönüştürücü",
        text: "Kenar çubuğunun altındaki yeni düğme: resimleri, PDF'leri, Word, sunum ve tablo dosyalarını çevir. Dosyalar bilgisayarından çıkmaz. Office dosyaları için LibreOffice'i panelden tek tıkla kurabilirsin.",
        action: "Dönüştürücüyü aç",
        open: "converter",
    },
    {
        icon: "🗂️",
        title: "Sekme grupları",
        text: "Bir sekmeye sağ tıkla ve \"Yeni gruba ekle\"yi seç. Renkli etikete tıklayınca grup daralır; sağ tıklayınca adını ve rengini değiştirirsin. Gruplar oturumla birlikte kaydedilir.",
    },
    {
        icon: "🧩",
        title: "Yeni sekme widget'ları",
        text: "Hava durumu, yapılacaklar, notlar, geri sayım ve odak zamanlayıcı. Sağ alttaki Özelleştir düğmesiyle ekle, sırala ya da kaldır. Notların şifreli olarak saklanır.",
        action: "Yeni sekmeyi aç",
        open: "newtab",
    },
    {
        icon: "⏰",
        title: "Saat ve selamlama",
        text: "Saatin yanındaki kaleme bas: selamlamaya adını ekle, saniyeleri ya da 12 saat biçimini aç veya saati tamamen gizle.",
    },
    {
        icon: "⬆️",
        title: "Tek tıkla güncelleme",
        text: "Yeni bir sürüm indiğinde araç çubuğunda parlayan bir Güncelle düğmesi belirir. Basınca sekmelerin ve oturumun kaydedilir, Sorune güncellenip yeniden açılır.",
    },
    {
        icon: "🎨",
        title: "Yenilenen sayfalar",
        text: "Ayarlar, geçmiş, yer imleri ve indirmeler daha derli toplu, simgeli ve temaya uyumlu yeni bir görünüme kavuştu.",
        action: "Ayarlara göz at",
        open: "settings",
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
