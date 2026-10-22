using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Claxe.Services.Conversion;

namespace Sorune.Tests;

public class ConversionTargetsTests
{
    [Fact]
    public void ImagesNeverConvertToTheirOwnFormat()
    {
        var targets = ConversionTargets.For("photo.jpeg").Select(t => t.Format).ToList();
        Assert.DoesNotContain("jpg", targets);
        Assert.Contains("png", targets);
        Assert.Contains("pdf", targets);
        Assert.All(ConversionTargets.For("photo.jpeg"), t => Assert.False(t.NeedsLibreOffice));
    }

    [Fact]
    public void OfficeFormatsNeedLibreOffice()
    {
        Assert.All(ConversionTargets.For("slides.pptx"), t => Assert.True(t.NeedsLibreOffice));
        Assert.Contains(ConversionTargets.For("letter.docx"), t => t is { Format: "txt", NeedsLibreOffice: false });
    }

    [Fact]
    public void PdfGoesToWordAndText()
        => Assert.Equal(["docx", "txt"], ConversionTargets.For("a.pdf").Select(t => t.Format));

    [Fact]
    public void UnknownFilesHaveNoTargets() => Assert.Empty(ConversionTargets.For("archive.7z"));

    [Fact]
    public void UniqueNeverReturnsAnExistingFile()
    {
        var folder = TestProfile.NewFolder();
        var path = Path.Combine(folder, "out.pdf");
        Assert.Equal(path, ConversionTargets.Unique(path));
        File.WriteAllText(path, "x");
        Assert.Equal(Path.Combine(folder, "out (2).pdf"), ConversionTargets.Unique(path));
    }
}

public class MarkdownTests
{
    [Fact]
    public void RendersBlocks()
    {
        var html = Markdown.ToHtml("# Title\n\nSome **bold** and *it*.\n\n- a\n- b\n\n```\ncode <x>\n```");
        Assert.Contains("<h1>Title</h1>", html);
        Assert.Contains("<strong>bold</strong>", html);
        Assert.Contains("<em>it</em>", html);
        Assert.Contains("<ul>\n<li>a</li>\n<li>b</li>\n</ul>", html);
        Assert.Contains("code &lt;x&gt;", html);
    }

    [Fact]
    public void RawHtmlIsShownAsText()
    {
        var html = Markdown.ToHtml("<script>alert(1)</script>");
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void OnlyWebLinksBecomeAnchors()
    {
        Assert.Contains("<a href=\"https://example.com\">x</a>", Markdown.ToHtml("[x](https://example.com)"));
        Assert.DoesNotContain("<a ", Markdown.ToHtml("[x](javascript:alert(1))"));
    }
}

public class ImageAndDocumentTests
{
    private static string MakePng(string folder)
    {
        var path = Path.Combine(folder, "in.png");
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawEllipse(Brushes.MediumPurple, null, new Point(60, 40), 50, 30);
        var bitmap = new RenderTargetBitmap(120, 80, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    private static Task<string> OnSta(Func<string> work)
    {
        var done = new TaskCompletionSource<string>();
        var thread = new Thread(() =>
        {
            try { done.SetResult(work()); }
            catch (Exception ex) { done.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    [Fact]
    public async Task ConvertsImagesToJpgIcoAndPdf()
    {
        var folder = TestProfile.NewFolder();
        var png = await OnSta(() => MakePng(folder));

        var jpg = Path.Combine(folder, "a.jpg");
        await ImageConverter.ConvertAsync(png, jpg, "jpg");
        Assert.Equal([0xFF, 0xD8], File.ReadAllBytes(jpg)[..2]);

        var ico = Path.Combine(folder, "a.ico");
        await ImageConverter.ConvertAsync(png, ico, "ico");
        Assert.Equal([0, 0, 1, 0, 4, 0], File.ReadAllBytes(ico)[..6]); // icon header with 4 sizes

        var pdf = Path.Combine(folder, "a.pdf");
        await ImageConverter.ImagesToPdfAsync([png, jpg], pdf);
        var text = File.ReadAllText(pdf, System.Text.Encoding.Latin1);
        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/Count 2", text);
        Assert.EndsWith("%%EOF\n", text);
    }

    [Fact]
    public async Task MarkdownToWordAndBack()
    {
        var folder = TestProfile.NewFolder();
        var md = Path.Combine(folder, "notes.md");
        File.WriteAllText(md, "# Başlık\n\nBir **kalın** satır.");
        var docx = Path.Combine(folder, "notes.docx");
        await DocumentConverter.TextToDocxAsync(md, docx, markdown: true);
        var txt = Path.Combine(folder, "notes.txt");
        await DocumentConverter.DocxToTextAsync(docx, txt);

        var text = File.ReadAllText(txt);
        Assert.Contains("Başlık", text);
        Assert.Contains("Bir kalın satır.", text);
    }

    [Fact]
    public async Task PdfWithImagesBecomesWord()
    {
        var folder = TestProfile.NewFolder();
        var png = await OnSta(() => MakePng(folder));
        var pdf = Path.Combine(folder, "images.pdf");
        await ImageConverter.ImagesToPdfAsync([png], pdf);

        var docx = Path.Combine(folder, "images.docx");
        await DocumentConverter.PdfToDocxAsync(pdf, docx);
        using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(docx, false);
        Assert.NotEmpty(document.MainDocumentPart!.ImageParts);
    }
}
