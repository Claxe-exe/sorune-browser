<div align="center">

<img src="src/Claxe/Assets/claxe-256.png" width="112" alt="Sorune">

# Sorune

**Anime ruhlu, cam efektli, kişisel Windows tarayıcısı**<br>
<sub>An anime-styled personal web browser for Windows, made by Claxe</sub>

[![Sürüm](https://img.shields.io/github/v/release/Claxe-exe/sorune-browser?style=for-the-badge&color=b583ff&label=s%C3%BCr%C3%BCm)](https://github.com/Claxe-exe/sorune-browser/releases/latest)
[![İndirme](https://img.shields.io/github/downloads/Claxe-exe/sorune-browser/total?style=for-the-badge&color=dd7fd0&label=indirme)](https://github.com/Claxe-exe/sorune-browser/releases)
![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-5b9dff?style=for-the-badge&logo=windows11&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10-512bd4?style=for-the-badge&logo=dotnet&logoColor=white)
![WebView2](https://img.shields.io/badge/WebView2-Chromium-0f9d58?style=for-the-badge&logo=microsoftedge&logoColor=white)

<a href="https://github.com/Claxe-exe/sorune-browser/releases/latest/download/SoruneBrowser-win-Setup.exe">
  <img src="https://img.shields.io/badge/%E2%AC%87%EF%B8%8F%20%20Sorune'yi%20indir-Windows%20kurulum%20dosyas%C4%B1-b583ff?style=for-the-badge" height="42" alt="Sorune'yi indir">
</a>

</div>

> **EN:** Sorune is a personal, anime-styled web browser for Windows, built with C# / .NET 10 / WPF on top of
> Microsoft WebView2 (Chromium). Glassy themed UI, a mascot peeking over the toolbar, tab groups, new tab widgets,
> a built-in file converter, web panels, InPrivate mode and encrypted history. Interface in Turkish and English.

![Yeni sekme](docs/screenshots/new-tab.png)

## ✨ 0.5.0'da yeni

| | |
|---|---|
| ⌨️ **Komut paleti** | Ctrl+K: komutlar, açık sekmeler, yer imleri ve geçmiş tek kutuda; klavyeyle seç, Enter ile çalıştır. |
| 🪟 **Bölünmüş ekran** | İki sayfa yan yana, sürüklenebilir ayırıcı; tıkladığın panel adres çubuğunu ve geri/ileri tuşlarını yönetir. |
| 🔊 **Sekme bazlı ses** | Her sekmenin kendi ses düzeyi (0–100), diğerlerini etkilemez; oturumla kaydedilir. |
| 📺 **Resim içinde resim** | Desteklenen videoları küçük, her zaman üstte duran pencereye taşır. |
| 🧰 **Mini araçlar** | JSON, renk seçici, QR kod, kronometre ve zamanlayıcı, metin sayacı, URL kodlama; hepsi çevrimdışı. |
| 💤 **Uyku modu ve indirmeler** | Ayarlanabilir uyku süresi, sabitlenmiş sekme muafiyeti; indirmelerde hız, kalan süre ve klasör seçimi. |
| 🛟 **Çökme kurtarma** | Beklenmedik kapanıştan sonra sekmeleri geri yüklemeyi teklif eder. |

<details>
<summary>0.4.0</summary>

Dosya dönüştürücü (LibreOffice desteğiyle), sekme grupları, yeni sekme widget'ları (hava durumu, yapılacaklar, notlar,
geri sayım, odak zamanlayıcı), tek tıkla güncelleme, yenilenen ayarlar ve kitaplık sayfaları.

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
- **Sekmeler:** gruplar, bölünmüş ekran, sürükle-sırala, sabitleme, sekme bazlı ses düzeyi ve sessize alma, gizli
  (InPrivate) sekmeler, ayarlanabilir uyku modu, oturum geri yükleme ve çökme kurtarma
- **Komut paleti (Ctrl+K)** ve resim içinde resim
- **Gezinme:** yerel adres önerileri, sayfada bul, yakınlaştırma göstergesi, temalı sağ tık menüleri, özel hata sayfası
- **Kenar çubuğu:** WhatsApp, Discord, Telegram, Spotify, Twitch ve kendi web panellerin; dosya dönüştürücü ve mini araçlar
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
powershell -File build/pack.ps1 -Version 0.5.0
```

Çıktı `artifacts/releases` klasörüne yazılır; önceki sürüm GitHub'dan indirilip küçük fark (delta) paketi de üretilir.

> Hazır duvar kâğıtları depoda yoktur; yalnızca dört temanın görselleri ve maskot depodadır.
> Kendi görsellerini `src/Claxe/Assets/Wallpapers` klasörüne `.jpg` olarak (ve `thumbs/` altına küçük önizlemesini)
> koyabilir ya da uygulamadan "Görsel yükle" ile ekleyebilirsin.

Arayüz metinleri `src/Claxe/Assets/i18n/en.json` dosyasındadır (Türkçe metin → İngilizce).

## ⌨️ Klavye kısayolları

| Kısayol | İşlem |
|---|---|
| Ctrl+T / Ctrl+Shift+N | Yeni sekme / yeni gizli sekme |
| Ctrl+W / Ctrl+Shift+T | Sekmeyi kapat / son kapatılanı geri aç |
| Ctrl+Tab / Ctrl+Shift+Tab | Sonraki / önceki sekme |
| Ctrl+1…8, Ctrl+9 | Sekmeye git, son sekme |
| Ctrl+L / Alt+D / F6 | Adres çubuğu (↑ ↓ ile öneriler) |
| Ctrl+F | Sayfada bul (Enter / Shift+Enter: sonraki / önceki) |
| F5 / Ctrl+R | Yenile |
| Alt+Sol / Alt+Sağ | Geri / ileri |
| Ctrl+D | Yer imlerine ekle / çıkar |
| Ctrl+H / Ctrl+J / Ctrl+Shift+O | Geçmiş / İndirmeler / Yer imleri |
| Ctrl+K | Komut paleti |
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
```

## 🔒 Veriler ve gizlilik

Kullanıcı verileri `%LOCALAPPDATA%\Sorune` altındadır (program ise `%LOCALAPPDATA%\SoruneBrowser` altına kurulur;
kaldırmak verileri silmez).

| Dosya | İçerik | Biçim |
|---|---|---|
| `history.dat` | Tarama geçmişi (10.000 kayıt / 180 gün) | DPAPI ile şifreli |
| `session-cookies.dat` | Kapanışta kaydedilen oturum çerezleri (tek kullanımlık, 14 gün) | DPAPI ile şifreli |
| `widgets.dat` | Yeni sekme widget'ları (notlar, yapılacaklar) | DPAPI ile şifreli |
| `settings.json`, `bookmarks.json`, `downloads.json`, `permissions.json`, `panels.json`, `shortcuts.json` | Ayarlar ve listeler | JSON |
| `WebView2\` | Chromium profili (kalıcı çerezler, önbellek) | WebView2 |

Hava durumu widget'ı yalnızca yazdığın şehir adını [Open-Meteo](https://open-meteo.com)'ya gönderir. Dönüştürücü
dosyaları bilgisayarda çevirir; hiçbir dosya bir sunucuya yüklenmez.

Şifre kaydetme kapalıdır. Başka tarayıcılardan çerez ve şifre aktarılmaz (Chromium bunları yalnızca kendisinin
çözebileceği şekilde şifreler). Geliştirme ve test için `SORUNE_PROFILE_DIR` ortam değişkeni ayrı bir profil klasörü
gösterebilir.

## 🌐 WebView2'nin Chrome'dan farkları

- Siteler tarayıcıyı Microsoft Edge olarak görür (motor Edge'in motorudur; uyumluluk açısından avantajlıdır).
- Chrome Web Mağazası eklentileri kurulamaz.
- Oturum çerezlerini WebView2 kendisi saklamaz; Sorune bunları kapanışta şifreleyip açılışta geri yükler.
- Tarayıcı süreci çökerse Sorune yeni bir süreç başlatır ve sekmeleri yeniden yükler.

## 📄 Lisans

© 2026 Claxe. Tüm hakları saklıdır. Bu depo yalnızca incelenmek içindir; kod ve görseller izin alınmadan
kopyalanamaz, dağıtılamaz veya başka projelerde kullanılamaz. Kullanılan açık kaynak paketler (WebView2 SDK, Velopack,
Microsoft.Data.Sqlite, PdfPig, Open XML SDK) kendi lisanslarına tabidir.
