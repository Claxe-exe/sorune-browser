using System.Windows;
using System.Windows.Threading;
using Claxe.Models;
using Claxe.Services;

namespace Claxe.ViewModels;

/// <summary>
/// Every open browser window of this profile. Each window has its own tabs; history, bookmarks,
/// downloads, settings and the rest are shared (<see cref="SharedStores"/>). The session is one
/// file for all of them: the first window, with the others in <see cref="SessionState.Windows"/>.
/// </summary>
public static class BrowserWindows
{
    private static readonly DispatcherTimer SaveTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private static MainViewModel? _lastActive;

    static BrowserWindows()
    {
        SaveTimer.Tick += (_, _) =>
        {
            SaveTimer.Stop();
            SaveNow();
        };
    }

    /// <summary>Open windows, oldest first.</summary>
    public static List<MainViewModel> All { get; } = [];

    /// <summary>The window used last: links from other apps and app-wide messages go there.</summary>
    public static MainViewModel? Lead
    {
        get => _lastActive is not null && All.Contains(_lastActive) ? _lastActive : All.FirstOrDefault();
        set => _lastActive = value;
    }

    /// <summary>The whole app is closing (restart, update): windows only let go of their tabs.</summary>
    public static bool AppClosing { get; set; }

    /// <summary>
    /// Opens a window (set by the app): with a saved window's tabs, with a live tab taken out of
    /// another window, or with a new tab. The bounds (screen DIPs) place it; null lets the app choose.
    /// </summary>
    public static Func<SessionState?, TabViewModel?, Rect?, MainViewModel>? Open { get; set; }

    /// <summary>Closes every window, saving once for all of them (restart, update); set by the app.</summary>
    public static Action? CloseAll { get; set; }

    public static void Register(MainViewModel window) => All.Add(window);

    public static void Unregister(MainViewModel window)
    {
        All.Remove(window);
        if (ReferenceEquals(_lastActive, window))
            _lastActive = null;
        ScheduleSave();
    }

    /// <summary>Changes are written at most once a second (debounced), whichever window made them.</summary>
    public static void ScheduleSave()
    {
        if (AppClosing || All.Count == 0)
            return;

        SaveTimer.Stop();
        SaveTimer.Start();
    }

    /// <summary>
    /// Always written, also with "continue previous session" off, so a crash can be recovered;
    /// a normal exit then deletes it again.
    /// </summary>
    public static void SaveNow()
    {
        SaveTimer.Stop();
        if (All.Count > 0)
            SessionStore.Save(BuildSession());
    }

    public static SessionState BuildSession()
    {
        var state = All[0].BuildSessionState();
        state.Windows = All.Skip(1).Select(w => w.BuildSessionState()).Where(s => s.Tabs.Count > 0).ToList();
        return state;
    }

    /// <summary>
    /// Moves a live tab, page and all, into another window (<paramref name="to"/>), or into a new
    /// window at <paramref name="bounds"/> when that is null.
    /// </summary>
    public static void MoveTab(MainViewModel from, TabViewModel tab, MainViewModel? to, int index, Rect? bounds)
    {
        if (ReferenceEquals(from, to) || !from.Tabs.Contains(tab))
            return;

        // The page leaves its old window before it joins the new one (a browser window can have one parent).
        tab.BeginMove();
        from.ReleaseTab(tab);
        if (to is null)
            Open?.Invoke(null, tab, bounds);
        else
            to.AdoptTab(tab, index);
        tab.EndMove();
        SoundService.Play(UiSound.TabOpen);
        ScheduleSave();
    }
}

/// <summary>What every window of the profile uses together (one copy, loaded once).</summary>
public static class SharedStores
{
    public static readonly ShortcutStore Shortcuts = new();
    public static readonly HistoryStore History = new();
    public static readonly BookmarkStore Bookmarks = new();
    public static readonly DownloadManager Downloads = new();
    public static readonly PermissionStore Permissions = new();
    public static readonly PanelStore Panels = new();
    public static readonly WidgetStore Widgets = new();
    public static readonly TimeMachineStore TimeMachine = new();
    public static readonly SiteStyleStore SiteStyles = new();

    private static SettingsController? _settings;

    public static SettingsController Settings(AppSettings settings)
        => _settings ??= new SettingsController(settings, Permissions, Panels);
}
