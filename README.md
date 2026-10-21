# Claxe Browser

Anime estetiğine sahip, kişisel kullanım için geliştirilen Windows tarayıcısı.
C# / .NET 10 / WPF + Microsoft WebView2 (Chromium).

## Çalıştırma

```bash
dotnet run --project src/Claxe
```

Gereksinimler: .NET 10 SDK, Microsoft Edge WebView2 Runtime (Windows 11'de hazır gelir).

### Exe olarak derlemek

```bash
dotnet publish src/Claxe/Claxe.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o dist/Claxe
```

`dist/Claxe/Claxe.exe` (~1,2 MB) yanındaki `Assets`, `Pages`, `runtimes` klasörleri ve `WebView2Loader.dll` ile birlikte taşınmalı.
Çalıştığı bilgisayarda .NET 10 Desktop Runtime kurulu olmalı.

## Yapı

```
src/Claxe/
  Core/        MVVM temel sınıfları (ObservableObject, RelayCommand)
  Models/      Ayar ve oturum veri modelleri
  Services/    Yollar, JSON depolama, adres çözümleme, WebView2 ortamı
  ViewModels/  MainViewModel (sekmeler, oturum), TabViewModel (tek sekme + WebView2)
  Views/       Ana pencere
  Controls/    Özel paneller (sekme şeridi)
  Themes/      Renkler ve kontrol stilleri
  Pages/       Uygulamanın kendi sayfaları (yeni sekme)
  Interop/     Win32 / DWM çağrıları
```

```
  Assets/      Tema duvar kâğıtları + JSON'ları, maskot pozları (çıktıya kopyalanır)
```

Kullanıcı verileri `%LOCALAPPDATA%\Claxe` altında tutulur (ayarlar, oturum, kısayollar, WebView2 profili, log).
Test için ayrı profil: `CLAXE_PROFILE_DIR` ortam değişkeni başka bir klasörü gösterirse gerçek profile dokunulmaz.

Ham görseller (`/assets`) Git'e eklenmez; uygulamanın kullandığı kopyalar `src/Claxe/Assets` altındadır.

## Ayarlar

Ayarlar sayfası: sağ üstteki dişli, `Ctrl+,` ya da adres çubuğuna `claxe://ayarlar`.
Değişiklikler anında uygulanır ve `%LOCALAPPDATA%\Claxe\settings.json` dosyasına kaydedilir.

| Anahtar | Varsayılan | Açıklama |
|---|---|---|
| `theme` | `midnight-mei` | `midnight-mei`, `tempest-blue`, `fate-saber`, `arima-kana` |
| `wallpaper` | `null` | `null` = temanınki, `preset:{dosya}`, `custom:{dosya}` |
| `accentColor` | `null` | `#RRGGBB`; `null` = temanınki |
| `chromeOpacity` | `null` | Cam tonunun opaklığı (0.40-0.97); `null` = temanınki |
| `cornerStyle` | `soft` | `sharp`, `soft`, `round` |
| `density` | `normal` | `compact`, `normal`, `comfortable` |
| `animationsEnabled` | `true` | Arayüz animasyonları ve yeni sekme efektleri |
| `mascotEnabled` | `true` | Araç çubuğunun arkasından bakan maskot |
| `soundsEnabled` / `soundVolume` | `true` / `0.5` | Arayüz sesleri (uygulama içinde sentezlenir) |
| `searchEngine` | `google` | `google`, `duckduckgo`, `bing`, `brave` |
| `restoreSession` | `true` | Açık sekmeleri (ve oturum çerezlerini) yeniden açılışta geri getir |
| `sidebarEnabled` / `panelWidth` | `true` / `420` | Sol web paneli şeridi ve açık panelin genişliği |
| `performanceMode` | `false` | Animasyonları kapatır, arka plan sekmelerini 3 dk sonra uyutur (normalde 10 dk) |

### Yeni tema eklemek

`src/Claxe/Assets/Themes` içine `{id}.json` ve duvar kâğıdını (`thumbs/` altına 360 px önizlemesini) koymak yeterli;
tema ayarlar sayfasında kendiliğinden listelenir. Parçacık efektleri: `petals`, `sparks`, `bubbles`, `none`.

## Klavye kısayolları

| Kısayol | İşlem |
|---|---|
| Ctrl+T | Yeni sekme |
| Ctrl+Shift+T | Son kapatılan sekmeyi geri aç |
| Ctrl+W / Ctrl+F4 | Sekmeyi kapat |
| Ctrl+Tab / Ctrl+Shift+Tab | Sonraki / önceki sekme |
| Ctrl+1…8, Ctrl+9 | Sekmeye git, son sekme |
| Ctrl+L / Alt+D / F6 | Adres çubuğu |
| F5 / Ctrl+R | Yenile |
| Alt+Sol / Alt+Sağ | Geri / ileri |
| F11 | Tam ekran |
| Ctrl+, | Ayarlar |
| Ctrl+H / Ctrl+J / Ctrl+Shift+O | Geçmiş / İndirmeler / Yer imleri |
| Ctrl+D | Sayfayı yer imlerine ekle / çıkar |
| Ctrl+Shift+Del | Tarama verilerini temizle |

## Veriler ve gizlilik

`%LOCALAPPDATA%\Claxe` altında:

| Dosya | İçerik | Biçim |
|---|---|---|
| `history.dat` | Tarama geçmişi (10.000 kayıt / 180 gün) | DPAPI ile şifreli |
| `session-cookies.dat` | Kapanışta kaydedilen oturum çerezleri (tek kullanımlık, 14 gün) | DPAPI ile şifreli |
| `bookmarks.json`, `downloads.json`, `permissions.json`, `panels.json`, `shortcuts.json` | Yer imleri, indirme listesi, site izinleri, web panelleri, kısayollar | JSON |
| `WebView2\` | Chromium profili (kalıcı çerezler, önbellek) | WebView2 |

Şifreleme Windows hesabına bağlıdır; dosyalar başka bir hesapta ya da bilgisayarda açılamaz.
Şifre kaydetme kapalıdır. Telemetri yoktur.

## WebView2'nin Chrome'dan farkları

- Siteler tarayıcıyı Microsoft Edge olarak görür; Chrome Web Mağazası eklentileri kurulamaz.
- Oturum çerezlerini WebView2 kendisi saklamaz; Claxe bunları kapanışta şifreleyip açılışta geri yükler
  (Claxe çökerse o oturumun çerezleri kaybolur).
- Bağlantı ve sertifika hata sayfaları WebView2'nin kendi sayfalarıdır.
- Tarayıcı süreci çökerse Claxe yeni bir süreç başlatır ve sekmeleri yeniden yükler.
