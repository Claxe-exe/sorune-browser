using System.Diagnostics;
using Microsoft.Win32;

namespace Claxe.Services;

/// <summary>
/// Registers Sorune as a browser for the current Windows user (HKCU only, no admin rights).
/// Windows 10/11 don't let apps make themselves the default: after registering, the user
/// picks Sorune in Settings › Default apps, which this opens.
/// </summary>
public static class DefaultBrowser
{
    private const string Name = "Sorune";
    private const string AppKey = @"Software\Clients\StartMenuInternet\" + Name;
    private const string ProgId = "SoruneHTML";

    /// <summary>Registration left by the browser's former name; removed whenever we (un)register.</summary>
    private const string LegacyName = "Claxe";

    private static readonly string[] Schemes = ["http", "https"];
    private static readonly string[] Extensions = [".htm", ".html", ".shtml", ".xht", ".xhtml", ".svg"];

    /// <summary>The exe Windows would start, or null when Sorune isn't registered.</summary>
    public static string? RegisteredExe
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppKey + @"\shell\open\command");
            return (key?.GetValue("") as string)?.Trim('"');
        }
    }

    public static void Register()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Sorune'nin yolu bulunamadı.");
        var icon = $"\"{exe}\",0";
        RemoveRegistration(LegacyName, "ClaxeHTML");

        Set(AppKey, "", Name);
        Set(AppKey + @"\DefaultIcon", "", icon);
        Set(AppKey + @"\shell\open\command", "", $"\"{exe}\"");
        Set(AppKey + @"\Capabilities", "ApplicationName", Name);
        Set(AppKey + @"\Capabilities", "ApplicationDescription", L.T("Anime temalı kişisel tarayıcı"));
        Set(AppKey + @"\Capabilities", "ApplicationIcon", icon);
        foreach (var scheme in Schemes)
            Set(AppKey + @"\Capabilities\URLAssociations", scheme, ProgId);
        foreach (var extension in Extensions)
            Set(AppKey + @"\Capabilities\FileAssociations", extension, ProgId);
        Set(@"Software\RegisteredApplications", Name, AppKey + @"\Capabilities");

        var progKey = @"Software\Classes\" + ProgId;
        Set(progKey, "", "Sorune HTML Belgesi");
        Set(progKey + @"\DefaultIcon", "", icon);
        Set(progKey + @"\shell\open\command", "", $"\"{exe}\" \"%1\"");
        Set(progKey + @"\Application", "ApplicationName", Name);
        Set(progKey + @"\Application", "ApplicationIcon", icon);

        Log.Info($"Sorune tarayıcı olarak kaydedildi: {exe}");
    }

    public static void Unregister()
    {
        RemoveRegistration(Name, ProgId);
        RemoveRegistration(LegacyName, "ClaxeHTML");
        Log.Info("Sorune tarayıcı kaydı kaldırıldı.");
    }

    /// <summary>Settings › Apps › Default apps, scrolled to Sorune.</summary>
    public static void OpenWindowsSettings()
        => Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=" + Name) { UseShellExecute = true });

    private static void RemoveRegistration(string name, string progId)
    {
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Clients\StartMenuInternet\" + name, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + progId, throwOnMissingSubKey: false);
        using var apps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true);
        apps?.DeleteValue(name, throwOnMissingValue: false);
    }

    private static void Set(string path, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue(name, value);
    }
}
