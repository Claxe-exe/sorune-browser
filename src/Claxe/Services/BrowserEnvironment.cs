using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

/// <summary>One shared WebView2 environment (one browser process tree) for every tab.</summary>
public static class BrowserEnvironment
{
    private static Task<CoreWebView2Environment>? _environment;

    /// <summary>Set once at startup from the settings: Chromium's auto-dark for every site.</summary>
    public static bool ForceDark { get; set; }

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

    /// <summary>
    /// After the browser process died, the next <see cref="GetAsync"/> starts a new one.
    /// Only resets if <paramref name="dead"/> is still the current environment, so the
    /// many tabs reporting the same crash cause a single restart.
    /// </summary>
    public static void Reset(CoreWebView2Environment dead)
    {
        if (_environment is { IsCompletedSuccessfully: true } current && ReferenceEquals(current.Result, dead))
        {
            _environment = null;
            SessionCookieStore.OnBrowserRestarted();
            Log.Info("Tarayıcı süreci yeniden başlatılıyor.");
        }
    }

    /// <summary>Must be called on the UI thread.</summary>
    public static Task<CoreWebView2Environment> GetAsync()
        => _environment ??= CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: AppPaths.WebViewData,
            options: new CoreWebView2EnvironmentOptions
            {
                // Built-in menus and sites' Accept-Language follow the interface language.
                Language = L.Locale,
                // Unpacked extensions the user installs from Settings → Extensions.
                AreBrowserExtensionsEnabled = true,
                AdditionalBrowserArguments = ForceDark ? "--enable-features=WebContentsForceDark" : null,
            });
}
