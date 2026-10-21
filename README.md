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

Kullanıcı verileri `%LOCALAPPDATA%\Claxe` altında tutulur (ayarlar, oturum, WebView2 profili, log).

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
