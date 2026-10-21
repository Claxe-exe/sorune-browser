using System.IO;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Claxe.Services.Conversion;

/// <summary>
/// Conversions that need no Office: PDF to Word/text (text and pictures, not the exact
/// layout), Word to text, and text/Markdown to Word.
/// </summary>
public static class DocumentConverter
{
    private const long EmuPerPoint = 12700;
    private const long MaxImageWidthEmu = 6L * 914400; // 6 inches: fits an A4/Letter page

    private sealed record Block(string Text, double FontSize, bool Bold);

    /// <summary>PDF to .docx: paragraphs in reading order, larger text as headings, embedded pictures.</summary>
    public static Task PdfToDocxAsync(string source, string output) => Task.Run(() =>
    {
        using var pdf = PdfDocument.Open(source);
        using var docx = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document);
        var main = docx.AddMainDocumentPart();
        main.Document = new W.Document(new W.Body());
        var body = main.Document.Body!;

        var pages = pdf.GetPages().Select(page => (Page: page, Blocks: ReadBlocks(page))).ToList();
        var sizes = pages.SelectMany(p => p.Blocks).Select(b => b.FontSize).Where(s => s > 0).Order().ToList();
        var bodySize = sizes.Count > 0 ? sizes[sizes.Count / 2] : 11;

        uint pictureId = 1;
        for (var i = 0; i < pages.Count; i++)
        {
            if (i > 0)
                body.Append(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));

            foreach (var block in pages[i].Blocks)
            {
                var heading = block.FontSize >= bodySize * 1.3;
                body.Append(TextParagraph(block.Text, heading || block.Bold, heading ? Math.Min(block.FontSize, 30) : null));
            }

            foreach (var image in pages[i].Page.GetImages())
            {
                byte[] bytes;
                PartTypeInfo type;
                if (image.TryGetPng(out var png))
                {
                    bytes = png;
                    type = ImagePartType.Png;
                }
                else if (image.RawMemory is { Length: > 2 } raw && raw.Span[0] == 0xFF && raw.Span[1] == 0xD8)
                {
                    bytes = raw.ToArray();
                    type = ImagePartType.Jpeg;
                }
                else
                {
                    continue; // a format PdfPig can't hand over (e.g. JBIG2)
                }

                var width = (long)(image.BoundingBox.Width * EmuPerPoint);
                var height = (long)(image.BoundingBox.Height * EmuPerPoint);
                if (width <= 0 || height <= 0)
                    continue;
                if (width > MaxImageWidthEmu)
                {
                    height = height * MaxImageWidthEmu / width;
                    width = MaxImageWidthEmu;
                }

                var part = main.AddImagePart(type);
                using (var stream = new MemoryStream(bytes))
                    part.FeedData(stream);
                body.Append(new W.Paragraph(new W.Run(Picture(main.GetIdOfPart(part), width, height, pictureId++))));
            }
        }

        if (pages.Count == 0 || !body.Elements<W.Paragraph>().Any())
            body.Append(new W.Paragraph());
        main.Document.Save();
    });

    public static Task PdfToTextAsync(string source, string output) => Task.Run(() =>
    {
        using var pdf = PdfDocument.Open(source);
        var text = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            if (text.Length > 0)
                text.Append("\n\n");
            text.AppendJoin("\n\n", ReadBlocks(page).Select(b => b.Text));
        }
        File.WriteAllText(output, text.ToString(), new UTF8Encoding(true));
    });

    public static Task DocxToTextAsync(string source, string output) => Task.Run(() =>
    {
        using var docx = WordprocessingDocument.Open(source, false);
        var paragraphs = docx.MainDocumentPart?.Document?.Body?.Descendants<W.Paragraph>().Select(p => p.InnerText) ?? [];
        File.WriteAllText(output, string.Join("\n", paragraphs), new UTF8Encoding(true));
    });

    /// <summary>Plain text or Markdown to .docx; Markdown headings become larger bold lines.</summary>
    public static Task TextToDocxAsync(string source, string output, bool markdown) => Task.Run(() =>
    {
        var lines = File.ReadAllLines(source);
        using var docx = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document);
        var main = docx.AddMainDocumentPart();
        main.Document = new W.Document(new W.Body());
        var body = main.Document.Body!;

        foreach (var line in lines)
        {
            var level = markdown ? line.TakeWhile(c => c == '#').Count() : 0;
            if (level is > 0 and <= 6 && line.Length > level && line[level] == ' ')
                body.Append(TextParagraph(line[(level + 1)..].Trim(), bold: true, fontSize: 24 - level * 2));
            else
                body.Append(TextParagraph(markdown ? Markdown.StripInline(line) : line, bold: false, fontSize: null));
        }

        if (lines.Length == 0)
            body.Append(new W.Paragraph());
        main.Document.Save();
    });

    /// <summary>A page's text blocks in reading order (columns handled), with their font size.</summary>
    private static List<Block> ReadBlocks(UglyToad.PdfPig.Content.Page page)
    {
        var words = page.GetWords().ToList();
        if (words.Count == 0)
            return [];

        var blocks = UnsupervisedReadingOrderDetector.Instance.Get(DocstrumBoundingBoxes.Instance.GetBlocks(words));
        var result = new List<Block>();
        foreach (var block in blocks)
        {
            var text = string.Join(" ", block.TextLines.Select(l => l.Text.Trim())).Trim();
            if (text.Length == 0)
                continue;

            var letters = block.TextLines[0].Words.SelectMany(w => w.Letters).ToList();
            var size = letters.Count > 0 ? letters.Average(l => l.PointSize) : 0;
            var bold = letters.Count > 0 && letters.All(l => l.FontName?.Contains("Bold", StringComparison.OrdinalIgnoreCase) == true);
            result.Add(new Block(text, size, bold));
        }
        return result;
    }

    private static W.Paragraph TextParagraph(string text, bool bold, double? fontSize)
    {
        var run = new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
        if (bold || fontSize is not null)
        {
            var properties = new W.RunProperties();
            if (bold)
                properties.Append(new W.Bold());
            if (fontSize is { } size)
                properties.Append(new W.FontSize { Val = ((int)Math.Round(size * 2)).ToString() });
            run.PrependChild(properties);
        }
        return new W.Paragraph(run);
    }

    private static W.Drawing Picture(string relationshipId, long width, long height, uint id)
        => new(
            new DW.Inline(
                new DW.Extent { Cx = width, Cy = height },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = id, Name = $"Picture {id}" },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = 0U, Name = $"image{id}" },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = width, Cy = height }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                    { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });
}
