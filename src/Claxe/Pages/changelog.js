// Patch notes: every Sorune release, newest first. Opened from the mini tools panel.
// Each line carries its own English text (a long document; kept out of the shared dictionary).
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Sorune sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);
const english = window.i18n && window.i18n.lang === "en";

function send(message) {
    if (host) host.postMessage(message);
}

const RELEASES = [
    {
        version: "0.8.0",
        date: "2026-10-22",
        name: ["Kendi temaların", "Your own themes"],
        items: [
            ["**Orta tıkla açılan sekmeler** artık Chrome gibi arka planda, açan sekmenin hemen yanında açılır; seni o sekmeye atmaz.", "**Middle-clicked links** now open in the background next to their tab, like Chrome, instead of jumping to the new tab."],
            ["**Kendi temaların:** şu anki görünümü (tema, duvar kâğıdı, renkler, cam, köşeler, yeni sekme ve sesler) bir adla kaydet, tek tıkla geri dön.", "**Your own themes:** save the current look (theme, wallpaper, colours, glass, corners, new tab and sounds) under a name and switch back in one click."],
            ["**Yedekle ve taşı:** ayarlar, temalar, duvar kâğıtları, yer imleri, paneller ve widget'lar tek bir .sorune dosyasına; yeni bilgisayarda geri yükle.", "**Back up and move:** settings, themes, wallpapers, bookmarks, panels and widgets in one .sorune file; restore it on a new computer."],
            ["**Dikey sekmeler** (isteğe bağlı): sekmeler solda alt alta bir liste olur; sürükleyip sıralanır, genişliği ayarlanır.", "**Vertical tabs** (optional): tabs become a list on the left; drag to reorder, width is adjustable."],
            ["**Sitelerde koyu mod:** sitelere koyu ya da açık tema iste; deneysel \"tüm siteleri karart\" seçeneği koyu teması olmayan siteleri de karartır.", "**Dark mode for sites:** ask sites for their dark or light theme; the experimental \"darken all sites\" option also darkens sites without one."],
            ["**Yama notları:** bu sayfa. Mini araçlar panelinden açılır.", "**Patch notes:** this page, opened from the mini tools panel."],
            ["**Düzeltme:** uyutulan sekmeler açılınca donuk kalmıyor, hemen uyanıyor.", "**Fix:** slept tabs no longer stay frozen when you open them; they wake up right away."],
        ],
    },
    {
        version: "0.7.0",
        date: "2026-10-22",
        name: ["Zaman makinesi", "Time machine"],
        items: [
            ["**Sekme zaman makinesi:** Ctrl+Shift+T kapattığın sekmeyi eski yerine, sabitlemesi ve grubuyla geri getirir; son 30 günün kapatılan sekmeleri ve önceki oturumlar Kitaplık'ta.", "**Tab time machine:** Ctrl+Shift+T brings a closed tab back to its old place with its pin and group; the last 30 days of closed tabs and previous sessions live in the Library."],
            ["**Profiller:** iş, okul, oyun… her profil kendi çerezleri, geçmişi, ayarları ve eklentileriyle ayrı pencerede.", "**Profiles:** work, school, games… each profile has its own cookies, history, settings and extensions in its own window."],
            ["**Görev yöneticisi** (Shift+Esc): hangi sekme ne kadar bellek ve işlemci kullanıyor, canlı.", "**Task manager** (Shift+Esc): live memory and CPU use per tab."],
            ["**Bağlantı önizlemesi:** sağ tık › Bağlantıyı önizle; sayfa yeni sekme açmadan küçük bir pencerede açılır.", "**Link preview:** right click › Preview link opens the page in a small window without a new tab."],
        ],
    },
    {
        version: "0.6.0",
        date: "2026-10-22",
        name: ["Kişiselleştirme", "Personalisation"],
        items: [
            ["**Eklentiler:** uBlock Origin Lite ve Bitwarden tek tıkla; açılmış Chromium eklentileri de yüklenebilir.", "**Extensions:** uBlock Origin Lite and Bitwarden in one click; unpacked Chromium extensions can be loaded too."],
            ["**Ekran görüntüsü:** görünen alan, tam sayfa ya da seçilen bölge.", "**Screenshots:** visible area, full page or a selected region."],
            ["**Okuma modu:** makaleler reklamsız, sade bir sayfada.", "**Reader mode:** articles on a clean page without ads."],
            ["**Konuşan maskot** ve kendi maskot görselin; **ses paketleri**.", "**Talking mascot** and your own mascot picture; **sound packs**."],
            ["Yeni sekme görünüm ayarları (efekt, karartma, bulanıklık, yerleşim) ve **duvar kâğıdından vurgu rengi**.", "New tab look settings (effect, dim, blur, layout) and **accent colour from the wallpaper**."],
            ["**Karşılama turu**, birim testleri ve otomatik derleme denetimi.", "**Welcome tour**, unit tests and automatic build checks."],
        ],
    },
    {
        version: "0.5.2",
        date: "2026-10-22",
        name: ["Siteye uyan renkler", "Colours that match the site"],
        items: [
            ["Sekme şeridi ve kenar çubuğu da açık sitenin rengine uyar.", "The tab strip and the sidebar also take on the open site's colour."],
        ],
    },
    {
        version: "0.5.1",
        date: "2026-10-22",
        name: ["Çerçeveli sayfa", "Framed page"],
        items: [
            ["Araç çubuğu açık sitenin rengine yumuşakça geçer (ayarlardan kapatılabilir).", "The toolbar fades into the open site's colour (can be turned off)."],
            ["**Çerçeveli sayfa:** sayfa ince parlayan bir kenarla içeride durur.", "**Framed page:** the page sits inset with a thin glowing edge."],
        ],
    },
    {
        version: "0.5.0",
        date: "2026-10-22",
        name: ["Güç araçları", "Power tools"],
        items: [
            ["**Çökme kurtarma:** beklenmedik kapanmadan sonra sekmeler geri gelir.", "**Crash recovery:** tabs come back after an unexpected shutdown."],
            ["İndirmelerde hız, kalan süre ve **indirme klasörü** seçimi.", "Download speed, time left and a choice of **download folder**."],
            ["**Sekme başına ses düzeyi**, uyku ayarları, **bölünmüş ekran** ve **resim içinde resim**.", "**Per-tab volume**, sleep settings, **split screen** and **picture-in-picture**."],
            ["**Mini araçlar** (JSON, renk, QR, süre, sayaç, URL) ve **Ctrl+K komut paleti**.", "**Mini tools** (JSON, colour, QR, timer, counter, URL) and the **Ctrl+K command palette**."],
        ],
    },
    {
        version: "0.4.0",
        date: "2026-10-21",
        name: ["Dönüştürücü ve gruplar", "Converter and groups"],
        items: [
            ["**Dosya dönüştürücü:** PDF, Word, görseller ve daha fazlası, kenar çubuğundan.", "**File converter:** PDF, Word, images and more, from the sidebar."],
            ["**Sekme grupları**, yeni sekmede **widget'lar** ve araç çubuğunda **güncelleme rozeti**.", "**Tab groups**, **widgets** on the new tab and an **update badge** in the toolbar."],
            ["Yenilikler sayfası, yenilenen ayarlar ve kitaplık tasarımı.", "A what's new page and redesigned settings and library."],
        ],
    },
    {
        version: "0.3.0",
        date: "2026-10-21",
        name: ["Yeni isim: Sorune", "New name: Sorune"],
        items: [
            ["Claxe artık **Sorune**; **İngilizce arayüz** geldi.", "Claxe is now **Sorune**, with an **English interface**."],
            ["**Kurulum dosyası** ve **otomatik güncelleme**.", "**Installer** and **automatic updates**."],
            ["Adres önerileri, varsayılan tarayıcı yapma, gizli sekme, sayfada bul ve sekme menüsü.", "Address suggestions, set as default browser, private tabs, find in page and the tab menu."],
            ["Başka tarayıcılardan yer imi ve geçmiş aktarma; özel hata sayfası; yer imi klasörleri.", "Import bookmarks and history from other browsers; a custom error page; bookmark folders."],
        ],
    },
    {
        version: "0.1 – 0.2",
        date: "2026-10-21",
        name: ["İlk adımlar", "First steps"],
        items: [
            ["Sekmeler, gezinme, klavye kısayolları ve oturumu geri yükleme.", "Tabs, navigation, keyboard shortcuts and session restore."],
            ["**Midnight Mei** tasarımı, yeni sekme sayfası ve **maskot**.", "The **Midnight Mei** design, the new tab page and the **mascot**."],
            ["Temalar, duvar kâğıtları, ayarlar sayfası ve sesler.", "Themes, wallpapers, the settings page and sounds."],
            ["Geçmiş, yer imleri, indirmeler, site izinleri ve **web panelleri**.", "History, bookmarks, downloads, site permissions and **web panels**."],
        ],
    },
];

/** "**bold**" parts become <strong>; everything else stays plain text (no HTML from strings). */
function richText(text) {
    const fragment = document.createDocumentFragment();
    text.split("**").forEach((part, index) => {
        if (!part) return;
        if (index % 2) {
            const strong = document.createElement("strong");
            strong.textContent = part;
            fragment.append(strong);
        } else {
            fragment.append(document.createTextNode(part));
        }
    });
    return fragment;
}

function pick(pair) {
    return english ? pair[1] : pair[0];
}

function anchor(version) {
    return "v" + version.replace(/[^0-9a-z]+/gi, "-");
}

function formatDate(iso) {
    const date = new Date(iso + "T12:00:00");
    return date.toLocaleDateString((window.i18n && window.i18n.locale) || "tr-TR", { day: "numeric", month: "long", year: "numeric" });
}

function render(current) {
    const jump = $("jump");
    const list = $("releases");
    jump.replaceChildren();
    list.replaceChildren();

    RELEASES.forEach((release, index) => {
        const link = document.createElement("a");
        link.href = "#" + anchor(release.version);
        link.textContent = release.version;
        if (release.version === current) link.className = "current";
        jump.append(link);

        const card = document.createElement("article");
        card.className = "release" + (index === 0 ? " latest" : "");
        card.id = anchor(release.version);
        card.style.setProperty("--delay", `${Math.min(index, 6) * 0.05}s`);

        const head = document.createElement("header");
        head.className = "release-head";
        const title = document.createElement("h2");
        title.textContent = release.version;
        const name = document.createElement("span");
        name.className = "name";
        name.textContent = pick(release.name);
        head.append(title, name);
        if (release.version === current) {
            const badge = document.createElement("span");
            badge.className = "badge";
            badge.textContent = t("Kullandığın sürüm");
            head.append(badge);
        }
        const date = document.createElement("span");
        date.className = "date";
        date.textContent = formatDate(release.date);
        head.append(date);

        const items = document.createElement("ul");
        for (const item of release.items) {
            const li = document.createElement("li");
            li.append(richText(pick(item)));
            items.append(li);
        }

        card.append(head, items);
        list.append(card);
    });
}

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
        // The config comes again on every look change: re-render only for a new version.
        const version = message.version ? String(message.version).split("+")[0] : null;
        if (version && version !== $("version").textContent) {
            $("version").textContent = version;
            render(version);
        }
    });
}

render(null);
send({ type: "ready" });
