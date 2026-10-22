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

    /// <summary>History, bookmarks and downloads; the fragment picks the section.</summary>
    public const string LibraryUrl = Origin + "library.html";
    public const string HistoryUrl = LibraryUrl + "#gecmis";
    public const string BookmarksUrl = LibraryUrl + "#yer-imleri";
    public const string DownloadsUrl = LibraryUrl + "#indirmeler";

    /// <summary>File converter, shown in its own sidebar panel.</summary>
    public const string ConverterUrl = Origin + "converter.html";

    /// <summary>Mini tools, shown in their own sidebar panel.</summary>
    public const string ToolsUrl = Origin + "tools.html";

    /// <summary>"What's new" after an update.</summary>
    public const string WhatsNewUrl = Origin + "whatsnew.html";

    public const string AssetsHost = "assets.claxe.internal";
    public const string AssetsOrigin = "https://" + AssetsHost + "/";

    public const string WallpapersHost = "wallpapers.claxe.internal";
    public const string WallpapersOrigin = "https://" + WallpapersHost + "/";

    /// <summary>Friendly addresses shown in (and accepted by) the address bar.</summary>
    /// <summary>Scheme shown in the address bar; the old name keeps working as an alias.</summary>
    public const string Scheme = "sorune://";
    private const string LegacyScheme = "claxe://";

    private static readonly (string Path, string Url)[] FriendlyNames =
    [
        ("ayarlar", SettingsUrl),
        ("settings", SettingsUrl),
        ("yenisekme", NewTabUrl),
        ("newtab", NewTabUrl),
        ("gecmis", HistoryUrl),
        ("history", HistoryUrl),
        ("yer-imleri", BookmarksUrl),
        ("bookmarks", BookmarksUrl),
        ("indirmeler", DownloadsUrl),
        ("downloads", DownloadsUrl),
        ("yenilikler", WhatsNewUrl),
        ("whatsnew", WhatsNewUrl),
        ("donusturucu", ConverterUrl),
        ("converter", ConverterUrl),
        ("araclar", ToolsUrl),
        ("tools", ToolsUrl),
    ];

    public static bool IsInternal(string? url)
        => url?.StartsWith(Origin, StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsNewTab(string? url) => IsPage(url, NewTabUrl);

    public static bool IsSettings(string? url) => IsPage(url, SettingsUrl);

    public static bool IsLibrary(string? url) => IsPage(url, LibraryUrl);

    /// <summary>What the address bar shows for a URL (empty for the new tab page).</summary>
    public static string ToDisplayAddress(string? url)
    {
        if (url is null || IsNewTab(url))
            return "";

        if (IsSettings(url))
            return Scheme + "ayarlar";

        if (IsPage(url, WhatsNewUrl))
            return Scheme + "yenilikler";

        if (IsPage(url, ConverterUrl))
            return Scheme + "donusturucu";

        if (IsPage(url, ToolsUrl))
            return Scheme + "araclar";

        if (IsLibrary(url))
        {
            var hash = url.IndexOf('#') is var i and >= 0 ? url[(i + 1)..] : "gecmis";
            return Scheme + (hash is "yer-imleri" or "indirmeler" ? hash : "gecmis");
        }

        return url;
    }

    public static bool TryResolveFriendly(string input, out string url)
    {
        input = input.Trim().TrimEnd('/');
        foreach (var scheme in new[] { Scheme, LegacyScheme })
        {
            if (!input.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
                continue;

            var path = input[scheme.Length..];
            foreach (var (name, target) in FriendlyNames)
            {
                if (string.Equals(path, name, StringComparison.OrdinalIgnoreCase))
                {
                    url = target;
                    return true;
                }
            }
        }

        url = "";
        return false;
    }

    private static string? _languageScript;

    /// <summary>
    /// Gives Sorune's own pages the interface language and its dictionary before their scripts
    /// run (no Turkish flash). Only set on the internal host; websites never see it.
    /// </summary>
    public static async Task InjectLanguageAsync(CoreWebView2 core)
    {
        if (L.Language == "tr")
            return;

        _languageScript ??= $$"""
            if (location.hostname === '{{Host}}') {
              window.__soruneI18n = {{System.Text.Json.JsonSerializer.Serialize(new { lang = L.Language, locale = L.Locale, strings = L.Strings })}};
            }
            """;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(_languageScript);
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
