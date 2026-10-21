using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Claxe.Core;
using Claxe.Models;
using Claxe.Services;
using Microsoft.Web.WebView2.Core;

namespace Claxe.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxClosedTabs = 25;
    private static readonly TimeSpan SuspendAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SuspendAfterInPerformanceMode = TimeSpan.FromMinutes(3);

    private static readonly HashSet<string> AppearanceKeys =
        ["theme", "wallpaper", "accentColor", "chromeOpacity", "cornerStyle", "density", "animationsEnabled", "performanceMode"];

    private readonly AppSettings _settings;
    private readonly SettingsController _settingsController;
    private readonly ShortcutStore _shortcuts = new();
    private readonly HistoryStore _history = new();
    private readonly BookmarkStore _bookmarks = new();
    private readonly DownloadManager _downloads = new();
    private readonly PermissionStore _permissions = new();
    private readonly PanelStore _panelStore = new();
    private readonly Queue<PermissionRequestViewModel> _permissionQueue = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _suspendTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _downloadsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly List<string> _closedTabs = [];
    private TabViewModel? _activeTab;
    private PanelViewModel? _activePanel;
    private PermissionRequestViewModel? _pendingPermission;
    private int _selectedSuggestion = -1;
    private bool _isShuttingDown;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        _settingsController = new SettingsController(settings, _permissions, _panelStore);
        _settingsController.Changed += OnSettingChanged;
        Mascot = new MascotViewModel(settings.MascotEnabled);

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveSession();
        };
        _suspendTimer.Tick += (_, _) => SuspendIdleTabs();
        _suspendTimer.Start();

        // Progress fires many times a second; the toolbar and pages refresh at most 4x per second.
        _downloadsTimer.Tick += (_, _) =>
        {
            _downloadsTimer.Stop();
            RaiseDownloadState();
        };
        _downloads.Changed += (_, _) =>
        {
            if (!_downloadsTimer.IsEnabled)
                _downloadsTimer.Start();
        };
        _downloads.Completed += (_, _) =>
        {
            SoundService.Play(UiSound.Toggle);
            Mascot.OnPageLoaded(success: true);
        };
        _bookmarks.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(IsActiveTabBookmarked));
            BroadcastToInternalPages(new { type = "bookmarks", items = _bookmarks.Items });
        };

        foreach (var panel in _panelStore.Items)
            Panels.Add(new PanelViewModel(panel));

        UpdateService.StateChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => BroadcastToInternalPages(_settingsController.BuildState()));

        NewTabCommand = new RelayCommand(NewTab);
        CloseTabCommand = new RelayCommand(p => CloseTab(p as TabViewModel ?? ActiveTab));
        ActivateTabCommand = new RelayCommand(p =>
        {
            if (p is TabViewModel tab)
                ActiveTab = tab;
        });
        BackCommand = new RelayCommand(() => ActiveTab?.GoBack(), () => ActiveTab?.CanGoBack == true);
        ForwardCommand = new RelayCommand(() => ActiveTab?.GoForward(), () => ActiveTab?.CanGoForward == true);
        ReloadOrStopCommand = new RelayCommand(ReloadOrStop, () => ActiveTab?.Core is not null);
        NavigateCommand = new RelayCommand(NavigateFromAddressBar);
        OpenSettingsCommand = new RelayCommand(() => OpenSettings());
        RevertAddressCommand = new RelayCommand(() =>
        {
            ClearSuggestions();
            ActiveTab?.RevertAddress();
            FocusContentRequested?.Invoke(this, EventArgs.Empty);
        });
        ToggleBookmarkCommand = new RelayCommand(ToggleBookmark, () => CanBookmark);
        OpenDownloadsCommand = new RelayCommand(() => OpenInternal(InternalPages.DownloadsUrl));
        TogglePanelCommand = new RelayCommand(p => TogglePanel(p as PanelViewModel));
        ClosePanelCommand = new RelayCommand(() => TogglePanel(null));
        ReloadPanelCommand = new RelayCommand(() => ActivePanel?.Tab?.Reload());
        PanelToTabCommand = new RelayCommand(PanelToTab);
        UnloadPanelCommand = new RelayCommand(p => UnloadPanel(p as PanelViewModel));
        RemovePanelCommand = new RelayCommand(p => RemovePanel((p as PanelViewModel)?.Id));
        ManagePanelsCommand = new RelayCommand(() => OpenSettings("kenar"));
    }

    /// <summary>Tabs in strip order (reorderable).</summary>
    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    /// <summary>
    /// The same tabs in creation order, only ever added to or removed from. The view
    /// hosts WebView2 controls from this list, so reordering tabs never re-parents
    /// a live browser window.
    /// </summary>
    public ObservableCollection<TabViewModel> HostedTabs { get; } = [];

    public ObservableCollection<PanelViewModel> Panels { get; } = [];

    public MascotViewModel Mascot { get; }

    /// <summary>Blurred wallpaper behind the tab strip and toolbar.</summary>
    public ImageSource? Backdrop => ThemeService.Backdrop;

    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand ActivateTabCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand ReloadOrStopCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand RevertAddressCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ToggleBookmarkCommand { get; }
    public ICommand OpenDownloadsCommand { get; }
    public ICommand TogglePanelCommand { get; }
    public ICommand ClosePanelCommand { get; }
    public ICommand ReloadPanelCommand { get; }
    public ICommand PanelToTabCommand { get; }
    public ICommand UnloadPanelCommand { get; }
    public ICommand RemovePanelCommand { get; }
    public ICommand ManagePanelsCommand { get; }

    public event EventHandler? FocusAddressBarRequested;
    public event EventHandler? FocusContentRequested;
    public event EventHandler? CloseWindowRequested;

    /// <summary>Layout or motion settings changed; the view re-fits sizes that aren't resource-bound.</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>The settings page asked for a wallpaper file; the view shows the file picker.</summary>
    public event EventHandler? WallpaperPickRequested;

    /// <summary>Asked before opening a downloaded program or script; the view shows the question.</summary>
    public Func<string, bool> ConfirmOpenRiskyFile { get; set; } = _ => false;

    public TabViewModel? ActiveTab
    {
        get => _activeTab;
        set
        {
            if (value is null || ReferenceEquals(value, _activeTab))
                return;

            if (_activeTab is not null)
                _activeTab.IsActive = false;

            _activeTab = value;
            value.IsActive = true;
            value.EnsureStarted();
            Mascot.NoteActivity();

            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(IsContentFullScreen));
            OnPropertyChanged(nameof(IsActiveTabLoading));
            OnPropertyChanged(nameof(IsActiveTabBookmarked));
            OnPropertyChanged(nameof(CanBookmark));
            CommandManager.InvalidateRequerySuggested();
            ScheduleSave();
        }
    }

    public string WindowTitle => ActiveTab is { } tab ? $"{tab.Title} - Claxe" : "Claxe";

    public bool IsContentFullScreen => ActiveTab?.IsContentFullScreen == true;

    public bool IsActiveTabLoading => ActiveTab?.IsLoading == true;

    public bool CanBookmark => ActiveTab?.Url is { } url && AddressResolver.IsNavigable(url) && !InternalPages.IsInternal(url);

    public bool IsActiveTabBookmarked => _bookmarks.Contains(ActiveTab?.Url);

    public SearchEngine SearchEngine => SearchEngines.Find(_settings.SearchEngine);

    // ---------------------------------------------------------------- sidebar

    public bool IsSidebarVisible => _settings.SidebarEnabled;

    public bool IsSidebarOnRight => _settings.SidebarPosition == "right";

    public double PanelWidth => _settings.PanelWidth;

    public PanelViewModel? ActivePanel
    {
        get => _activePanel;
        private set
        {
            if (ReferenceEquals(value, _activePanel))
                return;

            if (_activePanel is not null)
                _activePanel.IsOpen = false;
            _activePanel = value;
            if (value is not null)
                value.IsOpen = true;
            OnPropertyChanged();
        }
    }

    // ---------------------------------------------------------------- downloads

    public bool ShowDownloadsButton => _downloads.HasSessionDownloads;

    public int ActiveDownloadCount => _downloads.ActiveCount;

    /// <summary>0..1 for the toolbar button; null when nothing measurable is running.</summary>
    public double? DownloadProgress => _downloads.ActiveProgress;

    // ---------------------------------------------------------------- permissions

    /// <summary>The permission question currently on screen, if any.</summary>
    public PermissionRequestViewModel? PendingPermission
    {
        get => _pendingPermission;
        private set => SetProperty(ref _pendingPermission, value);
    }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>
    /// Called once the window is shown (WebView2 needs a live window handle).
    /// <paramref name="args"/> are links/files Claxe was started with (e.g. as default browser).
    /// </summary>
    public void Start(IReadOnlyList<string> args)
    {
        if (_settings.RestoreSession && SessionStore.Load() is { Tabs.Count: > 0 } session)
        {
            foreach (var saved in session.Tabs.Where(t => AddressResolver.IsNavigable(t.Url)))
                CreateTab(saved.Url, Tabs.Count, saved.Title, isPinned: saved.IsPinned);

            if (Tabs.Count > 0)
            {
                ActiveTab = Tabs[Math.Clamp(session.ActiveIndex, 0, Tabs.Count - 1)];
                Log.Info($"Oturum geri yüklendi: {Tabs.Count} sekme.");
            }
        }

        if (OpenExternal(args) == 0 && Tabs.Count == 0)
            NewTab();
    }

    /// <summary>Opens links or files handed over by Windows or a second Claxe launch. Returns how many opened.</summary>
    public int OpenExternal(IEnumerable<string> args)
    {
        var opened = 0;
        foreach (var arg in args)
        {
            string? url = null;
            if (AddressResolver.IsNavigable(arg) && !arg.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                url = new Uri(arg).AbsoluteUri;
            else if (File.Exists(arg))
                url = new Uri(Path.GetFullPath(arg)).AbsoluteUri;

            if (url is null)
                continue;

            ActiveTab = CreateTab(url, Tabs.Count);
            opened++;
        }

        return opened;
    }

    public void NewPrivateTab()
    {
        ActiveTab = CreateTab(InternalPages.NewTabUrl, Tabs.Count, isPrivate: true);
        SoundService.Play(UiSound.TabOpen);
        FocusAddressBarRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- address suggestions

    public ObservableCollection<SuggestionViewModel> Suggestions { get; } = [];

    /// <summary>Highlighted row (keyboard), -1 when none: Enter then uses what was typed.</summary>
    public int SelectedSuggestionIndex
    {
        get => _selectedSuggestion;
        set => SetProperty(ref _selectedSuggestion, Math.Clamp(value, -1, Suggestions.Count - 1));
    }

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>Local suggestions only (bookmarks + history): nothing typed is sent anywhere.</summary>
    public void UpdateSuggestions(string text)
    {
        Suggestions.Clear();
        _selectedSuggestion = -1;
        text = text.Trim();
        if (text.Length > 0)
        {
            var resolved = AddressResolver.Resolve(text, SearchEngine);
            var isSearch = resolved.StartsWith(SearchEngine.BuildUrl(""), StringComparison.Ordinal);
            Suggestions.Add(isSearch
                ? new SuggestionViewModel(text, $"{SearchEngine.Name} ile ara", resolved, "")
                : new SuggestionViewModel(resolved, "Siteye git", resolved, ""));

            var seen = new HashSet<string> { resolved };
            var compare = System.Globalization.CultureInfo.GetCultureInfo("tr-TR").CompareInfo;
            foreach (var bookmark in _bookmarks.Items
                         .Where(b => compare.IndexOf(b.Title, text, System.Globalization.CompareOptions.IgnoreCase) >= 0
                                     || compare.IndexOf(b.Url, text, System.Globalization.CompareOptions.IgnoreCase) >= 0)
                         .Take(3))
            {
                if (seen.Add(bookmark.Url))
                    Suggestions.Add(new SuggestionViewModel(bookmark.Title, bookmark.Url, bookmark.Url, ""));
            }

            foreach (var entry in _history.Suggest(text, 8))
            {
                if (Suggestions.Count >= 8)
                    break;
                if (seen.Add(entry.Url))
                    Suggestions.Add(new SuggestionViewModel(string.IsNullOrWhiteSpace(entry.Title) ? entry.Url : entry.Title, entry.Url, entry.Url, ""));
            }
        }

        OnPropertyChanged(nameof(SelectedSuggestionIndex));
        OnPropertyChanged(nameof(HasSuggestions));
    }

    public void ClearSuggestions() => UpdateSuggestions("");

    /// <summary>Arrow keys in the address bar; wraps through "nothing selected".</summary>
    public void MoveSuggestion(int delta)
    {
        if (Suggestions.Count == 0)
            return;

        var next = SelectedSuggestionIndex + delta;
        SelectedSuggestionIndex = next < -1 ? Suggestions.Count - 1 : next >= Suggestions.Count ? -1 : next;
    }

    public void OpenSuggestion(SuggestionViewModel suggestion)
    {
        ActiveTab?.Navigate(suggestion.Url);
        ClearSuggestions();
        FocusContentRequested?.Invoke(this, EventArgs.Empty);
    }

    // ---------------------------------------------------------------- tab context menu

    public ICommand DuplicateTabCommand => new RelayCommand(p =>
    {
        if (p is TabViewModel { Url: { } url } tab)
            ActiveTab = CreateTab(url, Tabs.IndexOf(tab) + 1, tab.Title, tab.IsPrivate);
    });

    public ICommand ReloadTabCommand => new RelayCommand(p => (p as TabViewModel)?.Reload());

    public ICommand ToggleMuteCommand => new RelayCommand(p => (p as TabViewModel)?.ToggleMute());

    public ICommand TogglePinCommand => new RelayCommand(p =>
    {
        if (p is not TabViewModel tab)
            return;

        // Pinned tabs live together at the start of the strip.
        var pinned = Tabs.Count(t => t.IsPinned);
        tab.IsPinned = !tab.IsPinned;
        Tabs.Move(Tabs.IndexOf(tab), tab.IsPinned ? pinned : pinned - 1);
        ScheduleSave();
    });

    public ICommand CloseOtherTabsCommand => new RelayCommand(p =>
    {
        if (p is TabViewModel keep)
            CloseTabs(Tabs.Where(t => !ReferenceEquals(t, keep) && !t.IsPinned).ToList(), keep);
    });

    public ICommand CloseTabsToRightCommand => new RelayCommand(p =>
    {
        if (p is TabViewModel from)
            CloseTabs(Tabs.Skip(Tabs.IndexOf(from) + 1).Where(t => !t.IsPinned).ToList(), from);
    });

    public ICommand NewPrivateTabCommand => new RelayCommand(NewPrivateTab);

    private void CloseTabs(List<TabViewModel> tabs, TabViewModel survivor)
    {
        ActiveTab = survivor;
        foreach (var tab in tabs)
            CloseTab(tab);
    }

    /// <summary>Right click on a page: the view shows Claxe's own menu built from WebView2's items.</summary>
    public event EventHandler<CoreWebView2ContextMenuRequestedEventArgs>? PageContextMenuRequested;

    /// <summary>
    /// Flushes everything and releases every WebView2. Async because the session cookies
    /// have to be read from the browser before it goes away.
    /// </summary>
    public async Task ShutdownAsync()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        _saveTimer.Stop();
        _suspendTimer.Stop();
        SaveSession();
        _history.Flush();

        PendingPermission?.Decide(false, remember: false);
        while (_permissionQueue.TryDequeue(out var queued))
            queued.Decide(false, remember: false);

        var core = HostedTabs.Where(t => !t.IsPrivate).Select(t => t.Core)
            .Concat(Panels.Select(p => p.Tab?.Core)).FirstOrDefault(c => c is not null);
        if (_settings.RestoreSession && core is not null)
        {
            try
            {
                var save = SessionCookieStore.SaveAsync(core);
                if (await Task.WhenAny(save, Task.Delay(TimeSpan.FromSeconds(3))) != save)
                    Log.Error("Oturum çerezleri zamanında kaydedilemedi.");
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                Log.Error("Oturum çerezleri okunamadı.", ex);
            }
        }

        foreach (var tab in HostedTabs.ToList())
        {
            Detach(tab);
            tab.Dispose();
        }
        foreach (var panel in Panels)
            panel.Dispose();

        HostedTabs.Clear();
        Tabs.Clear();
    }

    // ---------------------------------------------------------------- tabs

    public void NewTab()
    {
        ActiveTab = CreateTab(InternalPages.NewTabUrl, Tabs.Count);
        SoundService.Play(UiSound.TabOpen);
        FocusAddressBarRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Switches to the settings tab, opening one if needed; <paramref name="section"/> scrolls to it.</summary>
    public void OpenSettings(string? section = null)
        => OpenInternal(section is null ? InternalPages.SettingsUrl : InternalPages.SettingsUrl + "#" + section);

    public void OpenHistory() => OpenInternal(InternalPages.HistoryUrl);

    public void OpenBookmarks() => OpenInternal(InternalPages.BookmarksUrl);

    public void OpenDownloads() => OpenInternal(InternalPages.DownloadsUrl);

    public void CloseTab(TabViewModel? tab)
    {
        if (tab is null)
            return;

        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;

        if (tab.Url is { } url && !InternalPages.IsInternal(url) && !tab.IsPrivate)
        {
            _closedTabs.Add(url);
            if (_closedTabs.Count > MaxClosedTabs)
                _closedTabs.RemoveAt(0);
        }

        // Pick the neighbour before removing so the strip doesn't jump.
        if (ReferenceEquals(tab, ActiveTab) && Tabs.Count > 1)
            ActiveTab = Tabs[index + 1 < Tabs.Count ? index + 1 : index - 1];

        DenyPermissionsOf(tab);
        Tabs.Remove(tab);
        HostedTabs.Remove(tab);
        Detach(tab);
        tab.Dispose();
        SoundService.Play(UiSound.TabClose);

        if (Tabs.Count == 0)
        {
            _activeTab = null;
            CloseWindowRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        ScheduleSave();
    }

    public void ReopenClosedTab()
    {
        if (_closedTabs.Count == 0)
            return;

        var url = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);
        ActiveTab = CreateTab(url, Tabs.Count);
        SoundService.Play(UiSound.TabOpen);
    }

    public void SelectRelative(int delta)
    {
        if (ActiveTab is null || Tabs.Count < 2)
            return;

        var index = (Tabs.IndexOf(ActiveTab) + delta + Tabs.Count) % Tabs.Count;
        ActiveTab = Tabs[index];
    }

    /// <summary>Ctrl+1..8 select by position, Ctrl+9 always selects the last tab.</summary>
    public void SelectByNumber(int number)
    {
        if (Tabs.Count == 0)
            return;

        var index = number == 9 ? Tabs.Count - 1 : number - 1;
        if (index < Tabs.Count)
            ActiveTab = Tabs[index];
    }

    public void MoveTab(TabViewModel tab, int newIndex)
    {
        var oldIndex = Tabs.IndexOf(tab);
        // Pinned tabs move only among themselves, the others only after them.
        var pinned = Tabs.Count(t => t.IsPinned);
        newIndex = tab.IsPinned ? Math.Clamp(newIndex, 0, pinned - 1) : Math.Clamp(newIndex, pinned, Tabs.Count - 1);
        if (oldIndex < 0 || oldIndex == newIndex)
            return;

        Tabs.Move(oldIndex, newIndex);
        ScheduleSave();
    }

    public void Reload() => ActiveTab?.Reload();

    public void FocusAddressBar() => FocusAddressBarRequested?.Invoke(this, EventArgs.Empty);

    public void ToggleBookmark()
    {
        if (!CanBookmark || ActiveTab is not { Url: { } url } tab)
            return;

        _bookmarks.Toggle(url, tab.Title);
        SoundService.Play(UiSound.Toggle);
    }

    /// <summary>Called by the view after the user picked a file for the wallpaper.</summary>
    public void ImportWallpaper(string path)
    {
        if (!_settingsController.TryImportWallpaper(path, out var error))
            BroadcastToInternalPages(new { type = "wallpaperError", message = error });
    }

    /// <summary>The view reports the panel width after the splitter was dragged.</summary>
    public void SetPanelWidth(double width)
    {
        _settingsController.SetPanelWidth(width);
        OnPropertyChanged(nameof(PanelWidth));
    }

    private TabViewModel CreateTab(string? url, int index, string? title = null, bool isPrivate = false, bool isPinned = false)
    {
        var tab = CreatePage(url, title, isPrivate, isPinned);
        // New tabs never land inside the pinned group.
        if (!isPinned)
            index = Math.Max(index, Tabs.Count(t => t.IsPinned));
        tab.NewWindowRequested += OnNewWindowRequested;
        tab.InternalPageMessage += OnInternalPageMessage;
        tab.NavigationFinished += OnNavigationFinished;

        Tabs.Insert(Math.Clamp(index, 0, Tabs.Count), tab);
        HostedTabs.Add(tab);
        ScheduleSave();
        return tab;
    }

    /// <summary>Wiring shared by tabs and web panels.</summary>
    private TabViewModel CreatePage(string? url, string? title = null, bool isPrivate = false, bool isPinned = false)
    {
        var tab = new TabViewModel(url, title, isPrivate, isPinned);
        tab.PageContextMenuRequested += OnPageContextMenuRequested;
        tab.PropertyChanged += OnTabPropertyChanged;
        tab.CloseRequested += OnTabCloseRequested;
        tab.PermissionRequested += OnPermissionRequested;
        tab.CoreCreated += OnCoreCreated;
        return tab;
    }

    private void OnPageContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
        => PageContextMenuRequested?.Invoke(sender, e);

    private void Detach(TabViewModel tab)
    {
        tab.PageContextMenuRequested -= OnPageContextMenuRequested;
        tab.PropertyChanged -= OnTabPropertyChanged;
        tab.CloseRequested -= OnTabCloseRequested;
        tab.PermissionRequested -= OnPermissionRequested;
        tab.CoreCreated -= OnCoreCreated;
        tab.NewWindowRequested -= OnNewWindowRequested;
        tab.InternalPageMessage -= OnInternalPageMessage;
        tab.NavigationFinished -= OnNavigationFinished;
    }

    private void OpenInternal(string url)
    {
        // Reuse an open tab of the same page (settings, library) instead of piling them up.
        var page = url.Split('#')[0];
        if (Tabs.FirstOrDefault(t => t.Url?.Split('#')[0] == page) is { } existing)
        {
            ActiveTab = existing;
            if (existing.Url != url)
                existing.Navigate(url);
            return;
        }

        ActiveTab = CreateTab(url, Tabs.Count);
        SoundService.Play(UiSound.TabOpen);
    }

    private void OnCoreCreated(object? sender, CoreWebView2 core) => _downloads.Attach(core);

    private void OnNavigationFinished(object? sender, bool success)
    {
        if (sender is TabViewModel { Url: { } url, IsPrivate: false } tab && success && IsHistoryWorthy(url))
            _history.AddVisit(url, tab.Title);

        if (!ReferenceEquals(sender, ActiveTab))
            return;

        // Opening a new tab is not worth a celebration; real pages are.
        if (InternalPages.IsInternal(ActiveTab?.Url))
            Mascot.NoteActivity();
        else
            Mascot.OnPageLoaded(success);
    }

    private static bool IsHistoryWorthy(string url)
        => !InternalPages.IsInternal(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var tab = (TabViewModel)sender!;
        if (e.PropertyName is nameof(TabViewModel.Url) or nameof(TabViewModel.Title))
            ScheduleSave();
        if (e.PropertyName == nameof(TabViewModel.Title) && !tab.IsPrivate && tab.Url is { } url && IsHistoryWorthy(url) && Tabs.Contains(tab))
            _history.UpdateTitle(url, tab.Title);

        if (!ReferenceEquals(tab, ActiveTab))
            return;

        switch (e.PropertyName)
        {
            case nameof(TabViewModel.Title):
                OnPropertyChanged(nameof(WindowTitle));
                break;
            case nameof(TabViewModel.Url):
                OnPropertyChanged(nameof(IsActiveTabBookmarked));
                OnPropertyChanged(nameof(CanBookmark));
                CommandManager.InvalidateRequerySuggested();
                break;
            case nameof(TabViewModel.IsContentFullScreen):
                OnPropertyChanged(nameof(IsContentFullScreen));
                break;
            case nameof(TabViewModel.IsLoading):
                OnPropertyChanged(nameof(IsActiveTabLoading));
                CommandManager.InvalidateRequerySuggested();
                break;
            case nameof(TabViewModel.CanGoBack):
            case nameof(TabViewModel.CanGoForward):
            case nameof(TabViewModel.WebView):
                CommandManager.InvalidateRequerySuggested();
                break;
        }
    }

    /// <summary>
    /// window.open / target=_blank: hand the page a brand-new tab's CoreWebView2 so the
    /// opener relationship (needed by OAuth-style popups) is kept.
    /// </summary>
    private async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (sender is not TabViewModel source)
            return;

        var deferral = e.GetDeferral();
        try
        {
            // Popups from an InPrivate tab stay InPrivate (and share its profile).
            var tab = CreateTab(url: null, Tabs.IndexOf(source) + 1, isPrivate: source.IsPrivate);
            ActiveTab = tab;
            SoundService.Play(UiSound.TabOpen);
            await tab.EnsureCoreAsync();
            if (tab.Core is { } core)
            {
                e.NewWindow = core;
                e.Handled = true;
            }
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            // Not handled: WebView2 falls back to opening its own popup window.
            Log.Error($"Yeni pencere isteği sekmede açılamadı: {e.Uri}", ex);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is TabViewModel tab && Panels.FirstOrDefault(p => ReferenceEquals(p.Tab, tab)) is { } panel)
            UnloadPanel(panel);
        else
            CloseTab(sender as TabViewModel);
    }

    private void ReloadOrStop()
    {
        if (ActiveTab is not { } tab)
            return;

        if (tab.IsLoading)
            tab.Stop();
        else
            tab.Reload();
    }

    private void NavigateFromAddressBar()
    {
        if (SelectedSuggestionIndex >= 0 && SelectedSuggestionIndex < Suggestions.Count)
        {
            OpenSuggestion(Suggestions[SelectedSuggestionIndex]);
            return;
        }

        if (ActiveTab is not { } tab || string.IsNullOrWhiteSpace(tab.AddressText))
            return;

        tab.Navigate(AddressResolver.Resolve(tab.AddressText, SearchEngine));
        ClearSuggestions();
        FocusContentRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Background tabs left alone for a while are frozen (sooner in performance mode).</summary>
    private void SuspendIdleTabs()
    {
        var limit = _settings.PerformanceMode ? SuspendAfterInPerformanceMode : SuspendAfter;
        foreach (var tab in Tabs.Where(t => !t.IsActive && !t.IsSuspended && DateTime.UtcNow - t.LastActiveAt > limit).ToList())
            _ = tab.TrySuspendAsync();
    }

    // ---------------------------------------------------------------- web panels

    private void TogglePanel(PanelViewModel? panel)
    {
        if (panel is null || ReferenceEquals(panel, ActivePanel))
        {
            ActivePanel = null;
            return;
        }

        panel.EnsureTab(url =>
        {
            var tab = CreatePage(url);
            // Links that open a new window from a panel go to a normal tab.
            tab.NewWindowRequested += OnPanelNewWindowRequested;
            return tab;
        });
        ActivePanel = panel;
    }

    private void OnPanelNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!AddressResolver.IsNavigable(e.Uri))
            return;

        e.Handled = true;
        ActiveTab = CreateTab(e.Uri, Tabs.Count);
        SoundService.Play(UiSound.TabOpen);
    }

    private void PanelToTab()
    {
        if (ActivePanel?.Tab?.Url is { } url && AddressResolver.IsNavigable(url))
        {
            ActiveTab = CreateTab(url, Tabs.Count);
            SoundService.Play(UiSound.TabOpen);
        }
    }

    private void UnloadPanel(PanelViewModel? panel)
    {
        if (panel is null)
            return;

        if (ReferenceEquals(panel, ActivePanel))
            ActivePanel = null;

        if (panel.Unload() is { } tab)
        {
            DenyPermissionsOf(tab);
            tab.NewWindowRequested -= OnPanelNewWindowRequested;
            Detach(tab);
            tab.Dispose();
        }
    }

    private void RemovePanel(string? id)
    {
        if (Panels.FirstOrDefault(p => p.Id == id) is not { } panel || !_panelStore.Remove(id))
            return;

        UnloadPanel(panel);
        Panels.Remove(panel);
        BroadcastToInternalPages(_settingsController.BuildState());
    }

    // ---------------------------------------------------------------- permissions

    private void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        if (sender is not TabViewModel tab)
            return;

        var origin = PermissionStore.OriginOf(e.Uri);
        Log.Info($"İzin isteği: {origin} {e.PermissionKind} (kullanıcı başlattı: {e.IsUserInitiated})");
        if (_permissions.Find(origin, e.PermissionKind) is { } remembered)
        {
            e.State = remembered ? CoreWebView2PermissionState.Allow : CoreWebView2PermissionState.Deny;
            e.SavesInProfile = false;
            return;
        }

        var request = new PermissionRequestViewModel(tab, e, OnPermissionDecided);
        if (PendingPermission is null)
            PendingPermission = request;
        else
            _permissionQueue.Enqueue(request);
    }

    private void OnPermissionDecided(PermissionRequestViewModel request, bool allowed, bool remember)
    {
        Log.Info($"İzin kararı: {request.Origin} {request.Kind} -> {(allowed ? "izin" : "red")}{(remember ? " (hatırlanacak)" : "")}");
        if (remember)
        {
            _permissions.Remember(request.Origin, request.Kind, allowed);
            BroadcastToInternalPages(_settingsController.BuildState());
        }

        if (ReferenceEquals(request, PendingPermission))
            PendingPermission = _permissionQueue.TryDequeue(out var next) ? next : null;
    }

    /// <summary>Questions from a page that is going away are answered "no" (and not remembered).</summary>
    private void DenyPermissionsOf(TabViewModel tab)
    {
        foreach (var queued in _permissionQueue.Where(r => ReferenceEquals(r.Tab, tab)).ToList())
            queued.Decide(false, remember: false);

        var remaining = _permissionQueue.Where(r => !ReferenceEquals(r.Tab, tab)).ToList();
        _permissionQueue.Clear();
        foreach (var request in remaining)
            _permissionQueue.Enqueue(request);

        if (PendingPermission is { } pending && ReferenceEquals(pending.Tab, tab))
            pending.Decide(false, remember: false);
    }

    // ---------------------------------------------------------------- internal pages

    /// <summary>Requests from Claxe's own pages. The tab already verified the sender is internal.</summary>
    private void OnInternalPageMessage(object? sender, JsonElement message)
    {
        if (sender is not TabViewModel tab)
            return;

        string? Text(string name)
            => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        bool Flag(string name)
            => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        switch (Text("type"))
        {
            // ----- new tab page
            case "ready":
                tab.PostToInternalPage(BuildNewTabConfig());
                break;

            case "navigate" when !string.IsNullOrWhiteSpace(Text("text")):
                tab.Navigate(AddressResolver.Resolve(Text("text")!, SearchEngine));
                FocusContentRequested?.Invoke(this, EventArgs.Empty);
                break;

            case "addShortcut":
                if (!_shortcuts.Add(Text("title"), Text("url")))
                    tab.PostToInternalPage(new { type = "shortcutRejected" });
                BroadcastShortcuts();
                break;

            case "removeShortcut":
                _shortcuts.Remove(Text("url"));
                BroadcastShortcuts();
                break;

            // ----- settings page
            case "getSettings":
                tab.PostToInternalPage(_settingsController.BuildState());
                break;

            case "updateSetting" when message.TryGetProperty("value", out var value):
                if (!_settingsController.TryUpdate(Text("key"), value))
                    tab.PostToInternalPage(_settingsController.BuildState()); // put the page back in sync
                break;

            case "pickWallpaper":
                WallpaperPickRequested?.Invoke(this, EventArgs.Empty);
                break;

            case "removeCustomWallpaper":
                _settingsController.RemoveCustomWallpaper();
                break;

            case "forgetPermission":
                _permissions.Forget(Text("origin"),
                    Enum.TryParse<CoreWebView2PermissionKind>(Text("kind"), out var kind) ? kind : null);
                BroadcastToInternalPages(_settingsController.BuildState());
                break;

            case "clearPermissions":
                _permissions.Clear();
                BroadcastToInternalPages(_settingsController.BuildState());
                break;

            case "addPanel":
                if (_panelStore.Add(Text("name"), Text("url")) is { } added)
                {
                    Panels.Add(new PanelViewModel(added));
                    BroadcastToInternalPages(_settingsController.BuildState());
                }
                else
                {
                    tab.PostToInternalPage(new { type = "panelRejected" });
                }
                break;

            case "removePanel":
                RemovePanel(Text("id"));
                break;

            case "renamePanel" when Panels.FirstOrDefault(p => p.Id == Text("id")) is { } renamed:
                if (_panelStore.Rename(renamed.Id, Text("name")))
                    renamed.Name = _panelStore.Items.First(p => p.Id == renamed.Id).Name;
                BroadcastToInternalPages(_settingsController.BuildState());
                break;

            case "movePanel" when Panels.FirstOrDefault(p => p.Id == Text("id")) is { } moved
                                  && message.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Number:
                var newIndex = _panelStore.Move(moved.Id, delta.GetInt32());
                if (newIndex >= 0)
                    Panels.Move(Panels.IndexOf(moved), newIndex);
                BroadcastToInternalPages(_settingsController.BuildState());
                break;

            case "importSources":
                tab.PostToInternalPage(new
                {
                    type = "importSources",
                    items = BrowserImporter.Detect().Select(s => new { s.Id, s.Browser, s.Profile, s.HasBookmarks, s.HasHistory }),
                });
                break;

            case "importData":
                ImportFromBrowser(tab, Text("source"), Flag("bookmarks"), Flag("history"));
                break;

            case "registerDefaultBrowser":
                try
                {
                    DefaultBrowser.Register();
                    DefaultBrowser.OpenWindowsSettings();
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException
                                               or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Log.Error("Varsayılan tarayıcı kaydı başarısız.", ex);
                }
                BroadcastToInternalPages(_settingsController.BuildState());
                break;

            case "checkUpdates":
                _ = UpdateService.CheckAsync();
                break;

            case "applyUpdate":
                UpdateService.RestartToApply();
                break;

            case "unregisterDefaultBrowser":
                DefaultBrowser.Unregister();
                BroadcastToInternalPages(_settingsController.BuildState());
                break;

            case "clearData":
                ClearBrowsingData(tab, Text("range"), Flag("history"), Flag("cookies"), Flag("cache"), Flag("downloads"));
                break;

            // ----- library page
            case "history":
                var before = message.TryGetProperty("beforeId", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetInt64() : (long?)null;
                const int pageSize = 120;
                var items = _history.Query(Text("text"), before, pageSize);
                tab.PostToInternalPage(new { type = "history", items, append = before is not null, more = items.Count == pageSize });
                break;

            case "deleteHistory" when message.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array:
                _history.Delete(ids.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Number).Select(i => i.GetInt64()));
                tab.PostToInternalPage(new { type = "historyChanged" });
                break;

            case "bookmarks":
                tab.PostToInternalPage(new { type = "bookmarks", items = _bookmarks.Items });
                break;

            case "removeBookmark":
                _bookmarks.Remove(Text("id"));
                break;

            case "renameBookmark":
                _bookmarks.Rename(Text("id"), Text("title"));
                break;

            case "setBookmarkFolder":
                _bookmarks.SetFolder(Text("id"), Text("folder"));
                break;

            case "renameBookmarkFolder":
                _bookmarks.RenameFolder(Text("from"), Text("to"));
                break;

            case "downloads":
                tab.PostToInternalPage(BuildDownloadState());
                break;

            case "download":
                HandleDownloadAction(Text("action"), Text("id"));
                break;

            case "open" when Text("url") is { } url && AddressResolver.IsNavigable(url) && !InternalPages.IsInternal(url):
                if (Flag("newTab"))
                    ActiveTab = CreateTab(url, Tabs.Count);
                else
                    tab.Navigate(url);
                break;
        }
    }

    private void HandleDownloadAction(string? action, string? id)
    {
        switch (action)
        {
            case "open" when _downloads.GetFile(id) is { } file:
                if (!DownloadManager.IsRisky(file) || ConfirmOpenRiskyFile(file))
                {
                    try
                    {
                        DownloadManager.Open(file);
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        Log.Error($"Dosya açılamadı: {file}", ex);
                    }
                }
                break;
            case "showInFolder": _downloads.ShowInFolder(id); break;
            case "cancel": _downloads.Cancel(id); break;
            case "pause": _downloads.Pause(id); break;
            case "resume": _downloads.Resume(id); break;
            case "remove": _downloads.Remove(id); break;
            case "clear": _downloads.ClearFinished(); break;
        }
    }

    private async void ClearBrowsingData(TabViewModel requester, string? range, bool history, bool cookies, bool cache, bool downloads)
    {
        DateTime? since = range switch
        {
            "hour" => DateTime.UtcNow.AddHours(-1),
            "day" => DateTime.UtcNow.AddDays(-1),
            "week" => DateTime.UtcNow.AddDays(-7),
            _ => null,
        };

        if (history)
            _history.Clear(since);
        if (downloads)
            _downloads.ClearFinished(since);
        if (cookies)
            SessionCookieStore.Delete();

        var kinds = (CoreWebView2BrowsingDataKinds)0;
        if (cookies)
            kinds |= CoreWebView2BrowsingDataKinds.AllSite;
        if (cache)
            kinds |= CoreWebView2BrowsingDataKinds.DiskCache;
        if (history)
            kinds |= CoreWebView2BrowsingDataKinds.BrowsingHistory;
        if (downloads)
            kinds |= CoreWebView2BrowsingDataKinds.DownloadHistory;

        var ok = true;
        if (kinds != 0 && requester.Core?.Profile is { } profile)
        {
            try
            {
                if (since is { } start)
                    await profile.ClearBrowsingDataAsync(kinds, start.ToLocalTime(), DateTime.Now);
                else
                    await profile.ClearBrowsingDataAsync(kinds);
            }
            catch (Exception ex) when (ex is COMException or ArgumentException)
            {
                Log.Error("Tarama verileri temizlenemedi.", ex);
                ok = false;
            }
        }

        Log.Info($"Veriler temizlendi: aralık={range ?? "all"} geçmiş={history} çerez={cookies} önbellek={cache} indirme={downloads}");
        requester.PostToInternalPage(new { type = "dataCleared", ok });
    }

    /// <summary>Reads the other browser's files off the UI thread, then merges on it.</summary>
    private async void ImportFromBrowser(TabViewModel requester, string? sourceId, bool bookmarks, bool history)
    {
        const int historyLimit = 5000;
        try
        {
            var source = BrowserImporter.Find(sourceId) ?? throw new InvalidOperationException("Tarayıcı profili bulunamadı.");
            var (readBookmarks, readHistory) = await Task.Run(() => (
                bookmarks && source.HasBookmarks ? BrowserImporter.ReadBookmarks(source) : [],
                history && source.HasHistory ? BrowserImporter.ReadHistory(source, historyLimit) : []));

            var addedBookmarks = _bookmarks.Import(readBookmarks);
            var addedHistory = _history.Import(readHistory);
            Log.Info($"İçe aktarıldı ({source.Browser} / {source.Profile}): {addedBookmarks} yer imi, {addedHistory} geçmiş kaydı");
            requester.PostToInternalPage(new { type = "importResult", ok = true, bookmarks = addedBookmarks, history = addedHistory });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            Log.Error($"İçe aktarma başarısız: {sourceId}", ex);
            var message = ex is IOException or Microsoft.Data.Sqlite.SqliteException
                ? "Dosyalar okunamadı. O tarayıcıyı kapatıp tekrar dene."
                : ex.Message;
            requester.PostToInternalPage(new { type = "importResult", ok = false, error = message });
        }
    }

    private object BuildDownloadState() => new { type = "downloads", items = _downloads.Items };

    private void RaiseDownloadState()
    {
        OnPropertyChanged(nameof(ShowDownloadsButton));
        OnPropertyChanged(nameof(ActiveDownloadCount));
        OnPropertyChanged(nameof(DownloadProgress));
        BroadcastToInternalPages(BuildDownloadState());
    }

    private void BroadcastShortcuts() => BroadcastToInternalPages(new { type = "shortcuts", shortcuts = _shortcuts.Items });

    /// <summary>Only reaches tabs currently showing one of Claxe's own pages; each page ignores types it doesn't use.</summary>
    private void BroadcastToInternalPages(object payload)
    {
        foreach (var tab in HostedTabs)
            tab.PostToInternalPage(payload);
    }

    private void OnSettingChanged(object? sender, string key)
    {
        if (AppearanceKeys.Contains(key))
        {
            Motion.Enabled = _settings.AnimationsEnabled && !_settings.PerformanceMode;
            ThemeService.Apply(_settings);
            OnPropertyChanged(nameof(Backdrop));
            foreach (var tab in HostedTabs)
                tab.UpdateBackgroundColor();
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
            BroadcastToInternalPages(BuildNewTabConfig());
        }

        switch (key)
        {
            case "mascotEnabled":
                Mascot.IsEnabled = _settings.MascotEnabled;
                break;
            case "soundsEnabled":
            case "soundVolume":
                SoundService.Enabled = _settings.SoundsEnabled;
                SoundService.Volume = _settings.SoundVolume;
                break;
            case "searchEngine":
                BroadcastToInternalPages(BuildNewTabConfig());
                break;
            case "sidebarEnabled":
                if (!_settings.SidebarEnabled)
                    ActivePanel = null;
                OnPropertyChanged(nameof(IsSidebarVisible));
                break;
            case "sidebarPosition":
                OnPropertyChanged(nameof(IsSidebarOnRight));
                break;
            case "restoreSession":
                SessionCookieStore.Enabled = _settings.RestoreSession;
                if (_settings.RestoreSession)
                {
                    ScheduleSave();
                }
                else
                {
                    SessionStore.Delete();
                    SessionCookieStore.Delete();
                }
                break;
        }

        // Feedback: a toggle sound for switches, a sample click at the new volume for the slider.
        if (key is "animationsEnabled" or "mascotEnabled" or "soundsEnabled" or "restoreSession" or "sidebarEnabled" or "performanceMode")
            SoundService.Play(UiSound.Toggle);
        else if (key == "soundVolume")
            SoundService.Play(UiSound.Click);

        BroadcastToInternalPages(_settingsController.BuildState());
    }

    private object BuildNewTabConfig()
    {
        var theme = ThemeService.Current;
        return new
        {
            type = "config",
            theme = new
            {
                id = theme.Id,
                name = theme.Name,
                wallpaper = ThemeService.WallpaperUrl,
                accent = ThemeService.Accent,
                accentSecondary = theme.Colors.AccentSecondary,
                overlay = theme.NewTab.Overlay,
                particles = theme.NewTab.Particles,
            },
            animations = Motion.Enabled,
            searchEngine = SearchEngine.Name,
            shortcuts = _shortcuts.Items,
            maxShortcuts = ShortcutStore.MaxShortcuts,
        };
    }

    // ---------------------------------------------------------------- session

    private void ScheduleSave()
    {
        if (_isShuttingDown || !_settings.RestoreSession)
            return;

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveSession()
    {
        if (!_settings.RestoreSession)
            return;

        // InPrivate tabs are never written to disk.
        var saved = Tabs.Where(t => !t.IsPrivate).ToList();
        var state = new SessionState
        {
            Tabs = saved.Select(t => new SessionTab { Url = t.Url ?? InternalPages.NewTabUrl, Title = t.Title, IsPinned = t.IsPinned }).ToList(),
            ActiveIndex = ActiveTab is null ? 0 : Math.Max(0, saved.IndexOf(ActiveTab)),
        };
        SessionStore.Save(state);
    }
}
