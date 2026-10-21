using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Threading;
using Claxe.Core;
using Claxe.Models;
using Claxe.Services;
using Microsoft.Web.WebView2.Core;

namespace Claxe.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private const int MaxClosedTabs = 25;

    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer;
    private readonly List<string> _closedTabs = [];
    private TabViewModel? _activeTab;
    private bool _isShuttingDown;

    public MainViewModel(AppSettings settings)
    {
        _settings = settings;
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

    public ICommand NewTabCommand { get; }
    public ICommand CloseTabCommand { get; }
    public ICommand ActivateTabCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ForwardCommand { get; }
    public ICommand ReloadOrStopCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand RevertAddressCommand { get; }

    public event EventHandler? FocusAddressBarRequested;
    public event EventHandler? FocusContentRequested;
    public event EventHandler? CloseWindowRequested;

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

            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(IsContentFullScreen));
            CommandManager.InvalidateRequerySuggested();
            ScheduleSave();
        }
    }

    public string WindowTitle => ActiveTab is { } tab ? $"{tab.Title} - Claxe" : "Claxe";

    public bool IsContentFullScreen => ActiveTab?.IsContentFullScreen == true;

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
        FocusAddressBarRequested?.Invoke(this, EventArgs.Empty);
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
            case nameof(TabViewModel.CanGoBack):
            case nameof(TabViewModel.CanGoForward):
            case nameof(TabViewModel.IsLoading):
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
