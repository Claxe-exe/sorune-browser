using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

/// <summary>One shared WebView2 environment (one browser process tree) for every tab.</summary>
public static class BrowserEnvironment
{
    private static Task<CoreWebView2Environment>? _environment;

    /// <summary>Set once at startup from the settings: Chromium's auto-dark for every site.</summary>
    public static bool ForceDark { get; set; }

    /// <summary>Set once at startup: the port of <see cref="SecureDns"/>, which every HTTPS connection then goes through.</summary>
    public static int? SecureDnsPort { get; set; }

    /// <summary>Microsoft Defender SmartScreen warnings on dangerous sites and downloads.</summary>
    public static bool SmartScreen { get; set; } = true;

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
                AdditionalBrowserArguments = Arguments(),
            });

    private static string? Arguments()
    {
        var arguments = new List<string>();
        if (ForceDark) arguments.Add("--enable-features=WebContentsForceDark");
        // HTTPS only: plain HTTP sites keep going direct.
        if (SecureDnsPort is { } port) arguments.Add($"--proxy-server=https=127.0.0.1:{port}");
        return arguments.Count == 0 ? null : string.Join(' ', arguments);
    }
}
