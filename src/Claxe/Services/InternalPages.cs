using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

/// <summary>
/// Claxe's own pages are served from the read-only Pages folder through a WebView2
/// virtual host; theme images and the mascot come from the Assets folder on a second
/// host. ".internal" is reserved for private use, so it never collides with a real site.
/// </summary>
public static class InternalPages
{
    public const string Host = "claxe.internal";
    public const string Origin = "https://" + Host + "/";
    public const string NewTabUrl = Origin + "newtab.html";

    public const string AssetsHost = "assets.claxe.internal";
    public const string AssetsOrigin = "https://" + AssetsHost + "/";

    public static bool IsInternal(string? url)
        => url?.StartsWith(Origin, StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsNewTab(string? url)
        => string.Equals(url, NewTabUrl, StringComparison.OrdinalIgnoreCase);

    /// <summary>What the address bar shows for a URL (empty for the new tab page).</summary>
    public static string ToDisplayAddress(string? url)
        => url is null || IsNewTab(url) ? "" : url;

    public static void Register(CoreWebView2 core)
    {
        // DenyCors: other sites may embed these resources but can never read them with fetch/XHR.
        core.SetVirtualHostNameToFolderMapping(
            Host, AppPaths.PagesFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        core.SetVirtualHostNameToFolderMapping(
            AssetsHost, Path.Combine(AppContext.BaseDirectory, "Assets"), CoreWebView2HostResourceAccessKind.DenyCors);
    }
}
