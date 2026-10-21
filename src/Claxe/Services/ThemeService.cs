using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Claxe.Models;

namespace Claxe.Services;

/// <summary>
/// Loads a theme JSON, pushes its colours into the application resources (consumed
/// with DynamicResource) and pre-renders the blurred wallpaper used behind the chrome.
/// </summary>
public static class ThemeService
{
    private const string DefaultThemeId = "midnight-mei";

    public static string ThemesFolder => Path.Combine(AppContext.BaseDirectory, "Assets", "Themes");

    public static ThemeDefinition Current { get; private set; } = new() { Id = DefaultThemeId, Name = "Midnight Mei" };

    /// <summary>Blurred, downscaled wallpaper for the window chrome. Null if the theme has none.</summary>
    public static ImageSource? Backdrop { get; private set; }

    public static Color BaseColor => Parse(Current.Colors.Base);

    /// <summary>URL of the wallpaper as seen from Claxe's internal pages.</summary>
    public static string? WallpaperUrl => Current.Wallpaper is { } file
        ? InternalPages.AssetsOrigin + "Themes/" + Uri.EscapeDataString(file)
        : null;

    public static void Apply(string? themeId)
    {
        var theme = Load(themeId) ?? Load(DefaultThemeId);
        if (theme is null)
        {
            Log.Error($"Tema bulunamadı: {themeId}. Varsayılan renkler kullanılıyor.");
            return;
        }

        Current = theme;
        ApplyColors(theme);
        Backdrop = CreateBackdrop(theme);
    }

    private static ThemeDefinition? Load(string? themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId) || themeId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return null;

        return JsonStore.Load<ThemeDefinition>(Path.Combine(ThemesFolder, themeId + ".json"));
    }

    private static void ApplyColors(ThemeDefinition theme)
    {
        var c = theme.Colors;
        var resources = Application.Current.Resources;

        void Brush(string key, Color color, double opacity = 1)
        {
            var brush = new SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            resources[key] = brush;
        }

        var accent = Parse(c.Accent);
        resources["AccentColor"] = accent;
        resources["AccentSecondaryColor"] = Parse(c.AccentSecondary);

        Brush("WindowBackgroundBrush", Parse(c.Base));
        Brush("ChromeTintBrush", Parse(c.ChromeTint), Math.Clamp(theme.ChromeTintOpacity, 0, 1));
        Brush("SurfaceBrush", Parse(c.Surface));
        Brush("SurfaceHoverBrush", Parse(c.SurfaceHover));
        Brush("SurfacePressedBrush", Parse(c.SurfacePressed));
        Brush("BorderBrush", Parse(c.Border));
        Brush("TextPrimaryBrush", Parse(c.TextPrimary));
        Brush("TextSecondaryBrush", Parse(c.TextSecondary));
        Brush("AccentBrush", accent);
        Brush("AccentSoftBrush", accent, 0.16);
        Brush("AccentSecondaryBrush", Parse(c.AccentSecondary));
        Brush("SelectionBrush", accent, 0.45);
        Brush("DangerBrush", Parse(c.Danger));
        Brush("WarningBrush", Parse(c.Warning));
    }

    /// <summary>
    /// Blurs a small copy of the wallpaper once. Upscaling an already blurred 640px image
    /// is visually identical to a live BlurEffect but costs nothing per frame.
    /// </summary>
    private static ImageSource? CreateBackdrop(ThemeDefinition theme)
    {
        if (theme.Wallpaper is null)
            return null;

        var path = Path.Combine(ThemesFolder, theme.Wallpaper);
        if (!File.Exists(path))
        {
            Log.Error($"Tema duvar kâğıdı bulunamadı: {path}");
            return null;
        }

        const int width = 640;
        var source = new BitmapImage();
        source.BeginInit();
        source.UriSource = new Uri(path);
        source.DecodePixelWidth = width;
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.EndInit();
        source.Freeze();

        var height = (int)Math.Round(width * (double)source.PixelHeight / source.PixelWidth);

        // Draw slightly oversized so the blur doesn't pull transparent edges in.
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
