using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

/// <summary>One shared WebView2 environment (one browser process tree) for every tab.</summary>
public static class BrowserEnvironment
{
    private static Task<CoreWebView2Environment>? _environment;

    public static bool IsRuntimeAvailable(out string? version)
    {
        try
        {
            version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return version is not null;
        }
        catch (WebView2RuntimeNotFoundException)
        {
            version = null;
            return false;
        }
    }

    /// <summary>Must be called on the UI thread.</summary>
    public static Task<CoreWebView2Environment> GetAsync()
        => _environment ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: AppPaths.WebViewData,
            options: new CoreWebView2EnvironmentOptions
            {
                Language = "tr-TR",
                AreBrowserExtensionsEnabled = false,
            });
}
