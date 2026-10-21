using System.IO;

namespace Claxe.Services.Conversion;

/// <summary>An output format offered for a file; some need LibreOffice to be installed.</summary>
public sealed record ConversionTarget(string Format, bool NeedsLibreOffice);

/// <summary>Which formats each kind of file can be turned into.</summary>
public static class ConversionTargets
{
    public static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ico", ".jfif"];

    private static readonly string[] WriterExtensions = [".doc", ".odt", ".rtf"];
    private static readonly string[] ImpressExtensions = [".pptx", ".ppt", ".odp", ".pps", ".ppsx"];
    private static readonly string[] CalcExtensions = [".xlsx", ".xls", ".ods"];

    public static bool IsImage(string path) => ImageExtensions.Contains(Extension(path));

    /// <summary>"image", "pdf", "word", "slides", "sheet", "text" or "other": picks the icon on the page.</summary>
    public static string KindOf(string path)
    {
        var ext = Extension(path);
        if (IsImage(path)) return "image";
        if (ext == ".pdf") return "pdf";
        if (ext == ".docx" || WriterExtensions.Contains(ext)) return "word";
        if (ImpressExtensions.Contains(ext)) return "slides";
        if (CalcExtensions.Contains(ext) || ext == ".csv") return "sheet";
        if (ext is ".txt" or ".md" or ".markdown" or ".html" or ".htm") return "text";
        return "other";
    }

    public static IReadOnlyList<ConversionTarget> For(string path)
    {
        var ext = Extension(path);
        static ConversionTarget Own(string format) => new(format, false);
        static ConversionTarget Office(string format) => new(format, true);

        if (IsImage(path))
        {
            // WPF reads WebP when Windows has the codec, but cannot write it.
            return new[] { "png", "jpg", "bmp", "gif", "tiff", "ico", "pdf" }
                .Where(f => !ext.TrimStart('.').Equals(f, StringComparison.OrdinalIgnoreCase)
                            && !(f == "jpg" && ext is ".jpeg" or ".jfif")
                            && !(f == "tiff" && ext == ".tif"))
                .Select(Own).ToList();
        }

        return ext switch
        {
            ".pdf" => [Own("docx"), Own("txt")],
            ".txt" => [Own("pdf"), Own("docx")],
            ".md" or ".markdown" => [Own("pdf"), Own("html"), Own("docx")],
            ".html" or ".htm" => [Own("pdf")],
            ".docx" => [Office("pdf"), Office("odt"), Own("txt")],
            ".doc" or ".rtf" => [Office("pdf"), Office("docx"), Office("odt")],
            ".odt" => [Office("pdf"), Office("docx")],
            ".pptx" or ".ppsx" => [Office("pdf"), Office("odp")],
            ".ppt" or ".pps" => [Office("pdf"), Office("pptx"), Office("odp")],
            ".odp" => [Office("pdf"), Office("pptx")],
            ".xlsx" => [Office("pdf"), Office("ods"), Office("csv")],
            ".xls" => [Office("pdf"), Office("xlsx"), Office("csv")],
            ".ods" => [Office("pdf"), Office("xlsx"), Office("csv")],
            ".csv" => [Office("xlsx"), Office("pdf")],
            _ => [],
        };
    }

    public static ConversionTarget? Find(string path, string format)
        => For(path).FirstOrDefault(t => t.Format.Equals(format, StringComparison.OrdinalIgnoreCase));

    public static string Extension(string path) => Path.GetExtension(path).ToLowerInvariant();

    /// <summary>"name.pdf" next to the source, or "name (2).pdf" when that exists. Falls back to Downloads.</summary>
    public static string OutputPathFor(string source, string format, string? baseName = null)
    {
        var name = (baseName ?? Path.GetFileNameWithoutExtension(source)) + "." + format;
        var folder = Path.GetDirectoryName(source);
        if (folder is null || !CanWrite(folder))
            folder = DownloadsFolder;

        return Unique(Path.Combine(folder, name));
    }

    public static string DownloadsFolder
    {
        get
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var downloads = Path.Combine(profile, "Downloads");
            return Directory.Exists(downloads) ? downloads : profile;
        }
    }

    public static string Unique(string path)
    {
        if (!File.Exists(path))
            return path;

        var folder = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(folder, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static bool CanWrite(string folder)
    {
        try
        {
            var probe = Path.Combine(folder, $".sorune-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
