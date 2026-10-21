using System.IO;

namespace Claxe.Services;

/// <summary>
/// All user data lives under %LOCALAPPDATA%\Claxe, never inside the repository.
/// Setting CLAXE_PROFILE_DIR points the app at a separate profile (used for testing,
/// so test runs never touch the real tabs, cookies or settings).
/// </summary>
public static class AppPaths
{
    public static string DataRoot { get; } =
        Environment.GetEnvironmentVariable("CLAXE_PROFILE_DIR") is { Length: > 0 } custom
            ? Path.GetFullPath(custom)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claxe");

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
