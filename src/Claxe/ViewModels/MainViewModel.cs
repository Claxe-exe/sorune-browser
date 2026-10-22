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

    private static readonly HashSet<string> AppearanceKeys =
        ["theme", "wallpaper", "accentColor", "chromeOpacity", "cornerStyle", "density", "animationsEnabled", "performanceMode",
         "siteTintEnabled", "pageFrameEnabled", "userTheme"];

    private readonly AppSettings _settings;
    private readonly SettingsController _settingsController;
    private readonly ShortcutStore _shortcuts = new();
    private readonly HistoryStore _history = new();
    private readonly BookmarkStore _bookmarks = new();
    private readonly DownloadManager _downloads = new();
    private readonly PermissionStore _permissions = new();
    private readonly PanelStore _panelStore = new();
    private readonly ConverterController _converter;
    private readonly WidgetStore _widgets = new();
    private readonly ExtensionController _extensions;
    private bool _extensionsLoaded;
    private readonly Queue<PermissionRequestViewModel> _permissionQueue = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _suspendTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _downloadsTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly TimeMachineStore _timeMachine = new();
    private readonly TaskManagerController _taskManager;
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
        _settingsController.DownloadFolderInUse = () => _downloads.Folder;
        // A folder from a backup made on another computer may not exist here.
        _downloads.SetFolder(Directory.Exists(settings.DownloadFolder) ? settings.DownloadFolder : null);
        Mascot = new MascotViewModel(settings.MascotEnabled)
        {
            TalksEnabled = settings.MascotTalks,
            Peek = settings.MascotPeek,
            CustomImage = _settingsController.CustomMascotPath,
        };

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveSession();
        };
        _suspendTimer.Tick += (_, _) => SuspendIdleTabs();
        _suspendTimer.Start();
        // Only the active tab is asked whether it has a video for picture-in-picture.
        _pipTimer.Tick += (_, _) =>
        {
            _ = ActiveTab?.RefreshPictureInPictureAsync();
            if (_settings.SiteTintEnabled)
                _ = ActiveTab?.RefreshSiteColorAsync();
        };
        _pipTimer.Start();

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
        _downloads.Completed += (_, record) =>
        {
            SoundService.Play(UiSound.Toggle);
            Mascot.Say(L.T("İndirme bitti: {0}", record.FileName), happy: true);
        };
        _taskManager = new TaskManagerController(() => HostedTabs, () => HostedPanels);
        _timeMachine.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(CanReopenClosedTab));
            BroadcastToInternalPages(BuildTimeMachineState());
        };
        _bookmarks.Changed += (_, _) =>
        {
            OnPropertyChanged(nameof(IsActiveTabBookmarked));
            BroadcastToInternalPages(new { type = "bookmarks", items = _bookmarks.Items });
        };

        foreach (var panel in _panelStore.Items)
            AddPanel(new PanelViewModel(panel));

        _converter = new ConverterController(() => ActiveTab, url =>
        {
            ActiveTab = CreateTab(url, Tabs.Count);
            SoundService.Play(UiSound.TabOpen);
        });
        ConverterPanel = new PanelViewModel(new WebPanel { Id = "sorune-converter", Name = L.T("Dönüştürücü"), Url = InternalPages.ConverterUrl });
        HostedPanels.Add(ConverterPanel);
        ToolsPanel = new PanelViewModel(new WebPanel { Id = "sorune-tools", Name = L.T("Mini araçlar"), Url = InternalPages.ToolsUrl });
        HostedPanels.Add(ToolsPanel);

        _extensions = new ExtensionController(
            () => PickExtensionFolder?.Invoke(),
            () => PickExtensionArchive?.Invoke(),
            url =>
            {
                ActiveTab = CreateTab(url, Tabs.Count);
                SoundService.Play(UiSound.TabOpen);
            },
            RefreshExtensionPanelsAsync);

        UpdateService.StateChanged += (_, _) =>
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                OnPropertyChanged(nameof(IsUpdateReady));
                if (IsUpdateReady)
                    Mascot.Say(L.T("Yeni sürüm hazır ✨ Güncelle'ye bas"), happy: true);
                BroadcastToInternalPages(_settingsController.BuildState());
            });
        ApplyUpdateCommand = new RelayCommand(ApplyUpdate);

        Tabs.CollectionChanged += (_, _) => RefreshGroups();

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

    /// <summary>The user's web panels, in sidebar order.</summary>
    public ObservableCollection<PanelViewModel> Panels { get; } = [];

    /// <summary>Every panel whose page may be alive: the web panels plus the built-in converter.</summary>
    public ObservableCollection<PanelViewModel> HostedPanels { get; } = [];

    /// <summary>Built-in file converter, pinned to the bottom of the sidebar.</summary>
    public PanelViewModel ConverterPanel { get; }

    /// <summary>Built-in mini tools (JSON, colours, QR, timers, counters, URL encoding).</summary>
    public PanelViewModel ToolsPanel { get; }

    /// <summary>Enabled extensions that have a popup: they open in the sidebar panel.</summary>
    public ObservableCollection<PanelViewModel> ExtensionPanels { get; } = [];

    /// <summary>Picker for the user's own mascot image (shown by the view).</summary>
    public Func<string?>? PickMascotFile { get; set; }

    /// <summary>Folder / archive pickers for installing extensions (shown by the view).</summary>
    public Func<string?>? PickExtensionFolder { get; set; }
    public Func<string?>? PickExtensionArchive { get; set; }

    /// <summary>Asks where to save a backup (suggested file name in, chosen path out).</summary>
    public Func<string, string?>? PickBackupTarget { get; set; }

    public Func<string?>? PickBackupFile { get; set; }

    /// <summary>Keeps the sidebar's extension buttons in line with what is installed and enabled.</summary>
    private async Task RefreshExtensionPanelsAsync(CoreWebView2Profile profile)
    {
        List<ExtensionInfo> list;
        try
        {
            list = await ExtensionService.ListAsync(profile);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Log.Info($"Eklentiler okunamadı: {ex.Message}");
            return;
        }

        var wanted = list.Where(e => e.IsEnabled && e.PopupPage is not null)
            .ToDictionary(e => "ext-" + e.Id, e => e);
        foreach (var stale in ExtensionPanels.Where(p => !wanted.ContainsKey(p.Id)).ToList())
        {
            UnloadPanel(stale);
            ExtensionPanels.Remove(stale);
            HostedPanels.Remove(stale);
        }

        foreach (var (id, extension) in wanted)
        {
            if (ExtensionPanels.Any(p => p.Id == id))
                continue;

            var panel = new PanelViewModel(
                new WebPanel { Id = id, Name = extension.Name, Url = $"chrome-extension://{extension.Id}/{extension.PopupPage}" },
                LoadExtensionIcon(extension.IconFile));
            ExtensionPanels.Add(panel);
            HostedPanels.Add(panel);
        }
    }

    private static ImageSource? LoadExtensionIcon(string? file)
    {
        if (file is null || !File.Exists(file))
            return null;

        try
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 40;
            bitmap.UriSource = new Uri(file);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or COMException or FileFormatException)
        {
            return null;
        }
    }

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
    public ICommand ApplyUpdateCommand { get; }

    /// <summary>A new version is downloaded; the toolbar shows the restart button.</summary>
    public bool IsUpdateReady => UpdateService.State == UpdateState.ReadyToRestart;

    /// <summary>Normal close (saves everything), then the updater installs and restarts Sorune.</summary>
    private void ApplyUpdate()
    {
        if (UpdateService.PrepareApplyOnExit())
        {
            Log.Info("Güncelleme için kapanıyor.");
            CloseWindowRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? FocusAddressBarRequested;
    public event EventHandler? FocusContentRequested;
    public event EventHandler? CloseWindowRequested;

    /// <summary>Layout or motion settings changed; the view re-fits sizes that aren't resource-bound.</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>The settings page asked for a wallpaper file; the view shows the file picker.</summary>
    public event EventHandler? WallpaperPickRequested;

    /// <summary>"Restart now" after changing the language.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Shows a folder picker starting at the given folder; null when cancelled.</summary>
    public Func<string?, string?>? PickFolder { get; set; }

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

            // Switching to a tab hidden in a collapsed group opens the group.
            if (value.Group is { IsCollapsed: true } group)
            {
                group.IsCollapsed = false;
                RefreshGroups();
            }

            _activeTab = value;
            value.IsActive = true;
            value.EnsureStarted();
            Mascot.NoteActivity();

            OnPropertyChanged();
            RaiseSplitLayout();
            _ = value.RefreshPictureInPictureAsync();
            _ = value.RefreshSiteColorAsync();
            OnPropertyChanged(nameof(ToolbarSiteColor));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(IsContentFullScreen));
            OnPropertyChanged(nameof(IsActiveTabLoading));
            OnPropertyChanged(nameof(IsActiveTabBookmarked));
            OnPropertyChanged(nameof(CanBookmark));
            CommandManager.InvalidateRequerySuggested();
            ScheduleSave();
        }
    }

    /// <summary>"#rrggbb" the toolbar fades into (the active site's top colour); null keeps the theme look.</summary>
    public string? ToolbarSiteColor => _settings.SiteTintEnabled ? ActiveTab?.SiteColor : null;

    /// <summary>Pages are shown inset as a framed card (off in fullscreen, handled by the view).</summary>
    public bool IsPageFramed => _settings.PageFrameEnabled;

    public string WindowTitle => (ActiveTab is { } tab ? $"{tab.Title} - Sorune" : "Sorune")
                                 + (ProfileService.IsDefault ? "" : $" ({CurrentProfile.Name})");

    // ---------------------------------------------------------------- profiles

    private ProfileInfo _currentProfile = ProfileService.Current;

    /// <summary>The profile this window belongs to (toolbar badge).</summary>
    public ProfileInfo CurrentProfile
    {
        get => _currentProfile;
        private set
        {
            if (SetProperty(ref _currentProfile, value))
            {
                OnPropertyChanged(nameof(ProfileBrush));
                OnPropertyChanged(nameof(WindowTitle));
            }
        }
    }

    public System.Windows.Media.Brush ProfileBrush
    {
        get
        {
            var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_currentProfile.Color));
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>Profiles for the toolbar menu and the settings page (with which ones are open).</summary>
    public object BuildProfilesState() => new
    {
        type = "profiles",
        current = ProfileService.CurrentId,
        items = ProfileService.List().Select(p => new
        {
            p.Id,
            p.Name,
            p.Emoji,
            p.Color,
            isDefault = p.Id == ProfileService.DefaultId,
            isCurrent = p.Id == ProfileService.CurrentId,
            running = ProfileService.IsRunning(p.Id),
        }),
        colors = ProfileService.Colors,
    };

    public void OpenProfile(string id)
    {
        if (id == ProfileService.CurrentId)
            return;
        ProfileService.Launch(id);
        SoundService.Play(UiSound.TabOpen);
    }

    private void RefreshProfiles()
    {
        CurrentProfile = ProfileService.Current;
        BroadcastToInternalPages(BuildProfilesState());
    }

    public bool IsContentFullScreen => ActiveTab?.IsContentFullScreen == true;

    public bool IsActiveTabLoading => ActiveTab?.IsLoading == true;

    public bool CanBookmark => ActiveTab?.Url is { } url && AddressResolver.IsNavigable(url) && !InternalPages.IsInternal(url);

    public bool IsActiveTabBookmarked => _bookmarks.Contains(ActiveTab?.Url);

    public SearchEngine SearchEngine => SearchEngines.Find(_settings.SearchEngine);

    // ---------------------------------------------------------------- sidebar

    public bool IsSidebarVisible => _settings.SidebarEnabled;

    public bool IsSidebarOnRight => _settings.SidebarPosition == "right";

    public bool IsVerticalTabs => _settings.VerticalTabs;

    public double VerticalTabsWidth => _settings.VerticalTabsWidth;

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
        // The session is always kept on disk (as crash insurance); the setting decides whether it
        // comes back by itself. After a crash with the setting off, Sorune offers it instead.
        var session = SessionStore.Load();
        if (session is { Tabs.Count: > 0 })
        {
            // The previous run goes into the time machine before anything else happens.
            _timeMachine.ArchiveSession(session, SessionStore.LastSavedAt ?? DateTime.Now);
            if (_settings.RestoreSession)
                RestoreSession(session);
            else if (SessionStore.PreviousRunCrashed)
                RecoveryOffer = session;
        }

        if (OpenExternal(args) == 0 && Tabs.Count == 0)
            NewTab();
        ScheduleSave();

        // First run: the welcome tour instead of a bare new tab.
        if (SettingsStore.IsFirstRun)
        {
            Tabs[0].Navigate(InternalPages.WelcomeUrl);
            ActiveTab = Tabs[0];
        }

        // A hello once things have settled.
        var greet = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        greet.Tick += (_, _) =>
        {
            greet.Stop();
            Mascot.Greet(recovered: SessionStore.PreviousRunCrashed && _settings.RestoreSession);
        };
        greet.Start();

        // First start of a new version (not the very first start): show what changed, once.
        var version = UpdateService.CurrentVersion;
        if (_settings.LastSeenVersion != version)
        {
            var updated = !SettingsStore.IsFirstRun;
            _settings.LastSeenVersion = version;
            SettingsStore.Save(_settings);
            if (updated)
                OpenInternal(InternalPages.WhatsNewUrl);
        }
    }

    /// <summary>Recreates saved tabs (with groups, pins and the active tab) at the end of the strip.</summary>
    private void RestoreSession(SessionState session)
    {
        var groups = session.Groups
            .Where(g => !string.IsNullOrEmpty(g.Id))
            .GroupBy(g => g.Id).Select(g => g.First())
            .ToDictionary(g => g.Id, g => new TabGroupViewModel(g.Id, g.Name, g.Color, g.IsCollapsed));
        var restored = new List<TabViewModel?>();
        foreach (var saved in session.Tabs)
        {
            if (!AddressResolver.IsNavigable(saved.Url))
            {
                restored.Add(null);
                continue;
            }

            var tab = CreateTab(saved.Url, Tabs.Count, saved.Title, isPinned: saved.IsPinned);
            tab.Volume = Math.Clamp(saved.Volume, 0, 100);
            if (!saved.IsPinned && saved.GroupId is { } id && groups.TryGetValue(id, out var group))
            {
                if (!Groups.Contains(group))
                    Groups.Add(group);
                tab.Group = group;
            }
            restored.Add(tab);
        }
        RefreshGroups();

        TabViewModel? At(int index) => index >= 0 && index < restored.Count ? restored[index] : null;
        if ((At(session.ActiveIndex) ?? restored.LastOrDefault(t => t is not null)) is { } active)
        {
            ActiveTab = active;
            Log.Info($"Oturum geri yüklendi: {restored.Count(t => t is not null)} sekme.");
        }

        if (At(session.SplitLeft) is { } left && At(session.SplitRight) is { } right && left != right)
        {
            SplitRatio = Math.Clamp(session.SplitRatio, 0.2, 0.8);
            SetSplit(left, right);
        }
    }

    // ---------------------------------------------------------------- crash recovery

    private SessionState? _recoveryOffer;

    /// <summary>Tabs from a run that crashed while "continue previous session" was off; null when none.</summary>
    public SessionState? RecoveryOffer
    {
        get => _recoveryOffer;
        private set
        {
            if (SetProperty(ref _recoveryOffer, value))
                OnPropertyChanged(nameof(RecoveryTabCount));
        }
    }

    public int RecoveryTabCount => _recoveryOffer?.Tabs.Count ?? 0;

    public ICommand AcceptRecoveryCommand => new RelayCommand(() =>
    {
        if (RecoveryOffer is not { } session)
            return;

        RecoveryOffer = null;
        // The empty new tab opened at start is replaced by the recovered tabs.
        var blank = Tabs.Count == 1 && Tabs[0].IsNewTabPage ? Tabs[0] : null;
        RestoreSession(session);
        if (blank is not null && Tabs.Count > 1)
            CloseTab(blank);
        SoundService.Play(UiSound.TabOpen);
    });

    public ICommand DismissRecoveryCommand => new RelayCommand(() => RecoveryOffer = null);

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

    public void OpenTaskManager() => OpenInternal(InternalPages.TaskManagerUrl);

    /// <summary>Tells sites (prefers-color-scheme) which theme to use; per profile, so normal and InPrivate both.</summary>
    private void ApplyColorScheme(CoreWebView2 core)
    {
        try
        {
            core.Profile.PreferredColorScheme = _settings.SiteColorScheme switch
            {
                "dark" => CoreWebView2PreferredColorScheme.Dark,
                "light" => CoreWebView2PreferredColorScheme.Light,
                _ => CoreWebView2PreferredColorScheme.Auto,
            };
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Log.Info($"Site renk teması ayarlanamadı: {ex.Message}");
        }
    }

    private TabViewModel? TabById(string? id) => Tabs.FirstOrDefault(t => t.Id == id);

    /// <summary>A link from the preview window (or similar) as a new tab after the active one.</summary>
    public void OpenInNewTab(string url, bool isPrivate)
    {
        var index = ActiveTab is { } active ? Tabs.IndexOf(active) + 1 : Tabs.Count;
        ActiveTab = CreateTab(url, index, isPrivate: isPrivate, group: ActiveTab?.Group);
        SoundService.Play(UiSound.TabOpen);
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
                ? new SuggestionViewModel(text, L.T("{0} ile ara", SearchEngine.Name), resolved, "")
                : new SuggestionViewModel(resolved, L.T("Siteye git"), resolved, ""));

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
            ActiveTab = CreateTab(url, Tabs.IndexOf(tab) + 1, tab.Title, tab.IsPrivate, group: tab.Group);
    });

    public ICommand ReloadTabCommand => new RelayCommand(p => (p as TabViewModel)?.Reload());

    public ICommand ToggleMuteCommand => new RelayCommand(p => (p as TabViewModel)?.ToggleMute());

    public ICommand TogglePinCommand => new RelayCommand(p =>
    {
        if (p is not TabViewModel tab)
            return;

        // Pinned tabs live together at the start of the strip, outside any group.
        if (!tab.IsPinned)
            LeaveGroup(tab);
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

    public ICommand AddTabToNewGroupCommand => new RelayCommand(p =>
    {
        if (p is TabViewModel tab)
            AddToNewGroup(tab);
    });

    public ICommand RemoveTabFromGroupCommand => new RelayCommand(p =>
    {
        if (p is TabViewModel tab)
        {
            LeaveGroup(tab);
            RefreshGroups();
            ScheduleSave();
        }
    });

    // ---------------------------------------------------------------- split screen

    private TabViewModel? _splitLeft;
    private TabViewModel? _splitRight;
    private double _splitRatio = 0.5;

    public TabViewModel? SplitLeft => _splitLeft;
    public TabViewModel? SplitRight => _splitRight;

    public bool IsSplit => _splitLeft is not null && _splitRight is not null;

    /// <summary>The split pair is on screen (one of its tabs is the active tab).</summary>
    public bool IsSplitVisible => IsSplit && (ActiveTab == _splitLeft || ActiveTab == _splitRight);

    /// <summary>Share of the content width taken by the left pane (0.15..0.85).</summary>
    public double SplitRatio
    {
        get => _splitRatio;
        set
        {
            if (SetProperty(ref _splitRatio, Math.Clamp(value, 0.15, 0.85)))
            {
                RaiseSplitLayout();
                ScheduleSave();
            }
        }
    }

    public System.Windows.GridLength SplitLeftWidth => IsSplitVisible
        ? new System.Windows.GridLength(_splitRatio, System.Windows.GridUnitType.Star)
        : new System.Windows.GridLength(1, System.Windows.GridUnitType.Star);

    public System.Windows.GridLength SplitDividerWidth => new(IsSplitVisible ? 6 : 0);

    public System.Windows.GridLength SplitRightWidth => IsSplitVisible
        ? new System.Windows.GridLength(1 - _splitRatio, System.Windows.GridUnitType.Star)
        : new System.Windows.GridLength(0);

    public ICommand SplitRightCommand => new RelayCommand(p => SplitWith(p as TabViewModel, SplitSide.Right));
    public ICommand SplitLeftCommand => new RelayCommand(p => SplitWith(p as TabViewModel, SplitSide.Left));
    public ICommand CloseSplitCommand => new RelayCommand(CloseSplit);
    public ICommand SwapSplitCommand => new RelayCommand(SwapSplit);

    /// <summary>
    /// Shows <paramref name="tab"/> on the given side, next to the active tab. On the active tab
    /// itself, the other side gets a new tab.
    /// </summary>
    public void SplitWith(TabViewModel? tab, SplitSide side)
    {
        tab ??= ActiveTab;
        if (tab is null || side == SplitSide.None)
            return;

        var partner = ReferenceEquals(tab, ActiveTab) ? null : ActiveTab;
        if (partner is null)
        {
            partner = CreateTab(InternalPages.NewTabUrl, Tabs.IndexOf(tab) + 1, group: tab.Group);
            SoundService.Play(UiSound.TabOpen);
        }

        if (side == SplitSide.Right)
            SetSplit(partner, tab);
        else
            SetSplit(tab, partner);
        ActiveTab = tab.IsNewTabPage || !partner.IsNewTabPage ? tab : partner;
        if (ActiveTab!.IsNewTabPage)
            FocusAddressBarRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SplitWithNewTab()
    {
        if (ActiveTab is { } tab)
            SplitWith(tab, SplitSide.Right);
    }

    private void SetSplit(TabViewModel left, TabViewModel right)
    {
        ClearSplitSides();
        _splitLeft = left;
        _splitRight = right;
        left.SplitSide = SplitSide.Left;
        right.SplitSide = SplitSide.Right;
        RaiseSplitLayout();
        ScheduleSave();
    }

    /// <summary>Back to one page; both tabs stay open in the strip.</summary>
    public void CloseSplit()
    {
        if (!IsSplit)
            return;

        ClearSplitSides();
        RaiseSplitLayout();
        ScheduleSave();
    }

    public void SwapSplit()
    {
        if (_splitLeft is { } left && _splitRight is { } right)
            SetSplit(right, left);
    }

    private void ClearSplitSides()
    {
        if (_splitLeft is not null)
            _splitLeft.SplitSide = SplitSide.None;
        if (_splitRight is not null)
            _splitRight.SplitSide = SplitSide.None;
        _splitLeft = _splitRight = null;
    }

    private void RaiseSplitLayout()
    {
        UpdateShownTabs();
        OnPropertyChanged(nameof(IsSplit));
        OnPropertyChanged(nameof(IsSplitVisible));
        OnPropertyChanged(nameof(SplitLeftWidth));
        OnPropertyChanged(nameof(SplitDividerWidth));
        OnPropertyChanged(nameof(SplitRightWidth));
    }

    /// <summary>The active tab is shown; with a visible split, its partner too.</summary>
    private void UpdateShownTabs()
    {
        var split = IsSplitVisible;
        foreach (var tab in HostedTabs)
            tab.IsShown = ReferenceEquals(tab, ActiveTab) || (split && (ReferenceEquals(tab, _splitLeft) || ReferenceEquals(tab, _splitRight)));
    }

    /// <summary>A click inside one half of the split makes that half the active tab (address bar follows).</summary>
    private void OnContentFocused(object? sender, EventArgs e)
    {
        if (sender is TabViewModel { IsInSplit: true } tab && !ReferenceEquals(tab, ActiveTab) && Tabs.Contains(tab))
            ActiveTab = tab;
    }

    // ---------------------------------------------------------------- command palette

    private int _paletteIndex;

    public ObservableCollection<PaletteItem> PaletteItems { get; } = [];

    /// <summary>Highlighted row; Enter runs it.</summary>
    public int PaletteIndex
    {
        get => _paletteIndex;
        set => SetProperty(ref _paletteIndex, PaletteItems.Count == 0 ? -1 : Math.Clamp(value, 0, PaletteItems.Count - 1));
    }

    /// <summary>Ctrl+K: the view opens the palette.</summary>
    public event EventHandler? PaletteRequested;

    /// <summary>Opening the volume popup for a tab (from the palette or menus).</summary>
    public event EventHandler<TabViewModel>? VolumeEditRequested;

    /// <summary>Taking a screenshot from the palette ("visible", "full" or "region").</summary>
    public event EventHandler<string>? ScreenshotRequested;

    /// <summary>Opening the find bar from the palette.</summary>
    public event EventHandler? FindRequested;

    /// <summary>Toggling fullscreen from the palette.</summary>
    public event EventHandler? FullScreenRequested;

    public void OpenPalette() => PaletteRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Filters as the user types: every word must appear in the title, subtitle or keywords.</summary>
    public void UpdatePalette(string query)
    {
        var culture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        var words = query.Trim().ToLower(culture).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Matches(PaletteItem item)
        {
            var text = $"{item.Title} {item.Subtitle} {item.Keywords}".ToLower(culture);
            return words.All(text.Contains);
        }

        var results = new List<PaletteItem>();
        var commands = PaletteCommands().Where(Matches).ToList();
        if (words.Length == 0)
        {
            results.AddRange(commands);
        }
        else
        {
            // Titles starting with the query first, then open tabs, then the rest.
            var first = words[0];
            results.AddRange(commands.Where(c => c.Title.ToLower(culture).StartsWith(first, StringComparison.Ordinal)));
            results.AddRange(PaletteTabs().Where(Matches));
            results.AddRange(commands.Where(c => !results.Contains(c)));
            results.AddRange(_bookmarks.Items.Select(b => new PaletteItem("", b.Title, b.Url, () => OpenUrl(b.Url), L.T("yer imi")))
                .Where(Matches).Take(5));
            var seen = results.Select(r => r.Subtitle).ToHashSet();
            results.AddRange(_history.Suggest(query.Trim(), 5)
                .Where(h => seen.Add(h.Url))
                .Select(h => new PaletteItem("", string.IsNullOrWhiteSpace(h.Title) ? h.Url : h.Title, h.Url, () => OpenUrl(h.Url))));
        }

        PaletteItems.Clear();
        foreach (var item in results.Take(60))
            PaletteItems.Add(item);
        PaletteIndex = 0;
    }

    public void RunPaletteItem(int index)
    {
        if (index < 0 || index >= PaletteItems.Count)
            return;

        var item = PaletteItems[index];
        PaletteItems.Clear();
        item.Run();
    }

    private void OpenUrl(string url)
    {
        if (ActiveTab is { IsNewTabPage: true } tab)
            tab.Navigate(url);
        else
            ActiveTab = CreateTab(url, Tabs.Count);
        FocusContentRequested?.Invoke(this, EventArgs.Empty);
    }

    private IEnumerable<PaletteItem> PaletteTabs()
        => Tabs.Select(t => new PaletteItem(t.IsInSplit ? "" : "", t.Title, InternalPages.ToDisplayAddress(t.Url) is { Length: > 0 } a ? a : L.T("Yeni Sekme"),
            () => ActiveTab = t, L.T("sekmeye geç")));

    private IEnumerable<PaletteItem> PaletteCommands()
    {
        PaletteItem Cmd(string glyph, string title, string shortcut, Action run, string keywords = "")
            => new(glyph, L.T(title), shortcut, run, keywords);

        var tab = ActiveTab;
        yield return Cmd("", "Yeni sekme", "Ctrl+T", NewTab, "new tab aç");
        yield return Cmd("", "Yeni gizli sekme", "Ctrl+Shift+N", NewPrivateTab, "private inprivate incognito");
        if (tab is not null)
        {
            yield return Cmd("", "Sayfayı yenile", "F5", tab.Reload, "reload refresh");
            yield return Cmd("", "Sekmeyi kapat", "Ctrl+W", () => CloseTab(tab), "close");
            yield return Cmd("", tab.IsPinned ? "Sabitlemeyi kaldır" : "Sekmeyi sabitle", "", () => TogglePinCommand.Execute(tab), "pin");
            yield return Cmd(tab.IsMuted ? "" : "", tab.IsMuted ? "Sesi aç" : "Sesi kapat", "", tab.ToggleMute, "mute sound ses");
            yield return Cmd("", "Sekmenin ses düzeyi", L.T("şu an %{0}", tab.Volume), () => VolumeEditRequested?.Invoke(this, tab), "volume ses düzey");
        }
        yield return Cmd("", "Kapatılan sekmeyi geri aç", "Ctrl+Shift+T", ReopenClosedTab, "reopen undo");
        if (IsSplit)
        {
            yield return Cmd("", "Bölünmüş ekranı kapat", "", CloseSplit, "split screen kapat close");
            yield return Cmd("", "Bölmelerin yerini değiştir", "", SwapSplit, "split swap");
        }
        else
        {
            yield return Cmd("", "Bölünmüş ekran: yeni sekmeyle", "", SplitWithNewTab, "split screen yan yana böl");
            foreach (var other in Tabs.Where(t => t != tab).Take(8))
                yield return Cmd("", L.T("Yan yana aç: {0}", other.Title), "", () => SplitWith(other, SplitSide.Right), "split screen yan yana böl");
        }
        if (tab?.HasPictureInPictureVideo == true)
            yield return Cmd("", "Videoyu resim içinde resim yap", "", () => _ = TogglePictureInPictureAsync(), "picture in picture pip video");
        yield return Cmd("", "Diğer sekmeleri uyut", "", SleepOtherTabs, "sleep suspend uyku");
        if (tab is { Core: not null })
        {
            yield return Cmd("", "Ekran görüntüsü: görünen alan", "", () => ScreenshotRequested?.Invoke(this, "visible"), "screenshot ekran");
            yield return Cmd("", "Ekran görüntüsü: tam sayfa", "", () => ScreenshotRequested?.Invoke(this, "full"), "screenshot ekran full");
            yield return Cmd("", "Ekran görüntüsü: bölge seç", "", () => ScreenshotRequested?.Invoke(this, "region"), "screenshot ekran region");
        }
        if (tab?.IsReadable == true)
            yield return Cmd("", "Okuma modu", "", () => _ = OpenReaderAsync(), "reader read okuma");
        yield return Cmd("", "Karşılama turunu aç", "", () => OpenInternal(InternalPages.WelcomeUrl), "welcome tour karşılama");
        yield return Cmd("", "Sayfada bul", "Ctrl+F", () => FindRequested?.Invoke(this, EventArgs.Empty), "find search ara");
        yield return Cmd("", "Tam ekran", "F11", () => FullScreenRequested?.Invoke(this, EventArgs.Empty), "fullscreen");
        if (CanBookmark)
            yield return Cmd("", IsActiveTabBookmarked ? "Yer iminden kaldır" : "Yer imlerine ekle", "Ctrl+D", ToggleBookmark, "bookmark");
        if (tab is not null)
            yield return Cmd("", "Sekmeyi yeni gruba ekle", "", () => AddToNewGroup(tab), "group grup");
        yield return Cmd("", "Geçmiş", "Ctrl+H", OpenHistory, "history");
        yield return Cmd("", "Zaman makinesi", "", () => OpenInternal(InternalPages.TimeMachineUrl), "time machine closed tabs sessions kapatılan oturum");
        yield return Cmd("", "Görev yöneticisi", "Shift+Esc", OpenTaskManager, "task manager cpu ram bellek işlemci");
        yield return Cmd("", "Profiller", "", () => OpenSettings("profiller"), "profiles profil");
        yield return Cmd("", "İndirmeler", "Ctrl+J", OpenDownloads, "downloads");
        yield return Cmd("", "Yer imleri", "Ctrl+Shift+O", OpenBookmarks, "bookmarks");
        yield return Cmd("", "Ayarlar", "Ctrl+,", () => OpenSettings(), "settings");
        yield return Cmd("", "Tarama verilerini temizle", "Ctrl+Shift+Del", () => OpenSettings("gizlilik"), "clear data privacy");
        yield return Cmd("", "Dosya dönüştürücü", "", () => TogglePanel(ConverterPanel), "converter pdf word");
        yield return Cmd("", "Mini araçlar", "", () => TogglePanel(ToolsPanel), "tools json qr renk color sayaç url");
        yield return Cmd("", "Yenilikler", "", () => OpenInternal(InternalPages.WhatsNewUrl), "what's new");
        yield return Cmd("", "Yama notları", "", () => OpenInternal(InternalPages.ChangelogUrl), "changelog patch notes sürüm version");
        yield return Cmd("", IsVerticalTabs ? "Sekmeleri üste al" : "Dikey sekmeler", "", () => _settingsController.SetVerticalTabs(!IsVerticalTabs), "vertical tabs dikey sekme liste");
        yield return Cmd("", "Kendi temaların", "", () => OpenSettings("temalarim"), "theme tema kaydet save look");
        yield return Cmd("", "Yedekle ve taşı", "", () => OpenSettings("yedek"), "backup restore yedek taşı move export import");
    }

    // ---------------------------------------------------------------- reader mode

    private readonly Dictionary<TabViewModel, JsonElement> _readerArticles = [];

    public ICommand OpenReaderCommand => new RelayCommand(() => _ = OpenReaderAsync());

    /// <summary>Reads the article out of the page, then shows it on the reader page in the same tab (Back returns).</summary>
    public async Task OpenReaderAsync()
    {
        if (ActiveTab is not { } tab)
            return;

        var json = await tab.ExtractArticleAsync();
        if (json is null)
        {
            Mascot.Say(L.T("Bu sayfada okunacak bir yazı bulamadım"));
            return;
        }

        using (var document = JsonDocument.Parse(json))
            _readerArticles[tab] = document.RootElement.Clone();
        tab.Navigate(InternalPages.ReaderUrl);
    }

    // ---------------------------------------------------------------- picture-in-picture

    private readonly DispatcherTimer _pipTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    public ICommand TogglePictureInPictureCommand => new RelayCommand(() => _ = TogglePictureInPictureAsync());

    public async Task TogglePictureInPictureAsync()
    {
        if (ActiveTab is not { } tab)
            return;

        var result = await tab.TogglePictureInPictureAsync();
        Log.Info($"Resim içinde resim: {result}");
        if (result == "opened")
            Mascot.Say(L.T("Keyifli seyirler 🍿"), happy: true);
        if (result?.StartsWith("error", StringComparison.Ordinal) == true || result == "unsupported")
            Mascot.OnPageLoaded(success: false);
        await tab.RefreshPictureInPictureAsync();
    }

    // ---------------------------------------------------------------- tab groups

    public ObservableCollection<TabGroupViewModel> Groups { get; } = [];

    public bool HasGroups => Groups.Count > 0;

    /// <summary>A group was just created: the view opens its name/colour editor.</summary>
    public event EventHandler<TabGroupViewModel>? GroupEditRequested;

    public void AddToNewGroup(TabViewModel tab)
    {
        LeaveGroup(tab);
        UnpinForGroup(tab);
        // Each new group takes the next colour nobody uses yet.
        var color = TabGroupViewModel.Palette.Select(p => p.Key).FirstOrDefault(k => Groups.All(g => g.ColorKey != k))
                    ?? TabGroupViewModel.Palette[Groups.Count % TabGroupViewModel.Palette.Length].Key;
        var group = new TabGroupViewModel(null, "", color);
        Groups.Add(group);
        tab.Group = group;
        RefreshGroups();
        ScheduleSave();
        GroupEditRequested?.Invoke(this, group);
    }

    public void AddToGroup(TabViewModel tab, TabGroupViewModel group)
    {
        if (ReferenceEquals(tab.Group, group) || !Groups.Contains(group))
            return;

        LeaveGroup(tab);
        UnpinForGroup(tab);
        MoveAfter(tab, Tabs.Last(t => ReferenceEquals(t.Group, group)));
        tab.Group = group;
        group.IsCollapsed = false;
        RefreshGroups();
        ScheduleSave();
    }

    public void ToggleGroupCollapsed(TabGroupViewModel group)
    {
        if (!group.IsCollapsed && ActiveTab?.Group == group)
        {
            // The active tab can't hide: switch to the nearest tab outside the group (or a new one).
            var index = Tabs.IndexOf(ActiveTab);
            var outside = Tabs.Skip(index).FirstOrDefault(t => t.Group != group)
                          ?? Tabs.Take(index).LastOrDefault(t => t.Group != group);
            if (outside is null)
                NewTab();
            else
                ActiveTab = outside;
        }

        group.IsCollapsed = !group.IsCollapsed;
        SoundService.Play(UiSound.Toggle);
        RefreshGroups();
        ScheduleSave();
    }

    public void Ungroup(TabGroupViewModel group)
    {
        foreach (var tab in Tabs.Where(t => t.Group == group))
            tab.Group = null;
        RefreshGroups();
        ScheduleSave();
    }

    public void CloseGroup(TabGroupViewModel group)
    {
        var members = Tabs.Where(t => t.Group == group).ToList();
        if (members.Count == Tabs.Count)
            NewTab(); // closing every tab would close the window
        if (Tabs.FirstOrDefault(t => t.Group != group) is { } survivor)
            CloseTabs(members, survivor);
    }

    public void NewTabInGroup(TabGroupViewModel group)
    {
        if (Tabs.LastOrDefault(t => t.Group == group) is not { } last)
            return;

        group.IsCollapsed = false;
        ActiveTab = CreateTab(InternalPages.NewTabUrl, Tabs.IndexOf(last) + 1, group: group);
        SoundService.Play(UiSound.TabOpen);
        FocusAddressBarRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves the group's new name or colour (called by the editor).</summary>
    public void GroupChanged() => ScheduleSave();

    /// <summary>Takes a tab out of its group without splitting it: it moves just past the group first.</summary>
    private void LeaveGroup(TabViewModel tab)
    {
        if (tab.Group is not { } group)
            return;

        MoveAfter(tab, Tabs.Last(t => t.Group == group));
        tab.Group = null;
    }

    private void UnpinForGroup(TabViewModel tab)
    {
        if (!tab.IsPinned)
            return;

        tab.IsPinned = false;
        Tabs.Move(Tabs.IndexOf(tab), Tabs.Count(t => t.IsPinned));
    }

    private void MoveAfter(TabViewModel tab, TabViewModel anchor)
    {
        if (ReferenceEquals(tab, anchor))
            return;

        var from = Tabs.IndexOf(tab);
        var to = Tabs.IndexOf(anchor);
        Tabs.Move(from, from > to ? to + 1 : to);
    }

    /// <summary>Recomputes which tab draws each group's label, hides collapsed members and drops empty groups.</summary>
    private void RefreshGroups()
    {
        foreach (var empty in Groups.Where(g => Tabs.All(t => t.Group != g)).ToList())
            Groups.Remove(empty);

        for (var i = 0; i < Tabs.Count; i++)
        {
            var tab = Tabs[i];
            if (tab.Group is { } group && !Groups.Contains(group))
                tab.Group = null;
            tab.IsGroupStart = tab.Group is not null && (i == 0 || Tabs[i - 1].Group != tab.Group);
            tab.IsCollapsedInGroup = tab.Group?.IsCollapsed == true;
        }

        OnPropertyChanged(nameof(HasGroups));
    }

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
        _pipTimer.Stop();
        // This run's tabs go into the time machine even when they won't be restored next time.
        _timeMachine.ArchiveSession(BuildSessionState(), DateTime.Now);
        if (_settings.RestoreSession)
            SaveSession();
        else
            SessionStore.Delete(); // normal exit: nothing to recover
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
        foreach (var panel in HostedPanels)
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

        // Remembered for Ctrl+Shift+T and the time machine (never InPrivate tabs or empty new tabs).
        if (tab.Url is { } url && !tab.IsPrivate && !InternalPages.IsNewTab(url) && !InternalPages.IsPage(url, InternalPages.ReaderUrl))
        {
            _timeMachine.AddClosed(new ClosedTab
            {
                Url = url,
                Title = tab.Title,
                Index = index,
                IsPinned = tab.IsPinned,
                GroupId = tab.Group?.Id,
                GroupName = tab.Group?.Name,
                GroupColor = tab.Group?.ColorKey,
            });
        }

        // A closing half of the split screen ends the split (the other tab stays).
        TabViewModel? partner = null;
        if (tab.IsInSplit)
        {
            partner = ReferenceEquals(tab, _splitLeft) ? _splitRight : _splitLeft;
            CloseSplit();
        }

        // Pick the neighbour (or the split partner) before removing so the strip doesn't jump.
        if (ReferenceEquals(tab, ActiveTab) && Tabs.Count > 1)
            ActiveTab = partner ?? Tabs[index + 1 < Tabs.Count ? index + 1 : index - 1];

        DenyPermissionsOf(tab);
        _readerArticles.Remove(tab);
        foreach (var child in Tabs.Where(t => ReferenceEquals(t.Opener, tab)))
            child.Opener = null;
        Tabs.Remove(tab);
        HostedTabs.Remove(tab);
        Detach(tab);
        tab.Dispose();
        SoundService.Play(UiSound.TabClose);

        if (Tabs.Count == 0)
        {
            Log.Info("Son sekme kapandı, pencere kapanıyor.");
            _activeTab = null;
            CloseWindowRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        ScheduleSave();
    }

    /// <summary>Ctrl+Shift+T: the last closed tab comes back where it was (pinned, in its group).</summary>
    public void ReopenClosedTab()
    {
        if (_timeMachine.TakeLatest() is { } closed)
            RestoreClosed(closed);
    }

    public bool CanReopenClosedTab => _timeMachine.Closed.Count > 0;

    public ICommand ReopenClosedTabCommand => new RelayCommand(ReopenClosedTab);

    private void RestoreClosed(ClosedTab closed)
    {
        var tab = CreateTab(closed.Url, Math.Clamp(closed.Index, 0, Tabs.Count), closed.Title, isPinned: closed.IsPinned);
        if (!closed.IsPinned && closed.GroupId is { } groupId)
        {
            if (Groups.FirstOrDefault(g => g.Id == groupId) is { } group)
            {
                AddToGroup(tab, group);
            }
            else
            {
                // The group is gone too: it comes back with its name and colour.
                var revived = new TabGroupViewModel(groupId, closed.GroupName, closed.GroupColor);
                Groups.Add(revived);
                tab.Group = revived;
                RefreshGroups();
            }
        }

        ActiveTab = tab;
        SoundService.Play(UiSound.TabOpen);
        ScheduleSave();
    }

    // ---------------------------------------------------------------- tab time machine

    private object BuildTimeMachineState() => new
    {
        type = "timemachine",
        closed = _timeMachine.Closed.Select(c => new { c.Id, c.Url, c.Title, c.ClosedAt, group = c.GroupName }),
        sessions = _timeMachine.Sessions.Select(s => new { s.Id, s.SavedAt, tabs = s.Tabs }),
    };

    /// <summary>Opens an earlier run's tabs (all of them, or the one with <paramref name="url"/>).</summary>
    private void OpenArchivedSession(string? id, string? url)
    {
        if (_timeMachine.FindSession(id) is not { } session)
            return;

        TabViewModel? first = null;
        foreach (var tab in session.Tabs.Where(t => url is null || t.Url == url))
        {
            if (!AddressResolver.IsNavigable(tab.Url))
                continue;
            var created = CreateTab(tab.Url, Tabs.Count, tab.Title);
            first ??= created;
        }

        if (first is not null)
        {
            ActiveTab = first;
            SoundService.Play(UiSound.TabOpen);
        }
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

        // Dropped between two tabs of a group: joins it. Still touching its own group: stays.
        // Anywhere else: leaves its group.
        var left = newIndex > 0 ? Tabs[newIndex - 1] : null;
        var right = newIndex < Tabs.Count - 1 ? Tabs[newIndex + 1] : null;
        if (!tab.IsPinned && left?.Group is { } around && around == right?.Group)
            tab.Group = around;
        else if (tab.Group is not { } own || (left?.Group != own && right?.Group != own))
            tab.Group = null;

        RefreshGroups();
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

    private TabViewModel CreateTab(string? url, int index, string? title = null, bool isPrivate = false, bool isPinned = false,
        TabGroupViewModel? group = null)
    {
        var tab = CreatePage(url, title, isPrivate, isPinned);
        // New tabs never land inside the pinned group.
        if (!isPinned)
            index = Math.Max(index, Tabs.Count(t => t.IsPinned));
        tab.NewWindowRequested += OnNewWindowRequested;
        tab.InternalPageMessage += OnInternalPageMessage;
        tab.NavigationFinished += OnNavigationFinished;

        index = Math.Clamp(index, 0, Tabs.Count);
        // Inserted in the middle of a group (or asked to): it belongs to that group.
        if (!isPinned)
        {
            var left = index > 0 ? Tabs[index - 1] : null;
            var right = index < Tabs.Count ? Tabs[index] : null;
            tab.Group = group is not null && (left?.Group == group || right?.Group == group)
                ? group
                : left?.Group is { } around && around == right?.Group ? around : null;
        }

        Tabs.Insert(index, tab);
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
        tab.ContentFocused += OnContentFocused;
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
        tab.ContentFocused -= OnContentFocused;
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

    private void OnCoreCreated(object? sender, CoreWebView2 core)
    {
        _downloads.Attach(core);
        ApplyColorScheme(core);
        // The first normal page brings the extensions' sidebar buttons.
        if (!_extensionsLoaded && sender is TabViewModel { IsPrivate: false })
        {
            _extensionsLoaded = true;
            _ = RefreshExtensionPanelsAsync(core.Profile);
        }
    }

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
            case nameof(TabViewModel.SiteColor):
                OnPropertyChanged(nameof(ToolbarSiteColor));
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
            // Middle click / Ctrl+click: a background tab (after the ones already opened from
            // this page), the page stays where it is. Anything else opens in front.
            var background = source.ConsumeBackgroundOpen();
            var index = background ? IndexForBackgroundChild(source) : Tabs.IndexOf(source) + 1;
            // Popups from an InPrivate tab stay InPrivate (and share its profile).
            var tab = CreateTab(url: null, index, isPrivate: source.IsPrivate, group: source.Group);
            tab.Opener = source;
            if (!background)
                ActiveTab = tab;
            SoundService.Play(UiSound.TabOpen);

            var ensure = tab.EnsureCoreAsync();
            if (await Task.WhenAny(ensure, Task.Delay(TimeSpan.FromSeconds(4))) != ensure)
            {
                // A hidden page didn't get its browser in time: show it rather than lose the link.
                ActiveTab = tab;
                await ensure;
            }
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

    /// <summary>A page closed itself (window.close(), e.g. a sign-in popup that is done).</summary>
    private void OnTabCloseRequested(object? sender, EventArgs e)
    {
        if (sender is not TabViewModel tab)
            return;

        Log.Info($"Sayfa kendi sekmesini kapattı: {tab.Url}");
        if (Panels.FirstOrDefault(p => ReferenceEquals(p.Tab, tab)) is { } panel)
        {
            UnloadPanel(panel);
            return;
        }

        // A page may close its own tab, never the whole browser: the last tab becomes a new tab.
        if (Tabs.Count == 1 && ReferenceEquals(Tabs[0], tab))
            NewTab();
        CloseTab(tab);
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

    /// <summary>
    /// Background tabs left alone for the configured time are frozen (at most 3 minutes in
    /// performance mode). WebView2's TrySuspend keeps the page in memory as it is, so typed
    /// form text survives; the page just stops running scripts and timers until it is shown.
    /// </summary>
    private void SuspendIdleTabs()
    {
        if (_settings.SleepAfterMinutes <= 0)
            return;

        var minutes = _settings.PerformanceMode ? Math.Min(_settings.SleepAfterMinutes, 3) : _settings.SleepAfterMinutes;
        var limit = TimeSpan.FromMinutes(minutes);
        foreach (var tab in Tabs.Where(t => CanSleep(t) && DateTime.UtcNow - t.LastActiveAt > limit).ToList())
            _ = tab.TrySuspendAsync();
    }

    /// <summary>Not shown, not pinned (unless allowed), not downloading. Audio is checked by the tab itself.</summary>
    private bool CanSleep(TabViewModel tab)
        => !tab.IsActive && !tab.IsShown && !tab.IsSuspended
           && (_settings.SleepPinnedTabs || !tab.IsPinned)
           && !_downloads.HasActiveDownloads(tab.Core);

    public ICommand SleepTabCommand => new RelayCommand(p =>
    {
        if (p is TabViewModel tab)
            SleepTab(tab);
    });

    /// <summary>Puts a background tab to sleep right away (the active one can't sleep).</summary>
    public void SleepTab(TabViewModel tab)
    {
        if (!tab.IsActive && !tab.IsShown && !_downloads.HasActiveDownloads(tab.Core))
            _ = tab.TrySuspendAsync();
    }

    public void SleepOtherTabs()
    {
        foreach (var tab in Tabs.Where(t => !t.IsActive && !t.IsShown && !t.IsSuspended && !_downloads.HasActiveDownloads(t.Core)).ToList())
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
            if (ReferenceEquals(panel, ConverterPanel) || ReferenceEquals(panel, ToolsPanel))
                tab.InternalPageMessage += OnInternalPageMessage;
            return tab;
        });
        ActivePanel = panel;
    }

    private void OnPanelNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        if (!AddressResolver.IsNavigable(e.Uri))
            return;

        e.Handled = true;
        var tab = CreateTab(e.Uri, Tabs.Count);
        if (sender is TabViewModel panelPage && panelPage.ConsumeBackgroundOpen())
            tab.EnsureStarted(); // loads in the background
        else
            ActiveTab = tab;
        SoundService.Play(UiSound.TabOpen);
    }

    /// <summary>Background tabs from one page line up after it in the order they were opened (like Chrome).</summary>
    private int IndexForBackgroundChild(TabViewModel source)
    {
        var index = Tabs.IndexOf(source) + 1;
        while (index < Tabs.Count && ReferenceEquals(Tabs[index].Opener, source))
            index++;
        return index;
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
        HostedPanels.Remove(panel);
        BroadcastToInternalPages(_settingsController.BuildState());
    }

    private void AddPanel(PanelViewModel panel)
    {
        Panels.Add(panel);
        HostedPanels.Add(panel);
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
    private async void OnInternalPageMessage(object? sender, JsonElement message)
    {
        if (sender is not TabViewModel tab)
            return;

        string? Text(string name)
            => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        bool Flag(string name)
            => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

        switch (Text("type"))
        {
            // ----- converter panel
            case { } converterMessage when converterMessage.StartsWith("converter:", StringComparison.Ordinal):
                _converter.Handle(tab, converterMessage, message);
                break;

            // ----- extensions (settings page)
            case { } extensionMessage when extensionMessage.StartsWith("ext:", StringComparison.Ordinal):
                _extensions.Handle(tab, extensionMessage, message);
                break;

            // ----- new tab page
            case "ready":
                tab.PostToInternalPage(BuildNewTabConfig());
                break;

            case "profiles:list":
                tab.PostToInternalPage(BuildProfilesState());
                break;

            case "profiles:create":
                if (ProfileService.Create(Text("name"), Text("emoji"), Text("color")) is null)
                    tab.PostToInternalPage(new { type = "profilesError", message = L.T("Bir ad yaz (en fazla 12 profil olabilir).") });
                RefreshProfiles();
                break;

            case "profiles:update":
                ProfileService.Update(Text("id"), Text("name"), Text("emoji"), Text("color"));
                RefreshProfiles();
                break;

            case "profiles:delete":
                if (!ProfileService.Delete(Text("id")))
                    tab.PostToInternalPage(new { type = "profilesError", message = L.T("Açık olan ya da ana profil silinemez. Önce o profilin penceresini kapat.") });
                RefreshProfiles();
                break;

            case "profiles:open" when Text("id") is { } profileId:
                OpenProfile(profileId);
                break;

            case "taskmgr:ready":
                _taskManager.Start();
                break;

            case "taskmgr:show" when TabById(Text("id")) is { } shown:
                ActiveTab = shown;
                break;

            case "taskmgr:sleep" when TabById(Text("id")) is { } sleeper:
                SleepTab(sleeper);
                break;

            case "taskmgr:close" when TabById(Text("id")) is { } closing && !ReferenceEquals(closing, tab):
                CloseTab(closing);
                break;

            case "tm:list":
                tab.PostToInternalPage(BuildTimeMachineState());
                break;

            case "tm:reopen" when _timeMachine.Take(Text("id")) is { } closedTab:
                RestoreClosed(closedTab);
                break;

            case "tm:openSession":
                OpenArchivedSession(Text("id"), Text("url"));
                break;

            case "tm:removeSession":
                _timeMachine.RemoveSession(Text("id"));
                break;

            case "tm:clear":
                _timeMachine.Clear();
                break;

            case "reader:ready":
                tab.PostToInternalPage(new { type = "article", article = _readerArticles.TryGetValue(tab, out var article) ? article : (JsonElement?)null });
                break;

            case "reader:back":
                if (tab.CanGoBack)
                    tab.GoBack();
                else
                    tab.Navigate(InternalPages.NewTabUrl);
                break;

            case "tools:changelog":
                OpenInternal(InternalPages.ChangelogUrl);
                break;

            case "tools:currentUrl":
                // Only real web pages; Sorune's own pages have nothing worth a QR code.
                var current = ActiveTab?.Url is { } activeUrl && !InternalPages.IsInternal(activeUrl) && AddressResolver.IsNavigable(activeUrl)
                    ? activeUrl
                    : null;
                tab.PostToInternalPage(new { type = "toolsCurrentUrl", url = current });
                break;

            case "whatsnew:open":
                switch (Text("target"))
                {
                    case "converter" when !ConverterPanel.IsOpen:
                        TogglePanel(ConverterPanel);
                        break;
                    case "tools" when !ToolsPanel.IsOpen:
                        TogglePanel(ToolsPanel);
                        break;
                    case "palette":
                        OpenPalette();
                        break;
                    case "newtab":
                        NewTab();
                        break;
                    case "settings":
                        OpenSettings();
                        break;
                    case "import":
                        OpenSettings("ice-aktar");
                        break;
                    case "extensions":
                        OpenSettings("eklentiler");
                        break;
                    case "mascot":
                        OpenSettings("maskot");
                        break;
                    case "newtab-settings":
                        OpenSettings("yeni-sekme");
                        break;
                    case "timemachine":
                        OpenInternal(InternalPages.TimeMachineUrl);
                        break;
                    case "profiles":
                        OpenSettings("profiller");
                        break;
                    case "taskmanager":
                        OpenTaskManager();
                        break;
                    case "userthemes":
                        OpenSettings("temalarim");
                        break;
                    case "backup":
                        OpenSettings("yedek");
                        break;
                    case "verticaltabs":
                        OpenSettings("sekmeler");
                        break;
                    case "sitedark":
                        OpenSettings("duzen");
                        break;
                    case "changelog":
                        OpenInternal(InternalPages.ChangelogUrl);
                        break;
                    case "close":
                        // Becomes a new tab rather than closing (it may be the only tab).
                        tab.Navigate(InternalPages.NewTabUrl);
                        break;
                }
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

            case "saveWidgets" when message.TryGetProperty("widgets", out var widgets):
                var saved = _widgets.Save(widgets);
                // Other open new tabs follow; the sender keeps its own state (and its text cursor).
                foreach (var other in HostedTabs.Where(t => !ReferenceEquals(t, tab)))
                    other.PostToInternalPage(new { type = "widgets", widgets = saved });
                break;

            case "weather" when !string.IsNullOrWhiteSpace(Text("city")):
                var weather = await WeatherService.GetAsync(Text("city")!, L.Language);
                tab.PostToInternalPage(weather);
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

            case "userTheme:save":
                if (!_settingsController.SaveUserTheme(Text("name")))
                    tab.PostToInternalPage(new { type = "userThemeError", message = L.T("Bir ad yaz (en fazla 24 tema kaydedilebilir).") });
                break;

            case "userTheme:apply":
                _settingsController.ApplyUserTheme(Text("id"));
                break;

            case "userTheme:delete":
                _settingsController.DeleteUserTheme(Text("id"));
                break;

            case "backup:create":
                await CreateBackupAsync(tab, Flag("includeHistory"));
                break;

            case "backup:restore":
                if (PickBackupFile?.Invoke() is { } backupFile)
                {
                    var manifest = await Task.Run(() => BackupService.StageRestore(backupFile, out var restoreError) is { } staged
                        ? (object)new { type = "restoreStaged", version = (string?)staged["version"], createdAt = (string?)staged["createdAt"], includesHistory = staged["includesHistory"]?.GetValue<bool>() == true }
                        : new { type = "backupError", message = restoreError });
                    tab.PostToInternalPage(manifest);
                    BroadcastToInternalPages(_settingsController.BuildState());
                }
                break;

            case "backup:cancel":
                BackupService.CancelRestore();
                BroadcastToInternalPages(_settingsController.BuildState());
                break;

            case "pickMascot":
                if (PickMascotFile?.Invoke() is { } mascotFile && !_settingsController.TryImportMascot(mascotFile, out var mascotError))
                    tab.PostToInternalPage(new { type = "mascotError", message = mascotError });
                break;

            case "removeMascot":
                _settingsController.RemoveCustomMascot();
                break;

            case "pickDownloadFolder":
                if (PickFolder?.Invoke(_downloads.Folder) is { } folder)
                    _settingsController.SetDownloadFolder(folder);
                break;

            case "resetDownloadFolder":
                _settingsController.SetDownloadFolder(null);
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
                    AddPanel(new PanelViewModel(added));
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

            case "restartApp":
                RestartRequested?.Invoke(this, EventArgs.Empty);
                break;

            case "checkUpdates":
                _ = UpdateService.CheckAsync();
                break;

            case "applyUpdate":
                ApplyUpdate();
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
        {
            _history.Clear(since);
            _timeMachine.Clear(since);
        }
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
            var source = BrowserImporter.Find(sourceId) ?? throw new InvalidOperationException(L.T("Tarayıcı profili bulunamadı."));
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
                ? L.T("Dosyalar okunamadı. O tarayıcıyı kapatıp tekrar dene.")
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
            OnPropertyChanged(nameof(ToolbarSiteColor));
            OnPropertyChanged(nameof(IsPageFramed));
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
            case "verticalTabs":
                OnPropertyChanged(nameof(IsVerticalTabs));
                break;
            case "verticalTabsWidth":
                OnPropertyChanged(nameof(VerticalTabsWidth));
                break;
            case "downloadFolder":
                _downloads.SetFolder(_settings.DownloadFolder);
                break;
            case "siteColorScheme":
                foreach (var page in HostedTabs.Concat(HostedPanels.Select(p => p.Tab).OfType<TabViewModel>()))
                {
                    if (page.Core is { } pageCore)
                        ApplyColorScheme(pageCore);
                }
                break;
            case "mascotTalks":
                Mascot.TalksEnabled = _settings.MascotTalks;
                if (_settings.MascotTalks)
                    Mascot.Say(L.T("Merhaba! Artık konuşabiliyorum 👋"));
                break;
            case "mascotPeek":
                Mascot.Peek = _settings.MascotPeek;
                break;
            case "mascotCustom":
                Mascot.CustomImage = _settingsController.CustomMascotPath;
                Mascot.Say(L.T("Yeni görünümüm nasıl?"), happy: true);
                break;
            case "soundPack":
                SoundService.Pack = _settings.SoundPack;
                SoundService.Play(UiSound.TabOpen);
                break;
            case "userTheme":
                // A saved look also brings its sounds and new tab settings (the look itself is applied above).
                SoundService.Pack = _settings.SoundPack;
                SoundService.Play(UiSound.Toggle);
                break;
            case { } newTabKey when newTabKey.StartsWith("newTab", StringComparison.Ordinal):
                BroadcastToInternalPages(BuildNewTabConfig());
                break;
            case "restoreSession":
                SessionCookieStore.Enabled = _settings.RestoreSession;
                // The tab list stays on disk as crash insurance; saved cookies go right away.
                if (!_settings.RestoreSession)
                    SessionCookieStore.Delete();
                ScheduleSave();
                break;
        }

        // Feedback: a toggle sound for switches, a sample click at the new volume for the slider.
        if (key is "animationsEnabled" or "mascotEnabled" or "soundsEnabled" or "restoreSession" or "sidebarEnabled" or "performanceMode"
            or "siteTintEnabled" or "pageFrameEnabled" or "sleepPinnedTabs" or "newTabShowSearch" or "newTabShowShortcuts" or "newTabShowWidgets")
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
            widgets = _widgets.Items,
            maxWidgets = WidgetStore.MaxWidgets,
            version = UpdateService.CurrentVersion,
            look = new
            {
                effect = _settings.NewTabEffect,
                dim = _settings.NewTabDim,
                blur = _settings.NewTabBlur,
                align = _settings.NewTabAlign,
                showSearch = _settings.NewTabShowSearch,
                showShortcuts = _settings.NewTabShowShortcuts,
                showWidgets = _settings.NewTabShowWidgets,
            },
        };
    }

    // ---------------------------------------------------------------- session

    /// <summary>Changes are written at most once a second (debounced), not on every event.</summary>
    private void ScheduleSave()
    {
        if (_isShuttingDown)
            return;

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>
    /// Always written, also with "continue previous session" off, so a crash can be recovered;
    /// a normal exit then deletes it again (see <see cref="ShutdownAsync"/>).
    /// </summary>
    private void SaveSession() => SessionStore.Save(BuildSessionState());

    private async Task CreateBackupAsync(TabViewModel page, bool includeHistory)
    {
        var profileName = ProfileService.IsDefault ? "" : string.Concat(CurrentProfile.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))) + "-";
        var suggested = $"Sorune-{profileName}{DateTime.Now:yyyy-MM-dd}{BackupService.Extension}";
        if (PickBackupTarget?.Invoke(suggested) is not { } target)
            return;

        // Everything that is only in memory goes to disk first, so the backup is current.
        _history.Flush();
        SaveSession();
        var extensions = page.Core?.Profile is { } profile
            ? (await ExtensionService.ListAsync(profile)).Select(e => e.Name).ToList()
            : [];

        page.PostToInternalPage(new { type = "backupBusy" });
        try
        {
            var summary = await Task.Run(() => BackupService.Create(target, includeHistory, extensions));
            page.PostToInternalPage(new { type = "backupDone", path = target, files = summary.Files, bytes = summary.Bytes });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Yedek oluşturulamadı: {target}", ex);
            page.PostToInternalPage(new { type = "backupError", message = L.T("Yedek kaydedilemedi: {0}", ex.Message) });
        }
    }

    private SessionState BuildSessionState()
    {
        // InPrivate tabs are never written to disk.
        var saved = Tabs.Where(t => !t.IsPrivate).ToList();
        return new SessionState
        {
            Tabs = saved.Select(t => new SessionTab
            {
                Url = t.Url ?? InternalPages.NewTabUrl,
                Title = t.Title,
                IsPinned = t.IsPinned,
                GroupId = t.Group?.Id,
                Volume = t.Volume,
            }).ToList(),
            ActiveIndex = ActiveTab is null ? 0 : Math.Max(0, saved.IndexOf(ActiveTab)),
            Groups = Groups.Select(g => new SessionGroup { Id = g.Id, Name = g.Name, Color = g.ColorKey, IsCollapsed = g.IsCollapsed }).ToList(),
            SplitLeft = SplitLeft is { } left ? saved.IndexOf(left) : -1,
            SplitRight = SplitRight is { } right ? saved.IndexOf(right) : -1,
            SplitRatio = SplitRatio,
        };
    }
}
