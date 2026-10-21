using System.Collections.ObjectModel;
using System.ComponentModel;
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

    private static readonly HashSet<string> AppearanceKeys =
        ["theme", "wallpaper", "accentColor", "chromeOpacity", "cornerStyle", "density", "animationsEnabled"];

    private readonly AppSettings _settings;
    private readonly SettingsController _settingsController;
    private readonly ShortcutStore _shortcuts = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly List<string> _closedTabs = [];
    private TabViewModel? _activeTab;
    private bool _isShuttingDown;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
        _settingsController = new SettingsController(settings);
        _settingsController.Changed += OnSettingChanged;
        Mascot = new MascotViewModel(settings.MascotEnabled);
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveSession();
        };

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
        OpenSettingsCommand = new RelayCommand(OpenSettings);
        RevertAddressCommand = new RelayCommand(() =>
        {
            ActiveTab?.RevertAddress();
            FocusContentRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Tabs in strip order (reorderable).</summary>
    public ObservableCollection<TabViewModel> Tabs { get; } = [];

    /// <summary>
    /// The same tabs in creation order, only ever added to or removed from. The view
    /// hosts WebView2 controls from this list, so reordering tabs never re-parents
    /// a live browser window.
    /// </summary>
    public ObservableCollection<TabViewModel> HostedTabs { get; } = [];

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

    public event EventHandler? FocusAddressBarRequested;
    public event EventHandler? FocusContentRequested;
    public event EventHandler? CloseWindowRequested;

    /// <summary>Layout or motion settings changed; the view re-fits sizes that aren't resource-bound.</summary>
    public event EventHandler? AppearanceChanged;

    /// <summary>The settings page asked for a wallpaper file; the view shows the file picker.</summary>
    public event EventHandler? WallpaperPickRequested;

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
            CommandManager.InvalidateRequerySuggested();
            ScheduleSave();
        }
    }

    public string WindowTitle => ActiveTab is { } tab ? $"{tab.Title} - Claxe" : "Claxe";

    public bool IsContentFullScreen => ActiveTab?.IsContentFullScreen == true;

    public bool IsActiveTabLoading => ActiveTab?.IsLoading == true;

    public SearchEngine SearchEngine => SearchEngines.Find(_settings.SearchEngine);

    /// <summary>Called once the window is shown (WebView2 needs a live window handle).</summary>
    public void Start()
    {
        if (_settings.RestoreSession && SessionStore.Load() is { Tabs.Count: > 0 } session)
        {
            foreach (var saved in session.Tabs.Where(t => AddressResolver.IsNavigable(t.Url)))
                CreateTab(saved.Url, Tabs.Count, saved.Title);

            if (Tabs.Count > 0)
            {
                ActiveTab = Tabs[Math.Clamp(session.ActiveIndex, 0, Tabs.Count - 1)];
                Log.Info($"Oturum geri yüklendi: {Tabs.Count} sekme.");
                return;
            }
        }

        NewTab();
    }

    public void NewTab()
    {
        ActiveTab = CreateTab(InternalPages.NewTabUrl, Tabs.Count);
        SoundService.Play(UiSound.TabOpen);
        FocusAddressBarRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Switches to the settings tab, opening one if needed.</summary>
    public void OpenSettings()
    {
        if (Tabs.FirstOrDefault(t => InternalPages.IsSettings(t.Url)) is { } existing)
        {
            ActiveTab = existing;
            return;
        }

        ActiveTab = CreateTab(InternalPages.SettingsUrl, Tabs.Count);
        SoundService.Play(UiSound.TabOpen);
    }

    /// <summary>Called by the view after the user picked a file for the wallpaper.</summary>
    public void ImportWallpaper(string path)
    {
        if (!_settingsController.TryImportWallpaper(path, out var error))
            BroadcastToInternalPages(new { type = "wallpaperError", message = error });
    }

    public void CloseTab(TabViewModel? tab)
    {
        if (tab is null)
            return;

        var index = Tabs.IndexOf(tab);
        if (index < 0)
            return;

        if (tab.Url is { } url && !InternalPages.IsNewTab(url))
        {
            _closedTabs.Add(url);
            if (_closedTabs.Count > MaxClosedTabs)
                _closedTabs.RemoveAt(0);
        }

        // Pick the neighbour before removing so the strip doesn't jump.
        if (ReferenceEquals(tab, ActiveTab) && Tabs.Count > 1)
            ActiveTab = Tabs[index + 1 < Tabs.Count ? index + 1 : index - 1];

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
        newIndex = Math.Clamp(newIndex, 0, Tabs.Count - 1);
        if (oldIndex < 0 || oldIndex == newIndex)
            return;

        Tabs.Move(oldIndex, newIndex);
        ScheduleSave();
    }

    public void Reload() => ActiveTab?.Reload();

    public void FocusAddressBar() => FocusAddressBarRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Flushes the session and releases every WebView2. Called when the window closes.</summary>
    public void Shutdown()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;
        _saveTimer.Stop();
        SaveSession();

        foreach (var tab in HostedTabs.ToList())
        {
            Detach(tab);
            tab.Dispose();
        }

        HostedTabs.Clear();
        Tabs.Clear();
    }

    private TabViewModel CreateTab(string? url, int index, string? title = null)
    {
        var tab = new TabViewModel(url, title);
        tab.PropertyChanged += OnTabPropertyChanged;
        tab.NewWindowRequested += OnNewWindowRequested;
        tab.CloseRequested += OnTabCloseRequested;
        tab.InternalPageMessage += OnInternalPageMessage;
        tab.NavigationFinished += OnNavigationFinished;

        Tabs.Insert(Math.Clamp(index, 0, Tabs.Count), tab);
        HostedTabs.Add(tab);
        ScheduleSave();
        return tab;
    }

    private void Detach(TabViewModel tab)
    {
        tab.PropertyChanged -= OnTabPropertyChanged;
        tab.NewWindowRequested -= OnNewWindowRequested;
        tab.CloseRequested -= OnTabCloseRequested;
        tab.InternalPageMessage -= OnInternalPageMessage;
        tab.NavigationFinished -= OnNavigationFinished;
    }

    private void OnNavigationFinished(object? sender, bool success)
    {
        if (!ReferenceEquals(sender, ActiveTab))
            return;

        // Opening a new tab is not worth a celebration; real pages are.
        if (InternalPages.IsInternal(ActiveTab?.Url))
            Mascot.NoteActivity();
        else
            Mascot.OnPageLoaded(success);
    }

    /// <summary>Requests from Claxe's own pages. The tab already verified the sender is internal.</summary>
    private void OnInternalPageMessage(object? sender, JsonElement message)
    {
        if (sender is not TabViewModel tab)
            return;

        string? Text(string name)
            => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        switch (Text("type"))
        {
            case "ready":
                tab.PostToInternalPage(BuildNewTabConfig());
                break;

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

            case "navigate" when !string.IsNullOrWhiteSpace(Text("text")):
                tab.Navigate(AddressResolver.Resolve(Text("text")!, SearchEngine));
                FocusContentRequested?.Invoke(this, EventArgs.Empty);
                break;

            case "addShortcut":
                var added = _shortcuts.Add(Text("title"), Text("url"));
                if (!added)
                    tab.PostToInternalPage(new { type = "shortcutRejected" });
                BroadcastShortcuts();
                break;

            case "removeShortcut":
                _shortcuts.Remove(Text("url"));
                BroadcastShortcuts();
                break;
        }
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
            Motion.Enabled = _settings.AnimationsEnabled;
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
            case "restoreSession":
                if (_settings.RestoreSession)
                    ScheduleSave();
                else
                    SessionStore.Delete();
                break;
        }

        // Feedback: a toggle sound for switches, a sample click at the new volume for the slider.
        if (key is "animationsEnabled" or "mascotEnabled" or "soundsEnabled" or "restoreSession")
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
            animations = _settings.AnimationsEnabled,
            searchEngine = SearchEngine.Name,
            shortcuts = _shortcuts.Items,
            maxShortcuts = ShortcutStore.MaxShortcuts,
        };
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var tab = (TabViewModel)sender!;
        if (e.PropertyName is nameof(TabViewModel.Url) or nameof(TabViewModel.Title))
            ScheduleSave();

        if (!ReferenceEquals(tab, ActiveTab))
            return;

        switch (e.PropertyName)
        {
            case nameof(TabViewModel.Title):
                OnPropertyChanged(nameof(WindowTitle));
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
            var tab = CreateTab(url: null, Tabs.IndexOf(source) + 1);
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

    private void OnTabCloseRequested(object? sender, EventArgs e) => CloseTab(sender as TabViewModel);

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
        if (ActiveTab is not { } tab || string.IsNullOrWhiteSpace(tab.AddressText))
            return;

        tab.Navigate(AddressResolver.Resolve(tab.AddressText, SearchEngine));
        FocusContentRequested?.Invoke(this, EventArgs.Empty);
    }

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

        var state = new SessionState
        {
            Tabs = Tabs.Select(t => new SessionTab { Url = t.Url ?? InternalPages.NewTabUrl, Title = t.Title }).ToList(),
            ActiveIndex = ActiveTab is null ? 0 : Math.Max(0, Tabs.IndexOf(ActiveTab)),
        };
        SessionStore.Save(state);
    }
}
