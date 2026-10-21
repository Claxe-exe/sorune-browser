using System.IO;

namespace Claxe.Services;

/// <summary>
/// All user data lives under %LOCALAPPDATA%\Claxe, never inside the repository.
/// </summary>
public static class AppPaths
{
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claxe");

    public static string WebViewData => Path.Combine(DataRoot, "WebView2");
    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");
    public static string SessionFile => Path.Combine(DataRoot, "session.json");
    public static string LogFile => Path.Combine(DataRoot, "logs", "claxe.log");

    /// <summary>Read-only pages shipped with the app (new tab, etc.).</summary>
    public static string PagesFolder => Path.Combine(AppContext.BaseDirectory, "Pages");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(LogFile)!);
    }
}
