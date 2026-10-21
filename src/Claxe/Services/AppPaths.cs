using System.IO;

namespace Claxe.Services;

/// <summary>
/// All user data lives under %LOCALAPPDATA%\Sorune, never inside the repository.
/// Setting SORUNE_PROFILE_DIR (or the older CLAXE_PROFILE_DIR) points the app at a separate
/// profile, used for testing so test runs never touch the real tabs, cookies or settings.
/// </summary>
public static class AppPaths
{
    public static string DataRoot { get; } = ResolveDataRoot();

    /// <summary>
    /// The browser was called Claxe before: its data folder is moved to the new name once.
    /// If the move fails (a file in use), the old folder keeps being used, so nothing is lost.
    /// </summary>
    private static string ResolveDataRoot()
    {
        foreach (var variable in new[] { "SORUNE_PROFILE_DIR", "CLAXE_PROFILE_DIR" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } custom)
                return Path.GetFullPath(custom);
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var current = Path.Combine(local, "Sorune");
        var legacy = Path.Combine(local, "Claxe");
        if (Directory.Exists(current) || !Directory.Exists(legacy))
            return current;

        try
        {
            Directory.Move(legacy, current);
            return current;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return legacy;
        }
    }

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
