using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Claxe.Services.Conversion;

/// <summary>Image to image (WPF / Windows Imaging Component) and images to one PDF.</summary>
public static class ImageConverter
{
    private const int PdfMaxSide = 4000;

    public static Task ConvertAsync(string source, string output, string format)
        => Sta.RunAsync(() =>
        {
            var image = Load(source);
            if (format == "ico")
            {
                WriteIcon(image, output);
                return;
            }

            BitmapEncoder encoder = format switch
            {
                "png" => new PngBitmapEncoder(),
                "jpg" => new JpegBitmapEncoder { QualityLevel = 92 },
                "bmp" => new BmpBitmapEncoder(),
                "gif" => new GifBitmapEncoder(),
                "tiff" => new TiffBitmapEncoder { Compression = TiffCompressOption.Lzw },
                _ => throw new NotSupportedException(format),
            };

            // These formats have no (or only 1-bit) transparency: flatten onto white instead of black.
            BitmapSource frame = format is "jpg" or "bmp" or "gif" ? OnWhite(image) : image;
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var file = File.Create(output);
            encoder.Save(file);
        });

    /// <summary>One A4 page per image (orientation follows the image), each fitted with a margin.</summary>
    public static Task ImagesToPdfAsync(IReadOnlyList<string> sources, string output)
        => Sta.RunAsync(() =>
        {
            var pages = new List<SimplePdf.ImagePage>();
            foreach (var source in sources)
            {
                var image = OnWhite(Downscale(Load(source), PdfMaxSide));
                var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var jpeg = new MemoryStream();
                encoder.Save(jpeg);
                pages.Add(new SimplePdf.ImagePage(jpeg.ToArray(), image.PixelWidth, image.PixelHeight));
            }

            SimplePdf.WriteImages(output, pages);
        });

    private static BitmapSource Load(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.OnLoad);
        // Icons hold several sizes: take the largest.
        var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth * f.PixelHeight).First();
        frame.Freeze();
        return frame;
    }

    private static BitmapSource Downscale(BitmapSource image, int maxSide)
    {
        var scale = Math.Min(1.0, (double)maxSide / Math.Max(image.PixelWidth, image.PixelHeight));
        if (scale >= 1)
            return image;

        var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
        scaled.Freeze();
        return scaled;
    }

    /// <summary>Composites the image over white and returns 24-bit pixels.</summary>
    private static BitmapSource OnWhite(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth, height = bgra.PixelHeight;
        var pixels = new byte[width * height * 4];
        bgra.CopyPixels(pixels, width * 4, 0);

        var rgb = new byte[width * height * 3];
        for (int i = 0, j = 0; i < pixels.Length; i += 4, j += 3)
        {
            var a = pixels[i + 3];
            rgb[j] = (byte)((pixels[i] * a + 255 * (255 - a)) / 255);
            rgb[j + 1] = (byte)((pixels[i + 1] * a + 255 * (255 - a)) / 255);
            rgb[j + 2] = (byte)((pixels[i + 2] * a + 255 * (255 - a)) / 255);
        }

        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, rgb, width * 3);
        result.Freeze();
        return result;
    }

    /// <summary>A Windows icon with 16, 32, 48 and 256 px images (PNG-compressed entries).</summary>
    private static void WriteIcon(BitmapSource image, string output)
    {
        int[] sizes = [16, 32, 48, 256];
        var entries = sizes.Select(size => (Size: size, Png: SquarePng(image, size))).ToList();

        using var file = File.Create(output);
        using var writer = new BinaryWriter(file);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)entries.Count);

        var offset = 6 + 16 * entries.Count;
        foreach (var (size, png) in entries)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in entries)
            writer.Write(png);
    }

    /// <summary>The image fitted (aspect kept) and centred on a transparent square.</summary>
    private static byte[] SquarePng(BitmapSource image, int size)
    {
        var scale = (double)size / Math.Max(image.PixelWidth, image.PixelHeight);
        var width = image.PixelWidth * scale;
        var height = image.PixelHeight * scale;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
            context.DrawImage(image, new Rect((size - width) / 2, (size - height) / 2, width, height));
        }

        var target = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}

/// <summary>Runs WPF imaging work on its own STA thread, off the UI thread.</summary>
internal static class Sta
{
    public static Task RunAsync(Action work)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                work();
                done.SetResult();
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        })
        { IsBackground = true, Name = "Sorune converter" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }
}
