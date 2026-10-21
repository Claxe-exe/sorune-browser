using System.Diagnostics;
using Microsoft.Win32;

namespace Claxe.Services;

/// <summary>
/// Registers Claxe as a browser for the current Windows user (HKCU only, no admin rights).
/// Windows 10/11 don't let apps make themselves the default: after registering, the user
/// picks Claxe in Settings › Default apps, which this opens.
/// </summary>
public static class DefaultBrowser
{
    private const string AppKey = @"Software\Clients\StartMenuInternet\Claxe";
    private const string ProgId = "ClaxeHTML";
    private static readonly string[] Schemes = ["http", "https"];
    private static readonly string[] Extensions = [".htm", ".html", ".shtml", ".xht", ".xhtml", ".svg"];

    /// <summary>The exe Windows would start, or null when Claxe isn't registered.</summary>
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
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Claxe'in yolu bulunamadı.");
        var icon = $"\"{exe}\",0";

        Set(AppKey, "", "Claxe");
        Set(AppKey + @"\DefaultIcon", "", icon);
        Set(AppKey + @"\shell\open\command", "", $"\"{exe}\"");
        Set(AppKey + @"\Capabilities", "ApplicationName", "Claxe");
        Set(AppKey + @"\Capabilities", "ApplicationDescription", "Anime temalı kişisel tarayıcı");
        Set(AppKey + @"\Capabilities", "ApplicationIcon", icon);
        foreach (var scheme in Schemes)
            Set(AppKey + @"\Capabilities\URLAssociations", scheme, ProgId);
        foreach (var extension in Extensions)
            Set(AppKey + @"\Capabilities\FileAssociations", extension, ProgId);
        Set(@"Software\RegisteredApplications", "Claxe", AppKey + @"\Capabilities");

        var progKey = @"Software\Classes\" + ProgId;
        Set(progKey, "", "Claxe HTML Belgesi");
        Set(progKey + @"\DefaultIcon", "", icon);
        Set(progKey + @"\shell\open\command", "", $"\"{exe}\" \"%1\"");
        Set(progKey + @"\Application", "ApplicationName", "Claxe");
        Set(progKey + @"\Application", "ApplicationIcon", icon);

        Log.Info($"Claxe tarayıcı olarak kaydedildi: {exe}");
    }

    public static void Unregister()
    {
        Registry.CurrentUser.DeleteSubKeyTree(AppKey, throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\" + ProgId, throwOnMissingSubKey: false);
        using var apps = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true);
        apps?.DeleteValue("Claxe", throwOnMissingValue: false);
        Log.Info("Claxe tarayıcı kaydı kaldırıldı.");
    }

    /// <summary>Settings › Apps › Default apps, scrolled to Claxe.</summary>
    public static void OpenWindowsSettings()
        => Process.Start(new ProcessStartInfo("ms-settings:defaultapps?registeredAppUser=Claxe") { UseShellExecute = true });

    private static void Set(string path, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue(name, value);
    }
}
