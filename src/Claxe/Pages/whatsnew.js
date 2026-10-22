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
        icon: "🎨",
        title: "Siteye uyan renkler",
        text: "Sekme şeridi, araç çubuğu ve kenar çubuğu artık açık sitenin rengine yumuşakça geçiyor; YouTube gibi koyu sitelerde sayfa yapıştırılmış gibi durmuyor. Ayarlar → Düzen'den kapatabilirsin.",
        action: "Ayarlara göz at",
        open: "settings",
    },
    {
        icon: "🖼️",
        title: "Çerçeveli sayfa",
        text: "İstersen sayfa kenarlardan biraz içeride, ince çerçeveli bir kart olarak durur ve etrafında duvar kâğıdın görünür. Ayarlar → Düzen'den aç.",
    },    {
        icon: "⌨️",
        title: "Komut paleti",
        text: "Ctrl+K'ye bas ve yaz: komutlar, açık sekmeler, yer imleri ve geçmiş tek kutuda. Ok tuşlarıyla seç, Enter ile çalıştır.",
        action: "Paleti aç",
        open: "palette",
    },
    {
        icon: "🪟",
        title: "Bölünmüş ekran",
        text: "İki sayfayı yan yana kullan: araç çubuğundaki düğmeye bas ya da bir sekmeye sağ tıklayıp \"Sağda yan yana aç\"ı seç. Ortadaki çizgiyi sürükleyerek boyutlandır; tıkladığın panel adres çubuğunu yönetir.",
    },
    {
        icon: "🔊",
        title: "Sekme bazlı ses",
        text: "Sekmedeki hoparlöre tıkla ve sesini diğerlerinden bağımsız ayarla (sağ tık: sessize al). Ses düzeyi oturumla birlikte kaydedilir.",
    },
    {
        icon: "📺",
        title: "Resim içinde resim",
        text: "Sayfada uygun bir video varsa araç çubuğunda düğme belirir: video küçük, her zaman üstte duran bir pencereye geçer.",
    },
    {
        icon: "🧰",
        title: "Mini araçlar",
        text: "Kenar çubuğunda yeni panel: JSON biçimlendirme, renk seçici, QR kod, kronometre ve zamanlayıcı, metin sayacı, URL kodlama. Hepsi çevrimdışı.",
        action: "Araçları aç",
        open: "tools",
    },
    {
        icon: "💤",
        title: "Uyku modu ve indirmeler",
        text: "Sekmelerin ne zaman uyuyacağını sen seç; sabitlenmiş sekmeleri muaf tut. İndirmelerde hız ve kalan süre görünür, indirme klasörünü ayarlardan değiştirebilirsin.",
        action: "Ayarlara göz at",
        open: "settings",
    },
    {
        icon: "🛟",
        title: "Çökme kurtarma",
        text: "Sorune beklenmedik şekilde kapanırsa bir sonraki açılışta sekmelerini geri yüklemeyi teklif eder; \"Önceki oturuma devam et\" kapalı olsa bile.",
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
