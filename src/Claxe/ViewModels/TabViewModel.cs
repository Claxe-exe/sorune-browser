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

public enum SplitSide
{
    None,
    Left,
    Right,
}

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
    private static string NewTabTitle => L.T("Yeni Sekme");

    // Enums go to pages as camelCase strings ("completed"), the same as in the JSON files.
    private static readonly JsonSerializerOptions PageJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

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
    private bool _isSuspended;
    private bool _isContentFullScreen;
    private SecurityState _security;

    private CoreWebView2Environment? _environment;
    private bool _isPinned;
    private bool _isPlayingAudio;
    private bool _isMuted;
    private string? _zoomPercent;
    private int _findMatchCount;
    private int _findActiveIndex = -1;

    public TabViewModel(string? initialUrl, string? title = null, bool isPrivate = false, bool isPinned = false)
    {
        IsPrivate = isPrivate;
        _isPinned = isPinned;
        _pendingUrl = initialUrl;
        _url = initialUrl;
        _title = string.IsNullOrWhiteSpace(title) ? TitleFor(initialUrl) : title;
        _addressText = InternalPages.ToDisplayAddress(initialUrl);
        _security = SecurityFor(initialUrl);
    }

    public event EventHandler<CoreWebView2NewWindowRequestedEventArgs>? NewWindowRequested;

    /// <summary>The user clicked or tabbed into this page (picks the active half of a split screen).</summary>
    public event EventHandler? ContentFocused;
    public event EventHandler? CloseRequested;

    /// <summary>A message posted by one of Claxe's own pages (never by a website).</summary>
    public event EventHandler<JsonElement>? InternalPageMessage;

    /// <summary>Raised when a top-level navigation ends; the argument tells whether it succeeded.</summary>
    public event EventHandler<bool>? NavigationFinished;

    /// <summary>The page asks for camera, microphone, location, ... (WebView2's own prompt is replaced).</summary>
    public event EventHandler<CoreWebView2PermissionRequestedEventArgs>? PermissionRequested;

    /// <summary>The CoreWebView2 exists; app-wide services (downloads) attach here.</summary>
    public event EventHandler<CoreWebView2>? CoreCreated;

    /// <summary>Right click on the page; the view replaces WebView2's menu with a themed one.</summary>
    public event EventHandler<CoreWebView2ContextMenuRequestedEventArgs>? PageContextMenuRequested;

    /// <summary>InPrivate: separate throw-away profile, no history, not restored next time.</summary>
    public bool IsPrivate { get; }

    public bool IsPinned
    {
        get => _isPinned;
        set => SetProperty(ref _isPinned, value);
    }

    private int _volume = 100;
    private string? _volumeScriptId;
    private readonly List<CoreWebView2Frame> _frames = [];
    private bool _isShown;
    private SplitSide _splitSide;
    private bool _hasPictureInPictureVideo;

    /// <summary>This tab's own volume, 0..100 (independent of other tabs; mute is separate).</summary>
    public int Volume
    {
        get => _volume;
        set
        {
            if (!SetProperty(ref _volume, Math.Clamp(value, 0, 100)))
                return;
            OnPropertyChanged(nameof(HasCustomVolume));
            _ = ApplyVolumeAsync();
        }
    }

    public bool HasCustomVolume => _volume != 100;

    /// <summary>Visible in the content area: the active tab, or the other half of a split screen.</summary>
    public bool IsShown
    {
        get => _isShown;
        set
        {
            if (!SetProperty(ref _isShown, value))
                return;
            if (value)
            {
                IsSuspended = false;
                EnsureStarted();
            }
            if (Core is { } core)
                core.MemoryUsageTargetLevel = value || IsActive ? CoreWebView2MemoryUsageTargetLevel.Normal : CoreWebView2MemoryUsageTargetLevel.Low;
        }
    }

    /// <summary>Which half of the split screen shows this tab (None when not split).</summary>
    public SplitSide SplitSide
    {
        get => _splitSide;
        set
        {
            if (SetProperty(ref _splitSide, value))
            {
                OnPropertyChanged(nameof(IsInSplit));
                OnPropertyChanged(nameof(ContentColumn));
                OnPropertyChanged(nameof(ContentColumnSpan));
            }
        }
    }

    public bool IsInSplit => _splitSide != SplitSide.None;

    /// <summary>Column in the content grid: [left pane][divider][right pane].</summary>
    public int ContentColumn => _splitSide == SplitSide.Right ? 2 : 0;

    public int ContentColumnSpan => _splitSide == SplitSide.None ? 3 : 1;

    /// <summary>The page has a video that the browser can show in picture-in-picture.</summary>
    public bool HasPictureInPictureVideo
    {
        get => _hasPictureInPictureVideo;
        private set => SetProperty(ref _hasPictureInPictureVideo, value);
    }

    /// <summary>Re-checks the page for a picture-in-picture capable video (main frame only).</summary>
    public async Task RefreshPictureInPictureAsync()
    {
        if (Core is not { } core || IsSuspended || InternalPages.IsInternal(Url))
        {
            HasPictureInPictureVideo = false;
            return;
        }

        try
        {
            var result = await core.ExecuteScriptAsync(PictureInPicture.DetectScript);
            HasPictureInPictureVideo = result == "true";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            HasPictureInPictureVideo = false;
        }
    }

    /// <summary>Opens (or closes) the page's main video in Chromium's picture-in-picture window.</summary>
    public async Task<string?> TogglePictureInPictureAsync()
    {
        if (Core is not { } core)
            return null;

        try
        {
            // Runtime.evaluate with userGesture: the PiP API only works after a user action, and
            // this call stands in for the click the user made on Sorune's button.
            var json = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
            {
                expression = PictureInPicture.ToggleScript,
                userGesture = true,
                awaitPromise = true,
                returnByValue = true,
            }));
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("result", out var result) && result.TryGetProperty("value", out var value)
                ? value.GetString()
                : null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or JsonException)
        {
            Log.Info($"Resim içinde resim açılamadı: {ex.Message}");
            return "error";
        }
    }

    private async Task ApplyVolumeAsync()
    {
        if (Core is not { } core)
            return; // applied when the page is created

        try
        {
            if (_volumeScriptId is not null)
                core.RemoveScriptToExecuteOnDocumentCreated(_volumeScriptId);
            _volumeScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(TabAudio.Script(_volume));

            var call = TabAudio.SetCall(_volume);
            await core.ExecuteScriptAsync(call);
            foreach (var frame in _frames.ToList())
            {
                try
                {
                    await frame.ExecuteScriptAsync(call);
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException)
                {
                    _frames.Remove(frame); // gone meanwhile
                }
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            Log.Info($"Sekme sesi ayarlanamadı: {ex.Message}");
        }
    }

    private TabGroupViewModel? _group;
    private bool _isGroupStart;
    private bool _isCollapsedInGroup;

    /// <summary>The tab group this tab belongs to (members always sit next to each other).</summary>
    public TabGroupViewModel? Group
    {
        get => _group;
        set => SetProperty(ref _group, value);
    }

    /// <summary>First tab of its group: draws the group's label in front of itself.</summary>
    public bool IsGroupStart
    {
        get => _isGroupStart;
        set => SetProperty(ref _isGroupStart, value);
    }

    /// <summary>Its group is collapsed: only the label (on the first tab) stays visible.</summary>
    public bool IsCollapsedInGroup
    {
        get => _isCollapsedInGroup;
        set => SetProperty(ref _isCollapsedInGroup, value);
    }

    public bool IsPlayingAudio
    {
        get => _isPlayingAudio;
        private set => SetProperty(ref _isPlayingAudio, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set => SetProperty(ref _isMuted, value);
    }

    /// <summary>"%125" when the page is zoomed, null at 100 %.</summary>
    public string? ZoomPercent
    {
        get => _zoomPercent;
        private set => SetProperty(ref _zoomPercent, value);
    }

    public int FindMatchCount
    {
        get => _findMatchCount;
        private set => SetProperty(ref _findMatchCount, value);
    }

    /// <summary>0-based index of the highlighted match, -1 when none.</summary>
    public int FindActiveIndex
    {
        get => _findActiveIndex;
        private set => SetProperty(ref _findActiveIndex, value);
    }

    public void ToggleMute()
    {
        if (Core is { } core)
            core.IsMuted = !core.IsMuted;
    }

    public void ResetZoom()
    {
        if (_webView is not null)
            _webView.ZoomFactor = 1;
    }

    /// <summary>Highlights every match of <paramref name="term"/> (empty clears the search).</summary>
    public async Task FindAsync(string term)
    {
        if (Core is not { } core || _environment is null)
            return;

        if (string.IsNullOrEmpty(term))
        {
            StopFind();
            return;
        }

        var options = _environment.CreateFindOptions();
        options.FindTerm = term;
        options.SuppressDefaultFindDialog = true;
        options.ShouldHighlightAllMatches = true;
        try
        {
            await core.Find.StartAsync(options);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Log.Info($"Sayfada bul başarısız: {ex.Message}");
        }
    }

    public void FindNext() => Core?.Find.FindNext();

    public void FindPrevious() => Core?.Find.FindPrevious();

    public void StopFind()
    {
        Core?.Find.Stop();
        FindMatchCount = 0;
        FindActiveIndex = -1;
    }

    /// <summary>Null until the tab is first shown.</summary>
    public WebView2? WebView
    {
        get => _webView;
        private set => SetProperty(ref _webView, value);
    }

    /// <summary>Null before creation and after the browser process died.</summary>
    public CoreWebView2? Core
    {
        get
        {
            try
            {
                return _webView?.CoreWebView2;
            }
            catch (InvalidOperationException)
            {
                // The WPF control throws once its browser process has crashed; ProcessFailed recovers.
                return null;
            }
        }
    }

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
        set
        {
            if (!SetProperty(ref _isActive, value))
                return;

            if (value)
                IsSuspended = false; // WebView2 resumes by itself when it becomes visible
            else
                LastActiveAt = DateTime.UtcNow;

            // Background tabs ask the browser to trim memory where it can.
            if (Core is { } core)
                core.MemoryUsageTargetLevel = value || IsShown ? CoreWebView2MemoryUsageTargetLevel.Normal : CoreWebView2MemoryUsageTargetLevel.Low;
        }
    }

    /// <summary>When the tab was last left (UTC); drives background suspension.</summary>
    public DateTime LastActiveAt { get; private set; } = DateTime.UtcNow;

    public bool IsSuspended
    {
        get => _isSuspended;
        private set => SetProperty(ref _isSuspended, value);
    }

    /// <summary>
    /// Freezes a background tab's scripts and timers to save CPU and memory. Pages playing
    /// audio are left alone, and WebView2 itself refuses pages that can't be suspended.
    /// </summary>
    public async Task TrySuspendAsync()
    {
        if (IsActive || IsShown || IsSuspended || Core is not { } core || core.IsDocumentPlayingAudio || InternalPages.IsInternal(Url))
            return;

        try
        {
            IsSuspended = await core.TrySuspendAsync();
            if (IsSuspended)
                Log.Info($"Sekme uyutuldu: {Url}");
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Log.Info($"Sekme uyutulamadı ({Url}): {ex.Message}");
        }
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
            Title = L.T("Sekme başlatılamadı");
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

    /// <summary>Follows a theme change, so blank/loading pages match the new background.</summary>
    public void UpdateBackgroundColor()
    {
        if (_webView is null)
            return;

        var background = ThemeService.BaseColor;
        _webView.DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, background.R, background.G, background.B);
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
        _environment = environment;
        if (IsPrivate)
        {
            // All InPrivate tabs share one throw-away profile; WebView2 deletes its data when the last one closes.
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            options.ProfileName = "inprivate";
            await webView.EnsureCoreWebView2Async(environment, options);
        }
        else
        {
            await webView.EnsureCoreWebView2Async(environment);
        }

        if (_disposed)
            return;

        var core = webView.CoreWebView2;
        ConfigureSettings(core.Settings);
        InternalPages.Register(core);
        await InternalPages.InjectLanguageAsync(core);
        _volumeScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(TabAudio.Script(_volume));
        if (_disposed)
            return;

        core.FrameCreated += (_, e) =>
        {
            var frame = e.Frame;
            _frames.Add(frame);
            frame.Destroyed += (_, _) => _frames.Remove(frame);
        };

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
            if (!e.IsSuccess)
                ShowErrorPage(core, e.WebErrorStatus);
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
        core.PermissionRequested += (_, e) => PermissionRequested?.Invoke(this, e);
        core.ProcessFailed += (_, e) => OnProcessFailed(environment, e);
        if (!IsActive && !IsShown)
            core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
        core.IsDocumentPlayingAudioChanged += (_, _) => IsPlayingAudio = core.IsDocumentPlayingAudio;
        core.IsMutedChanged += (_, _) => IsMuted = core.IsMuted;
        core.Find.MatchCountChanged += (_, _) => FindMatchCount = core.Find.MatchCount;
        core.Find.ActiveMatchIndexChanged += (_, _) => FindActiveIndex = core.Find.ActiveMatchIndex;
        core.ContextMenuRequested += (_, e) => PageContextMenuRequested?.Invoke(this, e);
        webView.GotFocus += (_, _) => ContentFocused?.Invoke(this, EventArgs.Empty);
        webView.ZoomFactorChanged += (_, _) =>
            ZoomPercent = Math.Abs(webView.ZoomFactor - 1) < 0.001 ? null : $"%{Math.Round(webView.ZoomFactor * 100)}";
        CoreCreated?.Invoke(this, core);

        // Before the very first page of the run loads, so logins come back with it.
        // Never into an InPrivate profile: that would also hide them from normal tabs.
        if (!IsPrivate)
            SessionCookieStore.RestoreOnce(core);

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

    /// <summary>
    /// Keeps tabs usable when WebView2 processes fail: a crashed or hung page is reloaded;
    /// if the whole browser process exited, the tab drops its dead control and builds a
    /// new one on a fresh browser (right away if visible, otherwise when next shown).
    /// </summary>
    private void OnProcessFailed(CoreWebView2Environment environment, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Error($"WebView2 süreci sorunu: {e.ProcessFailedKind} ({e.Reason}, çıkış kodu {e.ExitCode}) — {Url}");
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                Core?.Reload();
                break;

            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                BrowserEnvironment.Reset(environment);
                // Not from inside the dead control's own event handler.
                System.Windows.Application.Current.Dispatcher.BeginInvoke(Recover);
                break;
        }
    }

    private void Recover()
    {
        if (_disposed)
            return;

        var dead = _webView;
        _initTask = null;
        _volumeScriptId = null;
        _frames.Clear();
        _pendingUrl = Url ?? InternalPages.NewTabUrl;
        WebView = null;
        IsLoading = false;
        IsSuspended = false;
        try
        {
            dead?.Dispose();
        }
        catch (InvalidOperationException ex)
        {
            Log.Info($"Çöken WebView bırakılırken: {ex.Message}");
        }

        if (IsActive || IsShown)
            EnsureStarted();
    }

    private async void ShowErrorPage(CoreWebView2 core, CoreWebView2WebErrorStatus status)
    {
        if (ErrorPage.Describe(status) is not { } text)
            return;

        try
        {
            await core.ExecuteScriptAsync(ErrorPage.BuildScript(Url, text.Title, text.Detail));
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            // WebView2's own error page simply stays.
            Log.Info($"Özel hata sayfası gösterilemedi ({status}): {ex.Message}");
        }
    }

    private void OnWebMessage(CoreWebView2WebMessageReceivedEventArgs e)
    {
        // e.Source is the top-level document URL that posted the message.
        if (!InternalPages.IsInternal(e.Source))
            return;

        try
        {
            var node = System.Text.Json.Nodes.JsonNode.Parse(e.WebMessageAsJson);
            if (node is not System.Text.Json.Nodes.JsonObject message)
                return;

            // Files dropped on the page arrive as objects next to the message; their real paths
            // are added here (never trusted from the page's own JSON).
            message.Remove("droppedPaths");
            if (e.AdditionalObjects is { Count: > 0 } objects)
            {
                var paths = new System.Text.Json.Nodes.JsonArray();
                foreach (var file in objects.OfType<CoreWebView2File>())
                    paths.Add(file.Path);
                message["droppedPaths"] = paths;
            }

            InternalPageMessage?.Invoke(this, JsonSerializer.SerializeToElement(message));
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
