using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

/// <summary>
/// Claxe's own pages are served from the read-only Pages folder through a WebView2
/// virtual host; theme images and the mascot come from the Assets folder, and the user's
/// own wallpapers from the profile, each on a separate host. ".internal" is reserved for
/// private use, so it never collides with a real site.
/// </summary>
public static class InternalPages
{
    public const string Host = "claxe.internal";
    public const string Origin = "https://" + Host + "/";
    public const string NewTabUrl = Origin + "newtab.html";
    public const string SettingsUrl = Origin + "settings.html";

    public const string AssetsHost = "assets.claxe.internal";
    public const string AssetsOrigin = "https://" + AssetsHost + "/";

    public const string WallpapersHost = "wallpapers.claxe.internal";
    public const string WallpapersOrigin = "https://" + WallpapersHost + "/";

    /// <summary>Friendly addresses shown in (and accepted by) the address bar.</summary>
    private static readonly (string Friendly, string Url)[] FriendlyNames =
    [
        ("claxe://ayarlar", SettingsUrl),
        ("claxe://settings", SettingsUrl),
        ("claxe://yenisekme", NewTabUrl),
        ("claxe://newtab", NewTabUrl),
    ];

    public static bool IsInternal(string? url)
        => url?.StartsWith(Origin, StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsNewTab(string? url) => IsPage(url, NewTabUrl);

    public static bool IsSettings(string? url) => IsPage(url, SettingsUrl);

    /// <summary>What the address bar shows for a URL (empty for the new tab page).</summary>
    public static string ToDisplayAddress(string? url)
    {
        if (url is null || IsNewTab(url))
            return "";

        return IsSettings(url) ? "claxe://ayarlar" : url;
    }

    public static bool TryResolveFriendly(string input, out string url)
    {
        foreach (var (friendly, target) in FriendlyNames)
        {
            if (string.Equals(input.TrimEnd('/'), friendly, StringComparison.OrdinalIgnoreCase))
            {
                url = target;
                return true;
            }
        }

        url = "";
        return false;
    }

    public static void Register(CoreWebView2 core)
    {
        // DenyCors: other sites may embed these resources but can never read them with fetch/XHR.
        core.SetVirtualHostNameToFolderMapping(
            Host, AppPaths.PagesFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        core.SetVirtualHostNameToFolderMapping(
            AssetsHost, Path.Combine(AppContext.BaseDirectory, "Assets"), CoreWebView2HostResourceAccessKind.DenyCors);

        Directory.CreateDirectory(ThemeService.CustomWallpapersFolder);
        core.SetVirtualHostNameToFolderMapping(
            WallpapersHost, ThemeService.CustomWallpapersFolder, CoreWebView2HostResourceAccessKind.DenyCors);
    }

    // Ignores query strings and fragments (e.g. settings.html#ses).
    private static bool IsPage(string? url, string page)
        => url is not null
           && url.StartsWith(page, StringComparison.OrdinalIgnoreCase)
           && (url.Length == page.Length || url[page.Length] is '?' or '#');
}
