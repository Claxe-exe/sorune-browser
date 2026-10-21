using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Claxe.Services.Conversion;

/// <summary>
/// A small Markdown renderer for the converter: headings, paragraphs, lists, quotes, code,
/// rules, bold/italic, inline code and links. Raw HTML in the source is shown as text.
/// </summary>
public static partial class Markdown
{
    public static string ToHtmlDocument(string markdown, string title)
        => Document(title, ToHtml(markdown));

    /// <summary>A plain text file as a readable page (wrapped, monospace).</summary>
    public static string TextDocument(string text, string title)
        => Document(title, $"<pre>{WebUtility.HtmlEncode(text)}</pre>");

    public static string StripInline(string line)
        => LinkPattern().Replace(BoldItalicPattern().Replace(line, "$2"), "$1").Replace("`", "");

    public static string ToHtml(string markdown)
    {
        var html = new StringBuilder();
        var paragraph = new List<string>();
        string? list = null;
        var inCode = false;

        void FlushParagraph()
        {
            if (paragraph.Count > 0)
                html.Append("<p>").Append(Inline(string.Join(" ", paragraph))).Append("</p>\n");
            paragraph.Clear();
        }

        void CloseList()
        {
            if (list is not null)
                html.Append($"</{list}>\n");
            list = null;
        }

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                html.Append(inCode ? "</code></pre>\n" : "<pre><code>");
                inCode = !inCode;
                continue;
            }
            if (inCode)
            {
                html.Append(WebUtility.HtmlEncode(rawLine)).Append('\n');
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.Length == 0)
            {
                FlushParagraph();
                CloseList();
                continue;
            }

            var level = trimmed.TakeWhile(c => c == '#').Count();
            if (level is > 0 and <= 6 && trimmed.Length > level && trimmed[level] == ' ')
            {
                FlushParagraph();
                CloseList();
                html.Append($"<h{level}>{Inline(trimmed[(level + 1)..].Trim())}</h{level}>\n");
            }
            else if (trimmed is "---" or "***" or "___")
            {
                FlushParagraph();
                CloseList();
                html.Append("<hr>\n");
            }
            else if (trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                FlushParagraph();
                CloseList();
                html.Append($"<blockquote>{Inline(trimmed[2..])}</blockquote>\n");
            }
            else if (UnorderedPattern().Match(trimmed) is { Success: true } bullet)
            {
                FlushParagraph();
                if (list != "ul") { CloseList(); html.Append("<ul>\n"); list = "ul"; }
                html.Append($"<li>{Inline(bullet.Groups[1].Value)}</li>\n");
            }
            else if (OrderedPattern().Match(trimmed) is { Success: true } numbered)
            {
                FlushParagraph();
                if (list != "ol") { CloseList(); html.Append("<ol>\n"); list = "ol"; }
                html.Append($"<li>{Inline(numbered.Groups[1].Value)}</li>\n");
            }
            else
            {
                CloseList();
                paragraph.Add(trimmed);
            }
        }

        FlushParagraph();
        CloseList();
        if (inCode)
            html.Append("</code></pre>\n");
        return html.ToString();
    }

    private static string Inline(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        encoded = CodePattern().Replace(encoded, "<code>$1</code>");
        encoded = StrongPattern().Replace(encoded, "<strong>$2</strong>");
        encoded = EmphasisPattern().Replace(encoded, "<em>$2</em>");
        // Only web links become anchors.
        return EncodedLinkPattern().Replace(encoded, "<a href=\"$2\">$1</a>");
    }

    private static string Document(string title, string body) => $$"""
        <!doctype html>
        <html><head><meta charset="utf-8"><title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
          body { font-family: "Segoe UI", sans-serif; font-size: 11pt; line-height: 1.55; color: #1b1b1f; margin: 0; }
          h1, h2, h3 { line-height: 1.25; margin: 1.2em 0 0.4em; }
          h1 { font-size: 22pt; } h2 { font-size: 17pt; } h3 { font-size: 13.5pt; }
          pre { white-space: pre-wrap; word-wrap: break-word; font-family: "Cascadia Mono", Consolas, monospace; font-size: 9.5pt; }
          pre code { display: block; background: #f3f2f7; padding: 10px 12px; border-radius: 6px; }
          code { font-family: "Cascadia Mono", Consolas, monospace; background: #f3f2f7; padding: 0 3px; border-radius: 3px; }
          blockquote { margin: 0.6em 0; padding: 2px 12px; border-left: 3px solid #b583ff; color: #4a4558; }
          a { color: #6b3fd1; } hr { border: 0; border-top: 1px solid #ddd; }
        </style></head>
        <body>{{body}}</body></html>
        """;

    [GeneratedRegex(@"^[-*+]\s+(.*)$")]
    private static partial Regex UnorderedPattern();

    [GeneratedRegex(@"^\d+[.)]\s+(.*)$")]
    private static partial Regex OrderedPattern();

    [GeneratedRegex(@"`([^`]+)`")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"(\*\*|__)(.+?)\1")]
    private static partial Regex StrongPattern();

    [GeneratedRegex(@"(\*|_)(.+?)\1")]
    private static partial Regex EmphasisPattern();

    [GeneratedRegex(@"(\*\*|__|\*|_)(.+?)\1")]
    private static partial Regex BoldItalicPattern();

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex LinkPattern();

    [GeneratedRegex(@"\[([^\]]+)\]\((https?://[^)\s]+)\)")]
    private static partial Regex EncodedLinkPattern();
}
