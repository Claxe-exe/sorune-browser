using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Claxe.Services;

/// <summary>The user's own look for one website.</summary>
public sealed class SiteStyle
{
    public string Host { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>One of <see cref="SiteStyleStore.Fonts"/>; empty keeps the site's own.</summary>
    public string Font { get; set; } = "";

    public int Hue { get; set; }
    public int Saturation { get; set; } = 100;
    public int Brightness { get; set; } = 100;

    /// <summary>Links take the theme's accent colour.</summary>
    public bool ThemeLinks { get; set; }

    /// <summary>CSS selectors of the elements the user hid on this site.</summary>
    public List<string> Hidden { get; set; } = [];

    public string Css { get; set; } = "";

    /// <summary>Nothing is changed on the site, so there is nothing to keep.</summary>
    public bool IsEmpty => Font.Length == 0 && Hue == 0 && Saturation == 100 && Brightness == 100
                           && !ThemeLinks && Hidden.Count == 0 && Css.Length == 0;
}

/// <summary>
/// Per-site looks (site-styles.json), one per host without "www.". <see cref="Changed"/> fires at
/// once so that open pages follow a slider live; saving and <see cref="Saved"/> wait until the
/// changes pause.
/// </summary>
public sealed class SiteStyleStore
{
    public const int MaxSites = 200;
    public const int MaxHidden = 100;
    public const int MaxSelectorLength = 400;
    public const int MaxCssLength = 20_000;

    /// <summary>Fonts every Windows has: a site's security rules can block fonts Sorune would bring along.</summary>
    public static readonly string[] Fonts =
        ["", "Segoe UI", "Verdana", "Trebuchet MS", "Georgia", "Times New Roman", "Consolas", "Comic Sans MS"];

    private readonly List<SiteStyle> _items;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public SiteStyleStore()
    {
        _items = JsonStore.Load<List<SiteStyle>>(FilePath) ?? [];
        _items.RemoveAll(s => string.IsNullOrEmpty(s.Host));
        foreach (var style in _items)
            Normalize(style);

        _saveTimer.Tick += (_, _) => Flush();
        if (Application.Current is { } app)
            app.Exit += (_, _) => Flush();
    }

    /// <summary>A style changed: the host it belongs to.</summary>
    public event Action<string>? Changed;

    /// <summary>The changes have paused and are on disk; new pages can take them over.</summary>
    public event Action? Saved;

    public IReadOnlyList<SiteStyle> Items => _items;

    /// <summary>The key of a web address (no "www."), or null for anything but http and https.</summary>
    public static string? HostOf(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host))
            return null;

        var host = uri.IdnHost.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    public SiteStyle? Find(string? host) => _items.FirstOrDefault(s => s.Host == host);

    /// <summary>Selectors come from the page being styled, so they cannot end their rule and start another.</summary>
    public static bool IsValidSelector(string? selector)
        => !string.IsNullOrWhiteSpace(selector)
           && selector.Length <= MaxSelectorLength
           && selector.IndexOfAny(['{', '}', ';', '@', '\n', '\r']) < 0
           && !selector.Contains("url(", StringComparison.OrdinalIgnoreCase)
           && !selector.Contains("/*", StringComparison.Ordinal);

    /// <summary>Changes a site's look; a look that ends up changing nothing is dropped.</summary>
    public void Update(string host, Action<SiteStyle> change)
    {
        var style = Find(host);
        if (style is null)
        {
            if (_items.Count >= MaxSites)
                return;
            style = new SiteStyle { Host = host };
            _items.Add(style);
        }

        change(style);
        Normalize(style);
        if (style.IsEmpty)
            _items.Remove(style);
        Touch(host);
    }

    /// <summary>Hides an element for good. False when it was hidden already, or cannot be.</summary>
    public bool Hide(string host, string? selector)
    {
        if (!IsValidSelector(selector) || Find(host) is { } full && (full.Hidden.Count >= MaxHidden || full.Hidden.Contains(selector!)))
            return false;

        Update(host, s => s.Hidden.Add(selector!));
        return true;
    }

    public void Remove(string? host)
    {
        if (host is not null && _items.RemoveAll(s => s.Host == host) > 0)
            Touch(host);
    }

    /// <summary>The theme's accent changed: pages with themed links are built again.</summary>
    public void RefreshThemedLinks()
    {
        foreach (var style in _items.Where(s => s.ThemeLinks).ToList())
            Touch(style.Host);
    }

    public void Flush()
    {
        if (!_saveTimer.IsEnabled)
            return;

        _saveTimer.Stop();
        try
        {
            JsonStore.Save(FilePath, _items);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Site görünümleri kaydedilemedi.", ex);
        }
        Saved?.Invoke();
    }

    private void Touch(string host)
    {
        _saveTimer.Stop();
        _saveTimer.Start();
        Changed?.Invoke(host);
    }

    private static void Normalize(SiteStyle style)
    {
        style.Font = Fonts.Contains(style.Font) ? style.Font : "";
        style.Hue = Math.Clamp(style.Hue, -180, 180);
        style.Saturation = Math.Clamp(style.Saturation, 0, 200);
        style.Brightness = Math.Clamp(style.Brightness, 50, 150);
        style.Hidden = (style.Hidden ?? []).Where(IsValidSelector).Distinct().Take(MaxHidden).ToList();
        style.Css ??= "";
        if (style.Css.Length > MaxCssLength)
            style.Css = style.Css[..MaxCssLength];
    }

    private static string FilePath => Path.Combine(AppPaths.DataRoot, "site-styles.json");
}
