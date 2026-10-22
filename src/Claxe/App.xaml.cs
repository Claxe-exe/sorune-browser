using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
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

        // Already running: hand the links over and quit, so there is only ever one window.
        _instance = SingleInstance.TryAcquire();
        if (_instance is null && SingleInstance.SendToRunning(args))
        {
            Shutdown(0);
            return;
        }

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
        ThemeService.Apply(settings);

        // One click sound for every button in the app (toolbar, tabs, caption buttons).
        EventManager.RegisterClassHandler(typeof(ButtonBase), ButtonBase.ClickEvent,
            new RoutedEventHandler((_, _) => SoundService.Play(UiSound.Click)));

        var viewModel = new MainViewModel(settings);
        viewModel.RestartRequested += (_, _) => Restart();
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
        viewModel.Start(args);

        // Quiet update check once things have settled (only when installed via Setup.exe).
        var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        updateTimer.Tick += async (_, _) =>
        {
            updateTimer.Stop();
            await UpdateService.CheckAsync();
        };
        updateTimer.Start();

        _instance?.Listen(links => Dispatcher.BeginInvoke(() =>
        {
            if (viewModel.OpenExternal(links) == 0)
                viewModel.NewTab();
            window.BringToFront();
        }));
    }

    /// <summary>Closes normally (session, cookies and history are saved), then starts again.</summary>
    public void Restart()
    {
        _restartRequested = true;
        MainWindow?.Close();
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
