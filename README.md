# Claxe Browser

Anime estetiğine sahip, kişisel kullanım için geliştirilen Windows tarayıcısı.
C# / .NET 10 / WPF + Microsoft WebView2 (Chromium).

## Çalıştırma

```bash
dotnet run --project src/Claxe
```

Gereksinimler: .NET 10 SDK, Microsoft Edge WebView2 Runtime (Windows 11'de hazır gelir).

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

Ayarlar ekranı gelene kadar `%LOCALAPPDATA%\Claxe\settings.json` elle düzenlenebilir:

| Anahtar | Varsayılan | Açıklama |
|---|---|---|
| `searchEngine` | `google` | `google`, `duckduckgo`, `bing`, `brave` |
| `restoreSession` | `true` | Açık sekmeleri yeniden açılışta geri getir |
| `theme` | `midnight-mei` | `Assets/Themes` altındaki tema |
| `animationsEnabled` | `true` | Arayüz animasyonları ve yeni sekme efektleri |
| `mascotEnabled` | `true` | Araç çubuğunun arkasından bakan maskot |

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
