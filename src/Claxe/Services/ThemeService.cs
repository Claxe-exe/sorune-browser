using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Claxe.Models;

namespace Claxe.Services;

public sealed record ThemeInfo(string Id, string Name, string? Description, string Accent, string? ThumbnailUrl);

public sealed record WallpaperInfo(string Id, string Name, string ThumbnailUrl);

/// <summary>
/// Turns a theme JSON plus the user's appearance settings into application resources
/// (consumed with DynamicResource, so everything updates live) and pre-renders the
/// blurred wallpaper used behind the chrome.
/// </summary>
public static class ThemeService
{
    public const string DefaultThemeId = "midnight-mei";
    private const string PresetPrefix = "preset:";
    private const string CustomPrefix = "custom:";

    private static string? _backdropKey;

    public static string ThemesFolder => Path.Combine(AppContext.BaseDirectory, "Assets", "Themes");
    public static string WallpapersFolder => Path.Combine(AppContext.BaseDirectory, "Assets", "Wallpapers");
    public static string CustomWallpapersFolder => Path.Combine(AppPaths.DataRoot, "wallpapers");

    public static ThemeDefinition Current { get; private set; } = new() { Id = DefaultThemeId, Name = "Midnight Mei" };

    /// <summary>Accent after the user's override.</summary>
    public static string Accent { get; private set; } = "#B583FF";

    /// <summary>Blurred, downscaled wallpaper for the window chrome. Null if there is none.</summary>
    public static ImageSource? Backdrop { get; private set; }

    /// <summary>URL of the active wallpaper as seen from Claxe's internal pages.</summary>
    public static string? WallpaperUrl { get; private set; }

    public static Color BaseColor => Parse(Current.Colors.Base);

    public static double TitleBarHeight { get; private set; } = 44;

    public static IReadOnlyList<ThemeInfo> ListThemes()
    {
        if (!Directory.Exists(ThemesFolder))
            return [];

        return Directory.EnumerateFiles(ThemesFolder, "*.json")
            .Select(path => JsonStore.Load<ThemeDefinition>(path))
            .OfType<ThemeDefinition>()
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .OrderBy(t => t.Id == DefaultThemeId ? 0 : 1).ThenBy(t => t.Name)
            .Select(t => new ThemeInfo(t.Id, t.Name, t.Description is null ? null : L.T(t.Description), t.Colors.Accent,
                t.Wallpaper is null ? null : InternalPages.AssetsOrigin + "Themes/thumbs/" + Uri.EscapeDataString(t.Wallpaper)))
            .ToList();
    }

    public static IReadOnlyList<WallpaperInfo> ListPresetWallpapers()
    {
        if (!Directory.Exists(WallpapersFolder))
            return [];

        var turkish = new CultureInfo("tr-TR");
        return Directory.EnumerateFiles(WallpapersFolder, "*.jpg")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Select(file => new WallpaperInfo(
                PresetPrefix + file,
                turkish.TextInfo.ToTitleCase(Path.GetFileNameWithoutExtension(file).Replace('-', ' ')),
                InternalPages.AssetsOrigin + "Wallpapers/thumbs/" + Uri.EscapeDataString(file)))
            // By display name, so "Konosuba" comes before "Konosuba 2".
            .OrderBy(w => w.Name, StringComparer.Create(turkish, ignoreCase: true))
            .ToList();
    }

    public static bool ThemeExists(string? id) => Load(id) is not null;

    public static bool WallpaperExists(string? id) => id is null || ResolveWallpaper(id) is not null;

    /// <summary>Picture URL for a wallpaper choice (null = the given theme's own wallpaper).</summary>
    public static string? WallpaperUrlFor(string? wallpaperId, string themeId)
    {
        if (wallpaperId is not null)
            return ResolveWallpaper(wallpaperId)?.Url;
        return Load(themeId)?.Wallpaper is { } file ? InternalPages.AssetsOrigin + "Themes/" + Uri.EscapeDataString(file) : null;
    }

    /// <summary>Accent colour of a theme (for previews); the current accent when unknown.</summary>
    public static string AccentOf(string themeId) => Load(themeId)?.Colors.Accent ?? Accent;

    /// <summary>File of the wallpaper in use (for picking colours out of it); null when none.</summary>
    public static string? WallpaperPath => _backdropKey;

    /// <summary>Applies theme, overrides and layout. Cheap to call repeatedly (the backdrop is cached).</summary>
    public static void Apply(AppSettings settings)
    {
        var theme = Load(settings.Theme) ?? Load(DefaultThemeId);
        if (theme is null)
            Log.Error($"Tema bulunamadı: {settings.Theme}. Varsayılan renkler kullanılıyor.");
        else
            Current = theme;

        Accent = IsHexColor(settings.AccentColor) ? settings.AccentColor! : Current.Colors.Accent;
        ApplyColors(Current, Accent, settings.ChromeOpacity ?? Current.ChromeTintOpacity);
        ApplyLayout(settings.Density, settings.CornerStyle);

        var wallpaper = ResolveWallpaper(settings.Wallpaper) ?? ResolveWallpaper(null);
        WallpaperUrl = wallpaper?.Url;
        if (wallpaper?.Path != _backdropKey)
        {
            _backdropKey = wallpaper?.Path;
            Backdrop = wallpaper is null ? null : CreateBackdrop(wallpaper.Value.Path);
        }
    }

    public static bool IsHexColor(string? value)
        => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);

    private static ThemeDefinition? Load(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId) || themeId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        return JsonStore.Load<ThemeDefinition>(Path.Combine(ThemesFolder, themeId + ".json"));
    }

    private static (string Path, string Url)? ResolveWallpaper(string? id)
    {
        string folder, file, url;
        if (id is null)
        {
            if (Current.Wallpaper is null)
                return null;
            (folder, file) = (ThemesFolder, Current.Wallpaper);
            url = InternalPages.AssetsOrigin + "Themes/" + Uri.EscapeDataString(file);
        }
        else if (id.StartsWith(PresetPrefix, StringComparison.Ordinal))
        {
            (folder, file) = (WallpapersFolder, id[PresetPrefix.Length..]);
            url = InternalPages.AssetsOrigin + "Wallpapers/" + Uri.EscapeDataString(file);
        }
        else if (id.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            (folder, file) = (CustomWallpapersFolder, id[CustomPrefix.Length..]);
            url = InternalPages.WallpapersOrigin + Uri.EscapeDataString(file);
        }
        else
        {
            return null;
        }

        // File names only: never let a setting point outside its folder.
        if (file.Length == 0 || file != Path.GetFileName(file))
            return null;

        var path = Path.Combine(folder, file);
        return File.Exists(path) ? (path, url) : null;
    }

    private static void ApplyColors(ThemeDefinition theme, string accentHex, double tintOpacity)
    {
        var c = theme.Colors;
        var resources = Application.Current.Resources;

        void Brush(string key, Color color, double opacity = 1)
        {
            var brush = new SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            resources[key] = brush;
        }

        var accent = Parse(accentHex);
        var accent2 = Parse(c.AccentSecondary);
        resources["AccentColor"] = accent;
        resources["AccentSecondaryColor"] = accent2;
        resources["AccentWashColor"] = Color.FromArgb(0x30, accent.R, accent.G, accent.B);
        resources["AccentFadeColor"] = Color.FromArgb(0x50, accent.R, accent.G, accent.B);

        Brush("WindowBackgroundBrush", Parse(c.Base));
        Brush("ChromeTintBrush", Parse(c.ChromeTint), Math.Clamp(tintOpacity, 0, 1));
        Brush("SurfaceBrush", Parse(c.Surface));
        Brush("SurfaceHoverBrush", Parse(c.SurfaceHover));
        Brush("SurfacePressedBrush", Parse(c.SurfacePressed));
        Brush("BorderBrush", Parse(c.Border));
        Brush("TextPrimaryBrush", Parse(c.TextPrimary));
        Brush("TextSecondaryBrush", Parse(c.TextSecondary));
        Brush("AccentBrush", accent);
        Brush("AccentSoftBrush", accent, 0.16);
        Brush("AccentSecondaryBrush", accent2);
        Brush("SelectionBrush", accent, 0.45);
        Brush("DangerBrush", Parse(c.Danger));
        Brush("WarningBrush", Parse(c.Warning));
    }

    /// <summary>Sizes for the chrome ("density") and corner shapes.</summary>
    private static void ApplyLayout(string density, string cornerStyle)
    {
        var (title, toolbar, tab, pill, button) = density switch
        {
            "compact" => (38.0, 42.0, 32.0, 32.0, 32.0),
            "comfortable" => (50.0, 54.0, 40.0, 40.0, 40.0),
            _ => (44.0, 48.0, 36.0, 36.0, 36.0),
        };
        var (tabRadius, controlRadius, pillRadius) = cornerStyle switch
        {
            "sharp" => (4.0, 4.0, 6.0),
            "round" => (14.0, 13.0, pill / 2),
            _ => (10.0, 9.0, pill / 2),
        };

        TitleBarHeight = title;
        var r = Application.Current.Resources;
        r["TitleBarHeight"] = title;
        r["ToolbarHeight"] = toolbar;
        r["TitleBarRowHeight"] = new GridLength(title);
        r["ToolbarRowHeight"] = new GridLength(toolbar);
        r["TabHeight"] = tab;
        r["PillHeight"] = pill;
        r["IconButtonSize"] = button;
        r["TabCornerRadiusValue"] = tabRadius;
        r["TabCornerRadius"] = new CornerRadius(tabRadius, tabRadius, 0, 0);
        r["ControlCornerRadius"] = new CornerRadius(controlRadius);
        r["PillCornerRadius"] = new CornerRadius(pillRadius);
    }

    /// <summary>
    /// Blurs a small copy of the wallpaper once. Upscaling an already blurred 640px image
    /// is visually identical to a live BlurEffect but costs nothing per frame.
    /// </summary>
    private static ImageSource? CreateBackdrop(string path)
    {
        try
        {
            const int width = 640;
            var source = new BitmapImage();
            source.BeginInit();
            source.UriSource = new Uri(path);
            source.DecodePixelWidth = width;
            source.CacheOption = BitmapCacheOption.OnLoad;
            source.EndInit();
            source.Freeze();

            var height = (int)Math.Round(width * (double)source.PixelHeight / source.PixelWidth);

            // Drawn slightly oversized so the blur doesn't pull transparent edges in.
            var image = new System.Windows.Controls.Image
            {
                Source = source,
                Stretch = Stretch.Fill,
                Width = width * 1.12,
                Height = height * 1.12,
                Effect = new BlurEffect { Radius = 22, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality },
            };
            image.Measure(new Size(image.Width, image.Height));
            image.Arrange(new Rect(-width * 0.06, -height * 0.06, image.Width, image.Height));

            var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            target.Render(image);
            target.Freeze();
            return target;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or COMException or IOException)
        {
            Log.Error($"Duvar kâğıdı okunamadı: {path}", ex);
            return null;
        }
    }

    private static Color Parse(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch (FormatException)
        {
            Log.Error($"Geçersiz tema rengi: {value}");
            return Colors.Magenta;
        }
    }
}
