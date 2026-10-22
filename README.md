<div align="center">

<img src="src/Claxe/Assets/claxe-256.png" width="112" alt="Sorune">

# Sorune

**Anime ruhlu, cam efektli, kişisel Windows tarayıcısı**<br>
<sub>An anime-styled personal web browser for Windows, made by Claxe</sub>

[![Sürüm](https://img.shields.io/github/v/release/Claxe-exe/sorune-browser?style=for-the-badge&color=b583ff&label=s%C3%BCr%C3%BCm)](https://github.com/Claxe-exe/sorune-browser/releases/latest)
[![İndirme](https://img.shields.io/github/downloads/Claxe-exe/sorune-browser/total?style=for-the-badge&color=dd7fd0&label=indirme)](https://github.com/Claxe-exe/sorune-browser/releases)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-5b9dff?style=for-the-badge&logo=windows11&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10-512bd4?style=for-the-badge&logo=dotnet&logoColor=white)
[![Lisans](https://img.shields.io/badge/lisans-MIT-4ade80?style=for-the-badge)](LICENSE)
![WebView2](https://img.shields.io/badge/WebView2-Chromium-0f9d58?style=for-the-badge&logo=microsoftedge&logoColor=white)

<a href="https://github.com/Claxe-exe/sorune-browser/releases/latest/download/SoruneBrowser-win-Setup.exe">
  <img src="https://img.shields.io/badge/%E2%AC%87%EF%B8%8F%20%20Sorune'yi%20indir-Windows%20kurulum%20dosyas%C4%B1-b583ff?style=for-the-badge" height="42" alt="Sorune'yi indir">
</a>

</div>

> [!TIP]
> ### 📥 Nasıl kurulur? (30 saniye)
> 1. **[SoruneBrowser-win-Setup.exe'yi indir](https://github.com/Claxe-exe/sorune-browser/releases/latest/download/SoruneBrowser-win-Setup.exe)** (~85 MB, her zaman en son sürüm)
> 2. İndirdiğin dosyayı çalıştır. Windows *"bilgisayarınızı korudu"* derse **Ek bilgi → Yine de çalıştır**'a bas (dosya imzasız olduğu için çıkar).
> 3. Bu kadar! Sorune masaüstüne ve Başlat menüsüne eklenir, yeni sürümleri kendisi indirir.
>
> Kurulum istemiyorsan: **[taşınabilir sürüm (ZIP)](https://github.com/Claxe-exe/sorune-browser/releases/latest/download/SoruneBrowser-win-Portable.zip)** indir, bir klasöre çıkar, içindeki `Sorune.exe`'yi aç.
>
> ⚠️ Yeşil **Code → Download ZIP** düğmesi uygulamayı değil **kaynak kodunu** indirir; içinde çalıştırılacak bir program yoktur.
> **EN:** Sorune is a personal, anime-styled web browser for Windows, built with C# / .NET 10 / WPF on top of
> Microsoft WebView2 (Chromium). Glassy themed UI, a mascot peeking over the toolbar, tab groups, new tab widgets,
> a built-in file converter, web panels, InPrivate mode and encrypted history. Interface in Turkish and English.

![Yeni sekme](docs/screenshots/new-tab.png)

## ✨ 0.8'de yeni

| | |
|---|---|
| 🎨 **Kendi temaların** | Şu anki görünümü (tema, duvar kâğıdı, renkler, cam, köşeler, yeni sekme, sesler) bir adla kaydet; tek tıkla geri dön. |
| 📦 **Yedekle ve taşı** | Ayarlar, temalar, duvar kâğıtları, yer imleri, paneller ve widget'lar tek bir `.sorune` dosyasında; yeni bilgisayarda geri yükle. |
| 📑 **Dikey sekmeler** | İsteğe bağlı: sekmeler solda alt alta bir liste; sürükle-sırala, genişlik ayarlı. |
| 🌙 **Sitelerde koyu mod** | Sitelerden koyu / açık temalarını iste; deneysel "tüm siteleri karart". |
| 📜 **Yama notları** | Mini araçlar panelinden: ilk sürümden bugüne her sürümde gelenler. |
| 🖱️ **Düzeltmeler** | Orta tıkla (ya da Ctrl+tık) açılan bağlantılar Chrome gibi arka planda, sekmenin yanında açılır; uyutulan sekmeler açılınca artık donuk kalmıyor. |

## 0.7'de yeni

| | |
|---|---|
| ⏪ **Sekme zaman makinesi** | Ctrl+Shift+T kapattığın sekmeyi eski yerine, sabitlemesi ve grubuyla geri açar; liste yeniden başlatmada kaybolmaz. Kitaplık'ta son 30 günün kapatılan sekmeleri ve önceki oturumlar. |
| 👥 **Profiller** | Kendi çerezleri, girişleri, geçmişi, ayarları ve eklentileriyle tamamen ayrı profiller; her biri kendi penceresinde. |
| 📊 **Görev yöneticisi** | Shift+Esc: sekme başına canlı bellek ve işlemci; ağır sekmeyi uyut ya da kapat. |
| 🔍 **Bağlantı önizlemesi** | Bağlantıya sağ tık → "Bağlantıyı önizle": sekme açmadan küçük pencerede bak. |

<details>
<summary>0.6</summary>


| | |
|---|---|
| 🧩 **Eklentiler** | uBlock Origin Lite ve Bitwarden tek tıkla (resmi GitHub sürümlerinden); kendi eklentini klasörden ya da ZIP/CRX'ten ekle. Açılır penceresi olanlar kenar çubuğunda. |
| 🧸 **Kendi maskotun** | İstediğin karakterin görselini yükle; maskot artık konuşma balonlarıyla tepki veriyor ve mola / uyku hatırlatıyor. |
| 🌌 **Yeni sekme senin** | Efekt (kar dahil), karartma, bulanıklık, sol / orta yerleşim, öğeleri gizleme; vurgu rengini duvar kâğıdından çıkarma. |
| 📸 **Ekran görüntüsü** | Görünen alan, tam sayfa ya da sürükleyerek bölge; panoya da kopyalanır. |
| 📖 **Okuma modu** | Makaleleri temaya uygun, sade bir sütunda; yazı tipi, boyut ve renk ayarlı. |
| 👋 **Karşılama turu** | İlk açılışta dil, tema, tarz, eklentiler ve içe aktarma adım adım. |
| 🔔 **Ses paketleri** | Klasik, Yumuşak, Retro, Kristal. |

</details>

<details>
<summary>0.5 ve 0.4</summary>

**0.5:** komut paleti (Ctrl+K), bölünmüş ekran, sekme bazlı ses, resim içinde resim, mini araçlar (JSON, renk, QR,
kronometre, sayaç, URL), ayarlanabilir uyku modu, indirme hızı ve klasör seçimi, çökme kurtarma, siteye uyan renkler,
çerçeveli sayfa.
**0.4:** dosya dönüştürücü (LibreOffice desteğiyle), sekme grupları, yeni sekme widget'ları, tek tıkla güncelleme,
yenilenen ayarlar ve kitaplık.

</details>

## 🎨 Temalar

<table>
  <tr>
    <td align="center"><img src="src/Claxe/Assets/Themes/thumbs/midnight-mei.jpg" width="190"><br><b>Midnight Mei</b></td>
    <td align="center"><img src="src/Claxe/Assets/Themes/thumbs/tempest-blue.jpg" width="190"><br><b>Tempest Blue</b></td>
    <td align="center"><img src="src/Claxe/Assets/Themes/thumbs/fate-saber.jpg" width="190"><br><b>Fate Saber</b></td>
    <td align="center"><img src="src/Claxe/Assets/Themes/thumbs/arima-kana.jpg" width="190"><br><b>Crimson Kana</b></td>
  </tr>
</table>

Her temanın kendi vurgu rengi, cam tonu ve yeni sekme efekti (yaprak, kıvılcım, kabarcık) var. Vurgu rengi, saydamlık,
köşe stili ve yoğunluk ayrıca ayarlanır; istersen kendi duvar kâğıdını yüklersin.

![Temalar](docs/screenshots/themes.png)

## 🧭 Özellikler

- **Görünüm:** cam efektli sekme ve araç çubuğu, kavisli neon aktif sekme, araç çubuğunun arkasından bakan maskot
  (sayfa yüklenince sevinir, boşta kalınca uyur; kapatılabilir)
- **Yeni sekme:** saat ve selamlama, arama, kısayollar, widget'lar, temaya göre efektler
- **Sekmeler:** üstte ya da dikey liste, gruplar, bölünmüş ekran, sürükle-sırala, sabitleme, orta tıkla arka planda
  açma, sekme bazlı ses düzeyi ve sessize alma, gizli
  (InPrivate) sekmeler, ayarlanabilir uyku modu, oturum geri yükleme ve çökme kurtarma
- **Komut paleti (Ctrl+K)** ve resim içinde resim
- **Gezinme:** arama motoru önerileri (Chrome gibi; kapatılabilir), geçmiş ve yer imi önerileri, sayfada bul, yakınlaştırma göstergesi, temalı sağ tık menüleri, özel hata sayfası
- **Kenar çubuğu:** WhatsApp, Discord, Telegram, Spotify, Twitch ve kendi web panellerin; dosya dönüştürücü, mini araçlar
  ve eklentiler
- **Kişiselleştirme:** tema, duvar kâğıdı, duvar kâğıdından vurgu rengi, cam, köşeler, yoğunluk, kendi maskotun, ses
  paketi, yeni sekme efekti / karartma / bulanıklık / yerleşim, siteye uyan renkler, çerçeveli sayfa, sitelerde koyu
  mod; hepsini **kendi teman** olarak kaydetme
- **Yedekle ve taşı:** her şey tek bir `.sorune` dosyasında, başka bilgisayara taşınabilir
- **Araçlar:** ekran görüntüsü, sayfadaki videoyu indirme (YouTube hariç), okuma modu, resim içinde resim, komut paleti, bağlantı önizlemesi, görev yöneticisi
- **Profiller ve zaman makinesi:** birbirinden bağımsız profiller; kapatılan sekmeler ve önceki oturumlar
- **Kitaplık:** geçmiş, klasörlü yer imleri, kendi indirme yöneticisi
- **Gizlilik:** geçmiş, oturum çerezleri ve widget notları Windows hesabına bağlı şifrelenir (DPAPI); site izinleri
  hatırlanır; tarama verileri zaman aralığıyla temizlenir; telemetri yok
- **İçe aktarma:** Chrome, Opera GX, Edge, Brave, Opera, Vivaldi ve Firefox'tan yer imleri ve geçmiş
- **Diğer:** Türkçe / İngilizce arayüz, varsayılan tarayıcı olabilme, tek pencere, arayüz sesleri, performans modu,
  otomatik güncelleme

## 📦 Kurulum

[Son sürümdeki](https://github.com/Claxe-exe/sorune-browser/releases/latest) `SoruneBrowser-win-Setup.exe` dosyasını
çalıştır. .NET kurulu olması gerekmez; WebView2 Runtime Windows 11'de hazır gelir. Sorune
`%LOCALAPPDATA%\SoruneBrowser` klasörüne kurulur ve yeni sürümleri kendisi indirir.

> **"Windows bilgisayarınızı korudu" uyarısı:** kurulum dosyası kod imzalama sertifikasıyla imzalı değil (sertifikalar
> ücretli). Uyarıda **Ek bilgi → Yine de çalıştır**'a bas. İçinden emin olmak istersen dosyayı
> [VirusTotal](https://www.virustotal.com)'da taratabilir ya da kaynak koddan kendin derleyebilirsin.

## 🛠️ Kaynaktan derleme

```bash
dotnet run --project src/Claxe
```

Gereksinimler: Windows 10/11, .NET 10 SDK.

Kurulum dosyası üretmek için ([Velopack](https://velopack.io)):

```bash
dotnet tool install -g vpk
```

```bash
powershell -File build/pack.ps1 -Version 0.8.0
```

Çıktı `artifacts/releases` klasörüne yazılır; önceki sürüm GitHub'dan indirilip küçük fark (delta) paketi de üretilir.

Testler (dönüştürücü, ayar doğrulama, widget'lar, eklenti paketleri) ve QR kodlayıcı denetimi:

```bash
dotnet test tests/Sorune.Tests
```

```bash
node tests/qr.test.js
```

> Hazır duvar kâğıtları depoda yoktur; yalnızca dört temanın görselleri ve maskot depodadır.
> Kendi görsellerini `src/Claxe/Assets/Wallpapers` klasörüne `.jpg` olarak (ve `thumbs/` altına küçük önizlemesini)
> koyabilir ya da uygulamadan "Görsel yükle" ile ekleyebilirsin.

Arayüz metinleri `src/Claxe/Assets/i18n/en.json` dosyasındadır (Türkçe metin → İngilizce).

## ⌨️ Klavye kısayolları

| Kısayol | İşlem |
|---|---|
| Ctrl+T / Ctrl+Shift+N | Yeni sekme / yeni gizli sekme |
| Ctrl+W / Ctrl+Shift+T | Sekmeyi kapat / son kapatılanı yerine geri aç |
| Shift+Esc | Görev yöneticisi |
| Ctrl+Tab / Ctrl+Shift+Tab | Sonraki / önceki sekme |
| Ctrl+1…8, Ctrl+9 | Sekmeye git, son sekme |
| Ctrl+L / Alt+D / F6 | Adres çubuğu (↑ ↓ ile öneriler) |
| Ctrl+F | Sayfada bul (Enter / Shift+Enter: sonraki / önceki) |
| F5 / Ctrl+R | Yenile |
| Alt+Sol / Alt+Sağ | Geri / ileri |
| Ctrl+D | Yer imlerine ekle / çıkar |
| Ctrl+H / Ctrl+J / Ctrl+Shift+O | Geçmiş / İndirmeler / Yer imleri |
| Ctrl+K / Ctrl+Shift+K | Komut paleti (web sitelerinin içinde Ctrl+Shift+K) |
| Ctrl+, / Ctrl+Shift+Del | Ayarlar / Tarama verilerini temizle |
| F11 | Tam ekran |

## 🗂️ Proje yapısı

```
src/Claxe/
  Core/        MVVM temel sınıfları
  Models/      Ayar, oturum ve tema modelleri
  Services/    Tema, geçmiş, yer imleri, indirmeler, izinler, widget'lar, hava durumu, güncelleme, ...
    Conversion/  Dosya dönüştürücü (resim, PDF, Word, LibreOffice)
  ViewModels/  Ana pencere, sekme, sekme grubu, panel, maskot, ayarlar, dönüştürücü
  Views/       Ana pencere
  Controls/    Sekme şeridi, sekme şekli, maskot
  Themes/      Tasarım anahtarları ve kontrol stilleri (WPF)
  Pages/       Uygulamanın kendi sayfaları: yeni sekme, ayarlar, kitaplık, dönüştürücü, yenilikler (HTML/CSS/JS)
  Assets/      Tema görselleri ve JSON'ları, maskot, simge, çeviriler
build/pack.ps1 Kurulum dosyası
tests/         Birim testleri (xUnit) ve QR kodlayıcı denetimi
```

## 🔒 Veriler ve gizlilik

Kullanıcı verileri `%LOCALAPPDATA%\Sorune` altındadır (program ise `%LOCALAPPDATA%\SoruneBrowser` altına kurulur;
kaldırmak verileri silmez).

| Dosya | İçerik | Biçim |
|---|---|---|
| `history.dat` | Tarama geçmişi (10.000 kayıt / 180 gün) | DPAPI ile şifreli |
| `session-cookies.dat` | Kapanışta kaydedilen oturum çerezleri (tek kullanımlık, 14 gün) | DPAPI ile şifreli |
| `widgets.dat` | Yeni sekme widget'ları (notlar, yapılacaklar) | DPAPI ile şifreli |
| `closed-tabs.dat`, `sessions-archive.dat` | Zaman makinesi: kapatılan sekmeler ve önceki oturumlar | DPAPI ile şifreli |
| `profiles.json`, `profiles\` | Profil listesi ve her profilin ayrı veri klasörü | JSON / klasör |
| `settings.json`, `bookmarks.json`, `downloads.json`, `permissions.json`, `panels.json`, `shortcuts.json`, `user-themes.json` | Ayarlar, listeler ve kayıtlı temalar | JSON |
| `WebView2\` | Chromium profili (kalıcı çerezler, önbellek) | WebView2 |

Microsoft Defender SmartScreen (tehlikeli site ve indirme uyarıları) varsayılan olarak açıktır; Ayarlar › Gizlilik ve
veriler'den kapatılabilir. Adres çubuğundaki arama önerileri için yazdıkların seçili arama motoruna gönderilir (gizli sekmelerde asla); Ayarlar ›
Arama'dan kapatılabilir. Hava durumu widget'ı yalnızca yazdığın şehir adını [Open-Meteo](https://open-meteo.com)'ya gönderir. Dönüştürücü
dosyaları bilgisayarda çevirir; hiçbir dosya bir sunucuya yüklenmez.

**Yedek dosyaları** (`.sorune`) bir ZIP'tir: şifreli dosyalar başka bir Windows hesabında açılabilsin diye yedeğe
çözülmüş olarak girer, geri yüklenince yeniden şifrelenir. Bu yüzden yedeği güvendiğin bir yerde sakla. Çerezler,
şifreler ve önbellek yedeğe hiç girmez; geçmiş yalnızca istersen girer.

Şifre kaydetme kapalıdır. Başka tarayıcılardan çerez ve şifre aktarılmaz (Chromium bunları yalnızca kendisinin
çözebileceği şekilde şifreler). Geliştirme ve test için `SORUNE_PROFILE_DIR` ortam değişkeni ayrı bir profil klasörü
gösterebilir.

## 🌐 WebView2'nin Chrome'dan farkları

- Siteler tarayıcıyı Microsoft Edge olarak görür (motor Edge'in motorudur; uyumluluk açısından avantajlıdır).
- Chrome Web Mağazası eklentileri kurulamaz.
- Oturum çerezlerini WebView2 kendisi saklamaz; Sorune bunları kapanışta şifreleyip açılışta geri yükler.
- Tarayıcı süreci çökerse Sorune yeni bir süreç başlatır ve sekmeleri yeniden yükler.

## 📄 Lisans

Sorune'nin kaynak kodu [MIT lisansı](LICENSE) ile açıktır: kullanabilir, değiştirebilir, dağıtabilir, kendi
projelerinde kullanabilirsin; yeter ki telif ve lisans bildirimini koru. © 2026 Claxe.

**İstisna:** tema duvar kâğıtları, maskot, uygulama simgesi ve ekran görüntülerindeki anime görselleri bu lisansa
dahil değildir; hakları sahiplerine aittir. Ayrıntılar ve kullanılan açık kaynak paketlerin lisansları için
[NOTICE.md](NOTICE.md).

<sub>**EN:** Sorune's source code is licensed under [MIT](LICENSE). The anime artwork (theme wallpapers, mascot, app
icon, screenshots) is excluded and belongs to its respective owners; see [NOTICE.md](NOTICE.md).</sub>
