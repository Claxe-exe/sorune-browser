using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Claxe.Services;
using Claxe.Services.Conversion;
using Microsoft.Web.WebView2.Core;

namespace Claxe.ViewModels;

/// <summary>
/// Backs the converter panel (sorune://converter). The page only ever sees file names; it can
/// convert files the user picked or dropped in this run and open results Sorune produced.
/// </summary>
public sealed class ConverterController
{
    private readonly HashSet<string> _allowedSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _outputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<TabViewModel?> _activeTab;
    private readonly Action<string> _openInNewTab;
    private bool _installing;
    private double? _installProgress;
    private bool _busy;

    public ConverterController(Func<TabViewModel?> activeTab, Action<string> openInNewTab)
    {
        _activeTab = activeTab;
        _openInNewTab = openInNewTab;
    }

    /// <summary>The panel page this controller answers; set by the panel on creation.</summary>
    public TabViewModel? Page { get; set; }

    public void Handle(TabViewModel page, string type, JsonElement message)
    {
        Page = page;
        string? Text(string name)
            => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        switch (type)
        {
            case "converter:ready":
                PostState();
                break;

            case "converter:pick":
                Pick();
                break;

            case "converter:drop" when message.TryGetProperty("droppedPaths", out var dropped) && dropped.ValueKind == JsonValueKind.Array:
                AddFiles(dropped.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!));
                break;

            case "converter:convert" when message.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Array:
                _ = ConvertAsync(
                    paths.EnumerateArray().Where(p => p.ValueKind == JsonValueKind.String).Select(p => p.GetString()!).ToList(),
                    Text("target") ?? "",
                    message.TryGetProperty("merge", out var merge) && merge.ValueKind == JsonValueKind.True);
                break;

            case "converter:open" when Text("path") is { } output && _outputs.Contains(output):
                Shell(output, select: false);
                break;

            case "converter:show" when Text("path") is { } output && _outputs.Contains(output):
                Shell(output, select: true);
                break;

            case "converter:pageToPdf":
                _ = PageToPdfAsync();
                break;

            case "converter:installLibreOffice":
                _ = InstallLibreOfficeAsync();
                break;

            case "converter:downloadLibreOffice":
                _openInNewTab(LibreOffice.DownloadPage);
                break;
        }
    }

    private void PostState()
    {
        var soffice = LibreOffice.Find();
        Post(new
        {
            type = "converterState",
            theme = new
            {
                accent = ThemeService.Accent,
                accent2 = ThemeService.Current.Colors.AccentSecondary,
                overlay = ThemeService.Current.NewTab.Overlay,
            },
            libreOffice = new
            {
                installed = soffice is not null,
                version = soffice is null ? null : LibreOffice.Version(soffice),
                installing = _installing,
                progress = _installProgress,
            },
            busy = _busy,
        });
    }

    private void Pick()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true,
            Title = L.T("Dönüştürülecek dosyaları seç"),
            Filter = L.T("Desteklenen dosyalar") + "|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.ico;*.pdf;*.txt;*.md;*.markdown;*.html;*.htm;"
                     + "*.docx;*.doc;*.odt;*.rtf;*.pptx;*.ppt;*.pps;*.ppsx;*.odp;*.xlsx;*.xls;*.ods;*.csv|"
                     + L.T("Tüm dosyalar") + "|*.*",
        };
        if (dialog.ShowDialog(System.Windows.Application.Current.MainWindow) == true)
            AddFiles(dialog.FileNames);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var files = new List<object>();
        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).Take(50))
        {
            _allowedSources.Add(path);
            files.Add(new
            {
                path,
                name = Path.GetFileName(path),
                size = new FileInfo(path).Length,
                kind = ConversionTargets.KindOf(path),
                targets = ConversionTargets.For(path).Select(t => new { format = t.Format, office = t.NeedsLibreOffice }),
            });
        }
        Post(new { type = "converterFiles", files });
    }

    private async Task ConvertAsync(List<string> paths, string target, bool merge)
    {
        if (_busy)
            return;

        paths = paths.Where(_allowedSources.Contains).ToList();
        if (paths.Count == 0 || target.Length == 0)
            return;

        _busy = true;
        var results = new List<object>();
        try
        {
            // Several images into a single PDF.
            if (merge && target == "pdf" && paths.Count > 1 && paths.All(ConversionTargets.IsImage))
            {
                Post(new { type = "converterProgress", index = 0, total = 1, name = Path.GetFileName(paths[0]) });
                var output = ConversionTargets.OutputPathFor(paths[0], "pdf");
                results.Add(await RunAsync(paths[0], output, () => ImageConverter.ImagesToPdfAsync(paths, output)));
            }
            else
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    var source = paths[i];
                    Post(new { type = "converterProgress", index = i, total = paths.Count, name = Path.GetFileName(source) });
                    if (ConversionTargets.Find(source, target) is not { } conversion)
                    {
                        results.Add(Failure(source, L.T("Bu dosya {0} biçimine dönüştürülemiyor.", target.ToUpperInvariant())));
                        continue;
                    }

                    var output = ConversionTargets.OutputPathFor(source, conversion.Format);
                    results.Add(await RunAsync(source, output, () => Convert(source, conversion, output)));
                }
            }
        }
        finally
        {
            _busy = false;
        }

        SoundService.Play(UiSound.Toggle);
        Post(new { type = "converterResults", results });
    }

    private static Task Convert(string source, ConversionTarget target, string output)
    {
        var ext = ConversionTargets.Extension(source);
        if (target.NeedsLibreOffice)
            return LibreOffice.ConvertAsync(source, target.Format, output);

        if (ConversionTargets.IsImage(source))
            return target.Format == "pdf"
                ? ImageConverter.ImagesToPdfAsync([source], output)
                : ImageConverter.ConvertAsync(source, output, target.Format);

        var markdown = ext is ".md" or ".markdown";
        var title = Path.GetFileNameWithoutExtension(source);
        return (ext, target.Format) switch
        {
            (".pdf", "docx") => DocumentConverter.PdfToDocxAsync(source, output),
            (".pdf", "txt") => DocumentConverter.PdfToTextAsync(source, output),
            (".docx", "txt") => DocumentConverter.DocxToTextAsync(source, output),
            (_, "docx") => DocumentConverter.TextToDocxAsync(source, output, markdown),
            (".html" or ".htm", "pdf") => HtmlPdfPrinter.PrintFileAsync(source, output),
            (_, "pdf") when markdown => HtmlPdfPrinter.PrintHtmlAsync(Markdown.ToHtmlDocument(File.ReadAllText(source), title), output),
            (_, "pdf") => HtmlPdfPrinter.PrintHtmlAsync(Markdown.TextDocument(File.ReadAllText(source), title), output),
            (_, "html") => File.WriteAllTextAsync(output, Markdown.ToHtmlDocument(File.ReadAllText(source), title)),
            _ => throw new NotSupportedException(target.Format),
        };
    }

    private async Task<object> RunAsync(string source, string output, Func<Task> work)
    {
        try
        {
            await work();
            _outputs.Add(output);
            Log.Info($"Dönüştürüldü: {source} -> {output}");
            return new { source = Path.GetFileName(source), ok = true, output, name = Path.GetFileName(output) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or NotSupportedException or FileFormatException or COMException
                                       or ArgumentException or TimeoutException or System.Xml.XmlException
                                       or UglyToad.PdfPig.Core.PdfDocumentFormatException)
        {
            Log.Error($"Dönüştürme başarısız: {source}", ex);
            TryDelete(output);
            var message = ex is NotSupportedException or FileFormatException or COMException
                ? L.T("Dosya okunamadı ya da bu biçim desteklenmiyor.")
                : ex.Message;
            return Failure(source, message);
        }
    }

    private static object Failure(string source, string error)
        => new { source = Path.GetFileName(source), ok = false, error };

    private async Task PageToPdfAsync()
    {
        if (_activeTab() is not { Core: { } core } tab || InternalPages.IsInternal(tab.Url))
        {
            Post(new { type = "converterResults", results = new[] { Failure(L.T("Sayfa"), L.T("Önce PDF yapmak istediğin sayfayı aç.")) } });
            return;
        }

        var name = string.Concat((tab.Title.Length > 0 ? tab.Title : "sayfa").Split(Path.GetInvalidFileNameChars())).Trim();
        if (name.Length > 80)
            name = name[..80];
        var output = ConversionTargets.Unique(Path.Combine(ConversionTargets.DownloadsFolder, name + ".pdf"));
        Post(new { type = "converterProgress", index = 0, total = 1, name = tab.Title });
        var result = await RunAsync(tab.Title, output, async () =>
        {
            var settings = core.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            if (!await core.PrintToPdfAsync(output, settings))
                throw new IOException(L.T("PDF yazılamadı."));
        });
        Post(new { type = "converterResults", results = new[] { result } });
    }

    private async Task InstallLibreOfficeAsync()
    {
        if (_installing || LibreOffice.Find() is not null)
        {
            PostState();
            return;
        }

        _installing = true;
        _installProgress = null;
        PostState();
        var lastPost = DateTime.MinValue;
        var started = await LibreOffice.InstallAsync(progress => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            _installProgress = progress;
            if (DateTime.UtcNow - lastPost > TimeSpan.FromMilliseconds(300))
            {
                lastPost = DateTime.UtcNow;
                PostState();
            }
        }));
        _installing = false;
        _installProgress = null;

        if (!started)
        {
            // No winget on this PC: the download page instead.
            _openInNewTab(LibreOffice.DownloadPage);
            Post(new { type = "converterInstallResult", ok = false, error = L.T("winget bulunamadı; indirme sayfası açıldı.") });
        }
        else
        {
            var ok = LibreOffice.Find() is not null;
            Post(new { type = "converterInstallResult", ok, error = ok ? null : L.T("Kurulum tamamlanmadı. İzin penceresini onayladığından emin ol ya da indirme sayfasını dene.") });
        }
        PostState();
    }

    private static void Shell(string path, bool select)
    {
        try
        {
            if (select)
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            else
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            Log.Error($"Dosya açılamadı: {path}", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Half-written output stays; the error is already shown.
        }
    }

    private void Post(object payload) => Page?.PostToInternalPage(payload);
}
