// Every Sorune release, newest first: shown by the patch notes page, and by the what's new page
// for the releases since the version that was installed before. Each line carries its own
// English text (a long document; kept out of the shared dictionary).
"use strict";

window.RELEASES = [
    {
        version: "0.9.2",
        date: "2026-10-23",
        name: ["Ekran çevirisi (deneme)", "Screen translation (preview)"],
        items: [
            ["**Ekranı çevir (resimler dahil):** sağ tık ya da Ctrl+Shift+E. Görünen ekran Google Lens'e gönderilir; resimlerdeki yazılar dahil çeviri sayfanın üstüne, yazıların yerine konur. Yabancı webtoon okumak için: her ekranda bir kez bas, aşağı kaydır, tekrar bas. Sağ alttaki düğme çeviriyi kaldırır. Deneme sürümü: Google Lens sayfası değişirse çalışmayabilir.", "**Translate screen (including pictures):** right click or Ctrl+Shift+E. The visible screen goes to Google Lens; the translation, text in pictures included, is laid over the page in place. For foreign webtoons: press once per screen, scroll, press again. The button at the bottom right removes it. Preview: may stop working if Google changes the Lens page."],
        ],
    },
    {
        version: "0.9.1",
        date: "2026-10-23",
        name: ["Daha az bellek", "Less memory"],
        items: [
            ["**Kullanılmayan panelleri boşalt:** WhatsApp, Discord gibi paneller kapatılınca da arka planda açık kalıp her biri 100–300 MB bellek kullanıyordu. Ayarlar › Kenar çubuğu'ndan seçtiğin süre (15 dk – 2 sa) boyunca açılmayan panel artık boşaltılıyor.", "**Unload unused panels:** panels like WhatsApp and Discord stayed open in the background after closing, each using 100–300 MB. A panel not opened for the time you pick in Settings › Sidebar (15 min – 2 hr) is now unloaded."],
        ],
    },
    {
        version: "0.9.0",
        date: "2026-10-23",
        name: ["Sayfa çevirisi", "Page translation"],
        items: [
            ["**Sayfa çevirisi:** sağ tık › \"Sayfayı Türkçeye çevir\". Google Çeviri sayfayı yerinde çevirir; adres çubuğundaki \"İngilizce → Türkçe\" düğmesiyle orijinale dönülür. Sekmede sonraki sayfalar ve sonradan yüklenen yazılar da çevrilir.", "**Page translation:** right click › \"Translate page to English\". Google Translate translates the page in place; the \"… → English\" button in the address bar brings the original back. Later pages and text loaded afterwards are translated too."],
            ["**Düzeltme:** yenilikler sayfası her güncellemede aynı kartları gösteriyordu; artık yalnızca senin sürümünden sonra gelenleri gösteriyor.", "**Fix:** the what's new page showed the same cards after every update; now it shows only what came after your version."],
        ],
    },
    {
        version: "0.8.5",
        date: "2026-10-23",
        name: ["Sayfadaki videoyu indir", "Download the video on a page"],
        items: [
            ["**Mini araçlar › Video:** açık sayfadaki video dosyalarını bulur, tek tıkla İndirmeler klasörüne kaydeder. YouTube desteklenmez; parça parça yayınlanan videolar dosya olmadığı için bulunamaz.", "**Mini tools › Video:** finds video files on the open page and saves them to Downloads in one click. YouTube isn't supported; streamed videos aren't files, so they can't be found."],
        ],
    },
    {
        version: "0.8.4",
        date: "2026-10-22",
        name: ["SmartScreen anahtarı", "SmartScreen switch"],
        items: [
            ["**SmartScreen uyarıları** artık kapatılabiliyor: Ayarlar › Gizlilik ve veriler. Kapalıyken kırmızı \"güvenli olmadığı bildirildi\" sayfası hiç çıkmaz.", "**SmartScreen warnings** can now be turned off in Settings › Privacy and data. When off, the red \"reported as unsafe\" page never appears."],
        ],
    },
    {
        version: "0.8.3",
        date: "2026-10-22",
        name: ["Arama önerileri", "Search suggestions"],
        items: [
            ["**Arama önerileri:** adres çubuğuna yazarken Chrome'daki gibi arama motorunun (Google, DuckDuckGo, Bing, Brave) önerileri çıkıyor. Ayarlar › Arama'dan kapatılabilir; gizli sekmelerde hiç kullanılmaz.", "**Search suggestions:** the search engine's suggestions (Google, DuckDuckGo, Bing, Brave) appear while you type in the address bar, like in Chrome. Can be turned off in Settings › Search; never used in InPrivate tabs."],
        ],
    },
    {
        version: "0.8.2",
        date: "2026-10-22",
        name: ["Google ile giriş düzeltmesi", "Google sign-in fix"],
        items: [
            ["**Google ile giriş** (ör. Kling AI) \"Devam et\"e basınca tarayıcıyı kapatıyordu: giriş penceresi kapanırken WebView2 bütün pencereye kapan diyordu. Artık yalnızca o sekme kapanıyor.", "**Google sign-in** (e.g. Kling AI) closed the browser after \"Continue\": as the sign-in window closed, WebView2 told the whole window to close. Now only that tab closes."],
        ],
    },
    {
        version: "0.8.1",
        date: "2026-10-22",
        name: ["Düzeltme", "Fix"],
        items: [
            ["Bir site kendi sekmesini kapattığında (ör. giriş penceresi işini bitirince) son sekmeyse artık tarayıcı kapanmıyor; yerine yeni sekme açılıyor.", "When a site closes its own tab (e.g. a sign-in window that is done) and it is the last one, the browser no longer quits; a new tab opens instead."],
            ["Pencere kapandığında nedeni günlüğe yazılıyor, beklenmedik kapanmalar bulunabilsin diye.", "The reason the window closes is now written to the log, so unexpected closes can be tracked down."],
        ],
    },
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
