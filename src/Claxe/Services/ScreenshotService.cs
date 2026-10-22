using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

/// <summary>
/// Page screenshots: the visible part (WebView2's own preview capture) or the whole page
/// (DevTools captureBeyondViewport). Saved as PNG in Pictures\Sorune and copied to the clipboard.
/// </summary>
public static class ScreenshotService
{
    /// <summary>Very long pages are cut here: bigger images are slow and some viewers can't open them.</summary>
    private const int MaxFullPageHeight = 16000;

    public static string Folder
    {
        get
        {
            var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            return Path.Combine(string.IsNullOrEmpty(pictures) ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : pictures, "Sorune");
        }
    }

    public static async Task<BitmapSource> CaptureVisibleAsync(CoreWebView2 core)
    {
        using var stream = new MemoryStream();
        await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        return Decode(stream.ToArray());
    }

    public static async Task<BitmapSource> CaptureFullPageAsync(CoreWebView2 core)
    {
        using var metrics = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.getLayoutMetrics", "{}"));
        var size = metrics.RootElement.TryGetProperty("cssContentSize", out var css) ? css : metrics.RootElement.GetProperty("contentSize");
        var width = Math.Ceiling(size.GetProperty("width").GetDouble());
        var height = Math.Min(MaxFullPageHeight, Math.Ceiling(size.GetProperty("height").GetDouble()));

        var parameters = JsonSerializer.Serialize(new
        {
            format = "png",
            captureBeyondViewport = true,
            clip = new { x = 0, y = 0, width, height, scale = 1 },
        });
        using var shot = JsonDocument.Parse(await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", parameters));
        return Decode(Convert.FromBase64String(shot.RootElement.GetProperty("data").GetString()!));
    }

    /// <summary>Saves the image and puts it on the clipboard; returns the file path.</summary>
    public static string SaveAndCopy(BitmapSource image, string? title)
    {
        Directory.CreateDirectory(Folder);
        var name = string.Concat((title ?? "").Split(Path.GetInvalidFileNameChars())).Trim();
        if (name.Length > 60)
            name = name[..60].Trim();
        var path = Path.Combine(Folder, $"{(name.Length > 0 ? name + " " : "")}{DateTime.Now:yyyy-MM-dd HH.mm.ss}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var file = File.Create(path))
            encoder.Save(file);

        try
        {
            Clipboard.SetImage(image);
        }
        catch (System.Runtime.InteropServices.COMException ex)
        {
            // Another app is holding the clipboard; the file is saved anyway.
            Log.Info($"Ekran görüntüsü panoya kopyalanamadı: {ex.Message}");
        }

        Log.Info($"Ekran görüntüsü kaydedildi: {path}");
        return path;
    }

    private static BitmapSource Decode(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }
}
