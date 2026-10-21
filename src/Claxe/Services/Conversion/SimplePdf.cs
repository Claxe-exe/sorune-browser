using System.Globalization;
using System.IO;
using System.Text;

namespace Claxe.Services.Conversion;

/// <summary>Writes a minimal PDF whose pages each show one JPEG image.</summary>
internal static class SimplePdf
{
    public sealed record ImagePage(byte[] Jpeg, int Width, int Height);

    // A4 in points, with a margin around the image.
    private const double A4Short = 595.28;
    private const double A4Long = 841.89;
    private const double Margin = 24;

    public static void WriteImages(string path, IReadOnlyList<ImagePage> pages)
    {
        using var file = File.Create(path);
        var offsets = new List<long>();
        var latin1 = Encoding.Latin1;

        void Raw(string text)
        {
            var bytes = latin1.GetBytes(text);
            file.Write(bytes, 0, bytes.Length);
        }

        void BeginObject(int number)
        {
            while (offsets.Count < number)
                offsets.Add(0);
            offsets[number - 1] = file.Position;
            Raw($"{number} 0 obj\n");
        }

        static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        Raw("%PDF-1.4\n%âãÏÓ\n");

        // 1 = catalog, 2 = page tree, then 3 objects per page: page, image, content stream.
        var pageNumbers = Enumerable.Range(0, pages.Count).Select(i => 3 + i * 3).ToList();

        BeginObject(1);
        Raw("<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        BeginObject(2);
        Raw($"<< /Type /Pages /Kids [{string.Join(" ", pageNumbers.Select(n => $"{n} 0 R"))}] /Count {pages.Count} >>\nendobj\n");

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            int pageObj = pageNumbers[i], imageObj = pageObj + 1, contentObj = pageObj + 2;

            var landscape = page.Width > page.Height;
            var pageWidth = landscape ? A4Long : A4Short;
            var pageHeight = landscape ? A4Short : A4Long;
            var scale = Math.Min((pageWidth - 2 * Margin) / page.Width, (pageHeight - 2 * Margin) / page.Height);
            var drawWidth = page.Width * scale;
            var drawHeight = page.Height * scale;
            var x = (pageWidth - drawWidth) / 2;
            var y = (pageHeight - drawHeight) / 2;

            BeginObject(pageObj);
            Raw($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(pageWidth)} {N(pageHeight)}] " +
                $"/Resources << /XObject << /Im0 {imageObj} 0 R >> >> /Contents {contentObj} 0 R >>\nendobj\n");

            BeginObject(imageObj);
            Raw($"<< /Type /XObject /Subtype /Image /Width {page.Width} /Height {page.Height} /ColorSpace /DeviceRGB " +
                $"/BitsPerComponent 8 /Filter /DCTDecode /Length {page.Jpeg.Length} >>\nstream\n");
            file.Write(page.Jpeg);
            Raw("\nendstream\nendobj\n");

            var content = $"q {N(drawWidth)} 0 0 {N(drawHeight)} {N(x)} {N(y)} cm /Im0 Do Q";
            BeginObject(contentObj);
            Raw($"<< /Length {content.Length} >>\nstream\n{content}\nendstream\nendobj\n");
        }

        var xref = file.Position;
        Raw($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            Raw($"{offset:D10} 00000 n \n");
        Raw($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
    }
}
