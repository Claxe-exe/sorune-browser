using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Claxe.Models;
using Claxe.Services;
using Claxe.ViewModels;
using Claxe.Views;

namespace Claxe;

public partial class App : Application
{
    /// <summary>Passed to the new process by <see cref="Restart"/>: wait for this process id to exit first.</summary>
    private const string RestartAfterArgument = "--restart-after";

    private SingleInstance? _instance;
    private bool _restartRequested;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = WaitForPreviousInstance(e.Args);
        AppPaths.EnsureCreated();
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Already running: hand the links over and quit, so one Sorune runs (it opens the windows).
        _instance = SingleInstance.TryAcquire();
        if (_instance is null && SingleInstance.SendToRunning(args))
        {
            Shutdown(0);
            return;
        }

        // A restore chosen last time is put in place before anything reads the profile.
        BackupService.ApplyPendingRestore();
        var settings = SettingsStore.Load();
        L.Load(settings.Language);
        SessionStore.MarkRunning();

        if (!BrowserEnvironment.IsRuntimeAvailable(out var version))
        {
            MessageBox.Show(
                L.T("Sorune'nin çalışması için Microsoft Edge WebView2 Runtime gerekiyor ama bu bilgisayarda bulunamadı.\n\nMicrosoft'un resmi sitesinden 'WebView2 Runtime' (Evergreen) kurup tekrar deneyin."),
                "Sorune", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Log.Info($"Sorune başlıyor. WebView2 Runtime {version}, dil {L.Language}");

        Motion.Enabled = settings.AnimationsEnabled && !settings.PerformanceMode;
        SessionCookieStore.Enabled = settings.RestoreSession;
        SoundService.Enabled = settings.SoundsEnabled;
        SoundService.Volume = settings.SoundVolume;
        SoundService.Pack = settings.SoundPack;
        BrowserEnvironment.ForceDark = settings.ForceDarkSites;
        SecureDns.Current = SecureDns.Find(settings.DnsProvider);
        if (settings.SecureDns)
        {
            try { BrowserEnvironment.SecureDnsPort = SecureDns.Start(); }
            catch (Exception ex) { Log.Error("Güvenli DNS başlatılamadı", ex); }
        }
        BrowserEnvironment.SmartScreen = settings.SmartScreenEnabled;
        ThemeService.Apply(settings);

        // One click sound for every button in the app (toolbar, tabs, caption buttons).
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent,
            new RoutedEventHandler((_, _) => SoundService.Play(UiSound.Click)));

        _settings = settings;
        BrowserWindows.Open = OpenWindow;
        BrowserWindows.CloseAll = CloseAllWindows;
        var viewModel = CreateWindow(null, maximized: false);
        viewModel.Start(args);

        // Quiet update check once things have settled (only when installed via Setup.exe).
        var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Stop();
            await UpdateService.CheckAsync();
        };
        // Only the main profile updates: an update replaces the files every profile window runs from.
        if (ProfileService.IsDefault)
            updateTimer.Start();

        // Links from other apps go to the window used last.
        _instance?.Listen(links => Dispatcher.BeginInvoke(() =>
        {
            if (BrowserWindows.Lead is not { } target || WindowOf(target) is not { } window)
                return;
            if (target.OpenExternal(links) == 0)
                target.NewTab();
            window.BringToFront();
        }));
    }

    private AppSettings? _settings;

    /// <summary>A browser window and its view model; the app ends when the last one has closed.</summary>
    private MainViewModel CreateWindow(Rect? bounds, bool maximized)
    {
        var viewModel = new MainViewModel(_settings!);
        viewModel.RestartRequested += (_, _) => Restart();
        var window = new MainWindow(viewModel);
        BrowserWindows.Register(viewModel);
        if (bounds is { } place && IsOnScreen(place))
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = place.Left;
            window.Top = place.Top;
            window.Width = place.Width;
            window.Height = place.Height;
        }
        else if (MainWindow is MainWindow { IsVisible: true } previous)
        {
            // Next to the window it came from, like a new window in Chrome.
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = previous.Left + 32;
            window.Top = previous.Top + 32;
            window.Width = previous.ActualWidth;
            window.Height = previous.ActualHeight;
        }

        window.Closed += (_, _) =>
        {
            var next = Windows.OfType<MainWindow>().FirstOrDefault(w => !ReferenceEquals(w, window));
            if (next is null)
                Shutdown();
            else if (ReferenceEquals(MainWindow, window))
                MainWindow = next;
        };
        MainWindow ??= window;
        if (maximized)
            window.WindowState = WindowState.Maximized;
        window.Show();
        return viewModel;
    }

    /// <summary>For <see cref="BrowserWindows.Open"/>: a saved window, a tab moved out of another window, or a new tab.</summary>
    private MainViewModel OpenWindow(SessionState? state, TabViewModel? tab, Rect? bounds)
    {
        var viewModel = CreateWindow(bounds, state?.IsMaximized == true);
        viewModel.StartWindow(state, tab);
        WindowOf(viewModel)?.Activate();
        return viewModel;
    }

    private MainWindow? WindowOf(MainViewModel viewModel)
        => Windows.OfType<MainWindow>().FirstOrDefault(w => ReferenceEquals(w.DataContext, viewModel));

    /// <summary>A saved position is used only while part of it is still on a screen (monitors change).</summary>
    private static bool IsOnScreen(Rect place)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return place.Width >= 300 && place.Height >= 200 && screen.IntersectsWith(new Rect(place.Left, place.Top, place.Width, 40));
    }

    /// <summary>
    /// Restart and update: everything (every window's tabs, cookies, history) is saved once,
    /// then all windows close and the app ends.
    /// </summary>
    private async void CloseAllWindows()
    {
        if (BrowserWindows.AppClosing)
            return;

        if (BrowserWindows.All.FirstOrDefault() is { } first)
        {
            try
            {
                await first.FlushForExitAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Kapanırken kayıt hatası", ex);
            }
        }

        BrowserWindows.AppClosing = true;
        foreach (var window in Windows.OfType<MainWindow>().ToList())
            window.CloseWindow();
    }

    /// <summary>Closes normally (session, cookies and history are saved), then starts again.</summary>
    public void Restart()
    {
        _restartRequested = true;
        Log.Info("Yeniden başlatılıyor.");
        CloseAllWindows();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SessionStore.MarkCleanExit();
        _instance?.Dispose();
        if (_restartRequested && Environment.ProcessPath is { } exe)
            Process.Start(new ProcessStartInfo(exe, $"{RestartAfterArgument} {Environment.ProcessId}") { UseShellExecute = false });
        base.OnExit(e);
    }

    /// <summary>
    /// After a restart, waits until the old process (and with it its browser process) is gone,
    /// so the single-instance lock and the WebView2 profile are free. Returns the other arguments.
    /// </summary>
    private static string[] WaitForPreviousInstance(string[] args)
    {
        var index = Array.IndexOf(args, RestartAfterArgument);
        if (index < 0 || index + 1 >= args.Length)
            return args;

        if (int.TryParse(args[index + 1], out var pid))
        {
            try
            {
                using var previous = Process.GetProcessById(pid);
                previous.WaitForExit(15000);
            }
            catch (ArgumentException)
            {
                // Already gone.
            }
            Thread.Sleep(1000);
        }

        return args.Where((_, i) => i != index && i != index + 1).ToArray();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Logged, then left unhandled on purpose: the app must not continue in an unknown state.
        Log.Error("Beklenmeyen hata", e.Exception);
        MessageBox.Show(
            L.T("Beklenmeyen bir hata oluştu ve Sorune kapanacak.\n\n{0}\n\nAyrıntılar: {1}", e.Exception.Message, AppPaths.LogFile),
            "Sorune", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
