using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Claxe.Core;
using Claxe.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Claxe.ViewModels;

public enum SecurityState
{
    /// <summary>Claxe's own pages; nothing to show.</summary>
    Internal,
    Secure,
    NotSecure,
    /// <summary>HTTPS with an invalid certificate. WebView2 shows its own warning page.</summary>
    CertificateError,
    Local,
}

/// <summary>
/// One browser tab. Owns its WebView2 control, which is created lazily the first
/// time the tab is shown, so restored background tabs cost nothing until opened.
/// </summary>
public sealed class TabViewModel : ObservableObject, IDisposable
{
    private const string NewTabTitle = "Yeni Sekme";

    private static readonly JsonSerializerOptions PageJson = new(JsonSerializerDefaults.Web);

    private Task? _initTask;
    private string? _pendingUrl;
    private bool _addressDirty;
    private bool _certificateError;
    private bool _disposed;

    private WebView2? _webView;
    private string? _url;
    private string _title;
    private string _addressText;
    private ImageSource? _favicon;
    private bool _isLoading;
    private bool _canGoBack;
    private bool _canGoForward;
    private bool _isActive;
    private bool _isContentFullScreen;
    private SecurityState _security;

    public TabViewModel(string? initialUrl, string? title = null)
    {
        _pendingUrl = initialUrl;
        _url = initialUrl;
        _title = string.IsNullOrWhiteSpace(title) ? TitleFor(initialUrl) : title;
        _addressText = InternalPages.ToDisplayAddress(initialUrl);
        _security = SecurityFor(initialUrl);
    }

    public event EventHandler<CoreWebView2NewWindowRequestedEventArgs>? NewWindowRequested;
    public event EventHandler? CloseRequested;

    /// <summary>A message posted by one of Claxe's own pages (never by a website).</summary>
    public event EventHandler<JsonElement>? InternalPageMessage;

    /// <summary>Raised when a top-level navigation ends; the argument tells whether it succeeded.</summary>
    public event EventHandler<bool>? NavigationFinished;

    /// <summary>Null until the tab is first shown.</summary>
    public WebView2? WebView
    {
        get => _webView;
        private set => SetProperty(ref _webView, value);
    }

    public CoreWebView2? Core => _webView?.CoreWebView2;

    public string? Url
    {
        get => _url;
        private set
        {
            if (SetProperty(ref _url, value))
                UpdateSecurity();
        }
    }

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    /// <summary>
    /// Text in the address bar. Typing marks it dirty so page navigation
    /// does not overwrite what the user is in the middle of typing.
    /// </summary>
    public string AddressText
    {
        get => _addressText;
        set
        {
            if (SetProperty(ref _addressText, value))
                _addressDirty = true;
        }
    }

    public ImageSource? Favicon
    {
        get => _favicon;
        private set => SetProperty(ref _favicon, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public bool CanGoBack
    {
        get => _canGoBack;
        private set => SetProperty(ref _canGoBack, value);
    }

    public bool CanGoForward
    {
        get => _canGoForward;
        private set => SetProperty(ref _canGoForward, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    /// <summary>True while the page shows an element fullscreen (e.g. a video).</summary>
    public bool IsContentFullScreen
    {
        get => _isContentFullScreen;
        private set => SetProperty(ref _isContentFullScreen, value);
    }

    public SecurityState Security
    {
        get => _security;
        private set => SetProperty(ref _security, value);
    }

    public bool IsNewTabPage => InternalPages.IsNewTab(Url);

    /// <summary>Creates the WebView2 if needed. Safe to call repeatedly.</summary>
    public Task EnsureCoreAsync() => _initTask ??= InitializeAsync();

    /// <summary>Fire-and-forget variant for UI code; failures are logged and shown on the tab.</summary>
    public async void EnsureStarted()
    {
        try
        {
            await EnsureCoreAsync();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or WebView2RuntimeNotFoundException)
        {
            Log.Error($"Sekme başlatılamadı: {Url}", ex);
            Title = "Sekme başlatılamadı";
            IsLoading = false;
        }
    }

    public void Navigate(string url)
    {
        _addressDirty = false;
        SetAddressFromPage(url);

        if (Core is { } core)
        {
            core.Navigate(url);
            return;
        }

        _pendingUrl = url;
        Url = url;
        EnsureStarted();
    }

    public void GoBack() => Core?.GoBack();

    public void GoForward() => Core?.GoForward();

    public void Reload() => Core?.Reload();

    public void Stop() => Core?.Stop();

    /// <summary>Sends data to the page, but only while it is one of Claxe's own pages.</summary>
    public void PostToInternalPage(object payload)
    {
        if (Core is { } core && InternalPages.IsInternal(core.Source))
            core.PostWebMessageAsJson(JsonSerializer.Serialize(payload, PageJson));
    }

    /// <summary>Puts the address bar back to the current page URL (Escape).</summary>
    public void RevertAddress()
    {
        _addressDirty = false;
        SetAddressFromPage(Url);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var webView = _webView;
        WebView = null;
        webView?.Dispose();
    }

    private async Task InitializeAsync()
    {
        var background = ThemeService.BaseColor;
        var webView = new WebView2
        {
            // Matches the theme background so a fresh tab never flashes white.
            DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, background.R, background.G, background.B),
        };
        WebView = webView;

        var environment = await BrowserEnvironment.GetAsync();
        await webView.EnsureCoreWebView2Async(environment);
        if (_disposed)
            return;

        var core = webView.CoreWebView2;
        ConfigureSettings(core.Settings);
        InternalPages.Register(core);

        core.SourceChanged += (_, _) => OnSourceChanged(core);
        core.NavigationStarting += (_, _) =>
        {
            IsLoading = true;
            _certificateError = false;
            UpdateSecurity();
        };
        // Only observed: the default action keeps WebView2's warning page. Errors are never auto-accepted.
        core.ServerCertificateErrorDetected += (_, _) =>
        {
            _certificateError = true;
            UpdateSecurity();
        };
        core.NavigationCompleted += (_, e) =>
        {
            IsLoading = false;
            NavigationFinished?.Invoke(this, e.IsSuccess);
        };
        core.WebMessageReceived += (_, e) => OnWebMessage(e);
        core.HistoryChanged += (_, _) =>
        {
            CanGoBack = core.CanGoBack;
            CanGoForward = core.CanGoForward;
        };
        core.DocumentTitleChanged += (_, _) => Title = string.IsNullOrWhiteSpace(core.DocumentTitle)
            ? TitleFor(core.Source)
            : core.DocumentTitle;
        core.FaviconChanged += (_, _) => UpdateFavicon(core);
        core.NewWindowRequested += (_, e) => NewWindowRequested?.Invoke(this, e);
        core.WindowCloseRequested += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        core.ContainsFullScreenElementChanged += (_, _) => IsContentFullScreen = core.ContainsFullScreenElement;

        if (_pendingUrl is { } url)
        {
            _pendingUrl = null;
            core.Navigate(url);
        }
    }

    private static void ConfigureSettings(CoreWebView2Settings settings)
    {
        // Passwords are intentionally not stored until a proper password manager is integrated.
        settings.IsPasswordAutosaveEnabled = false;

        // No host objects: pages never get direct access to app code. Web messaging is on so
        // the new tab page can talk to the app, but OnWebMessage ignores every other origin.
        settings.AreHostObjectsAllowed = false;
        settings.IsWebMessageEnabled = true;

        settings.AreBrowserAcceleratorKeysEnabled = true;
        settings.IsZoomControlEnabled = true;
    }

    private void OnSourceChanged(CoreWebView2 core)
    {
        Url = core.Source;
        if (!_addressDirty)
            SetAddressFromPage(core.Source);
        OnPropertyChanged(nameof(IsNewTabPage));
    }

    private void OnWebMessage(CoreWebView2WebMessageReceivedEventArgs e)
    {
        // e.Source is the top-level document URL that posted the message.
        if (!InternalPages.IsInternal(e.Source))
            return;

        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                InternalPageMessage?.Invoke(this, document.RootElement.Clone());
        }
        catch (JsonException ex)
        {
            Log.Error("Dahili sayfadan geçersiz mesaj geldi.", ex);
        }
    }

    private void SetAddressFromPage(string? url)
    {
        _addressText = InternalPages.ToDisplayAddress(url);
        OnPropertyChanged(nameof(AddressText));
    }

    private async void UpdateFavicon(CoreWebView2 core)
    {
        if (string.IsNullOrEmpty(core.FaviconUri))
        {
            Favicon = null;
            return;
        }

        try
        {
            using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
            if (_disposed || stream is null)
            {
                Favicon = null;
                return;
            }

            // The WebView2 stream wraps a COM IStream; decode from a plain copy so the
            // bitmap is fully loaded (and freezable) before EndInit returns.
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            if (buffer.Length == 0)
            {
                Favicon = null;
                return;
            }
            buffer.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 32;
            bitmap.StreamSource = buffer;
            bitmap.EndInit();
            bitmap.Freeze();
            Favicon = bitmap;
        }
        catch (Exception ex) when (ex is COMException or NotSupportedException or FileFormatException or InvalidOperationException)
        {
            // A broken favicon is cosmetic: fall back to the default icon.
            Log.Info($"Favicon okunamadı ({core.FaviconUri}): {ex.Message}");
            Favicon = null;
        }
    }

    private static string TitleFor(string? url)
    {
        if (url is null || InternalPages.IsNewTab(url))
            return NewTabTitle;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
            ? uri.Host
            : url;
    }

    private void UpdateSecurity()
        => Security = _certificateError ? SecurityState.CertificateError : SecurityFor(Url);

    private static SecurityState SecurityFor(string? url)
    {
        if (url is null || InternalPages.IsInternal(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return SecurityState.Internal;

        return uri.Scheme switch
        {
            "https" => SecurityState.Secure,
            "http" when uri.IsLoopback => SecurityState.Local,
            "http" => SecurityState.NotSecure,
            _ => SecurityState.Local,
        };
    }
}
