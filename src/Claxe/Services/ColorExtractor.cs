using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Claxe.Services;

/// <summary>
/// Finds the vivid colours of a wallpaper, so the accent can be taken from it: the image is
/// shrunk, pixels are grouped by hue (only fairly saturated, not too dark ones), and the
/// strongest groups' average colours come back, most dominant first.
/// </summary>
public static class ColorExtractor
{
    private const int Bins = 24;
    private static (string Path, IReadOnlyList<string> Colors)? _cache;

    public static IReadOnlyList<string> Palette(string? path, int count = 6)
    {
        if (path is null || !File.Exists(path))
            return [];
        if (_cache is { } cached && cached.Path == path)
            return cached.Colors;

        try
        {
            var colors = Extract(path, count);
            _cache = (path, colors);
            return colors;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException
                                       or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            Log.Info($"Duvar kâğıdı renkleri çıkarılamadı: {ex.Message}");
            return [];
        }
    }

    public static IReadOnlyList<string> Extract(string path, int count)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = 96;
        image.UriSource = new Uri(path);
        image.EndInit();
        image.Freeze();

        var pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = pixels.PixelWidth * 4;
        var data = new byte[stride * pixels.PixelHeight];
        pixels.CopyPixels(data, stride, 0);
        return FromPixels(data, count);
    }

    /// <summary>BGRA pixels in, "#RRGGBB" colours out (separate for testing).</summary>
    public static IReadOnlyList<string> FromPixels(byte[] bgra, int count)
    {
        var weight = new double[Bins];
        var sumR = new double[Bins];
        var sumG = new double[Bins];
        var sumB = new double[Bins];

        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            double b = bgra[i] / 255.0, g = bgra[i + 1] / 255.0, r = bgra[i + 2] / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var delta = max - min;
            var saturation = max == 0 ? 0 : delta / max;
            if (saturation < 0.3 || max < 0.3 || delta == 0)
                continue;

            var hue = max == r ? (g - b) / delta % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
            hue = (hue * 60 + 360) % 360;
            var bin = (int)(hue / (360.0 / Bins)) % Bins;
            // Vivid, bright pixels count more: they are what an accent should look like.
            var w = saturation * max;
            weight[bin] += w;
            sumR[bin] += r * w;
            sumG[bin] += g * w;
            sumB[bin] += b * w;
        }

        var picked = new List<int>();
        foreach (var bin in Enumerable.Range(0, Bins).Where(b => weight[b] > 0).OrderByDescending(b => weight[b]))
        {
            // Skip hues right next to one already picked, so the swatches differ.
            if (picked.Any(p => Math.Min(Math.Abs(p - bin), Bins - Math.Abs(p - bin)) <= 1))
                continue;
            picked.Add(bin);
            if (picked.Count == count)
                break;
        }

        return picked.Select(b =>
        {
            // Brighten a little so the accent stays readable on the dark interface.
            var (r, g, bl) = (sumR[b] / weight[b], sumG[b] / weight[b], sumB[b] / weight[b]);
            var lift = 0.85 / Math.Max(0.01, Math.Max(r, Math.Max(g, bl)));
            lift = Math.Clamp(lift, 1, 1.6);
            return $"#{To255(r * lift):X2}{To255(g * lift):X2}{To255(bl * lift):X2}";
        }).ToList();
    }

    private static int To255(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
}
