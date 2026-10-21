using System.Windows;
using System.Windows.Threading;
using Claxe.Services;
using Claxe.ViewModels;
using Claxe.Views;

namespace Claxe;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureCreated();
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        if (!BrowserEnvironment.IsRuntimeAvailable(out var version))
        {
            MessageBox.Show(
                "Claxe'in çalışması için Microsoft Edge WebView2 Runtime gerekiyor ama bu bilgisayarda bulunamadı.\n\n" +
                "Microsoft'un resmi sitesinden 'WebView2 Runtime' (Evergreen) kurup tekrar deneyin.",
                "Claxe", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        Log.Info($"Claxe başlıyor. WebView2 Runtime {version}");

        var viewModel = new MainViewModel(SettingsStore.Load());
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
        viewModel.Start();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Logged, then left unhandled on purpose: the app must not continue in an unknown state.
        Log.Error("Beklenmeyen hata", e.Exception);
        MessageBox.Show(
            $"Beklenmeyen bir hata oluştu ve Claxe kapanacak.\n\n{e.Exception.Message}\n\nAyrıntılar: {AppPaths.LogFile}",
            "Claxe", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
