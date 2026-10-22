using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

/// <summary>
/// Translates a page in place with Google Translate: the page's text nodes are read, sent to
/// Google in batches and written back, keeping the originals so "show original" restores them.
/// The page itself, its layout and the user's sign-in stay untouched (no proxy page).
/// Only the visible text goes to Google; never form fields, code or editable areas.
/// </summary>
public static class PageTranslator
{
    private static readonly HttpClient Http = CreateClient();

    private const int MaxBatchChars = 4500;
    private const int MaxBatchItems = 120;

    /// <summary>
    /// Collects text nodes not handled yet (so it also picks up content the page adds later) and
    /// returns [[id, text], …]. Defines apply/restore on first use.
    /// </summary>
    private const string CollectScript = """
        (() => {
            const s = window.__soruneTr || (window.__soruneTr = { nodes: [], originals: new Map(), seen: new WeakSet() });
            if (!s.apply) {
                s.apply = (pairs) => {
                    for (const [id, text] of pairs) {
                        const node = s.nodes[id];
                        if (!node || !node.isConnected) continue;
                        if (!s.originals.has(node)) s.originals.set(node, node.nodeValue);
                        const raw = s.originals.get(node);
                        node.nodeValue = raw.match(/^\s*/)[0] + text + raw.match(/\s*$/)[0];
                    }
                    return true;
                };
                s.restore = () => {
                    for (const [node, text] of s.originals) if (node.isConnected) node.nodeValue = text;
                    s.originals.clear();
                    s.nodes = [];
                    s.seen = new WeakSet();
                    return true;
                };
            }
            const skip = new Set(["SCRIPT", "STYLE", "NOSCRIPT", "TEXTAREA", "CODE", "PRE", "KBD", "SAMP", "INPUT", "SELECT", "OPTION", "SVG", "MATH"]);
            const out = [];
            let chars = 0;
            const walker = document.createTreeWalker(document.body || document.documentElement, NodeFilter.SHOW_TEXT, {
                acceptNode(node) {
                    if (s.seen.has(node) || !/\p{L}/u.test(node.nodeValue || "")) return NodeFilter.FILTER_REJECT;
                    for (let el = node.parentElement; el; el = el.parentElement) {
                        if (skip.has(el.tagName.toUpperCase()) || el.isContentEditable
                            || el.getAttribute("translate") === "no" || el.classList.contains("notranslate")) return NodeFilter.FILTER_REJECT;
                    }
                    return NodeFilter.FILTER_ACCEPT;
                },
            });
            while (out.length < 800 && chars < 80000 && walker.nextNode()) {
                const node = walker.currentNode;
                s.seen.add(node);
                const text = node.nodeValue.trim().slice(0, 1500);
                out.push([s.nodes.push(node) - 1, text]);
                chars += text.length;
            }
            return out;
        })()
        """;

    public const string RestoreScript = "window.__soruneTr ? window.__soruneTr.restore() : false";

    public sealed record Result(int Translated, string? SourceLanguage);

    /// <summary>
    /// Translates whatever text the page shows that wasn't translated yet. Returns the detected
    /// source language (most common among the batches) and how many pieces were replaced.
    /// </summary>
    public static async Task<Result> TranslateNewTextAsync(CoreWebView2 core, string target, CancellationToken cancel)
    {
        var json = await core.ExecuteScriptAsync(CollectScript);
        var items = ParsePairs(json);
        if (items.Count == 0)
            return new Result(0, null);

        var languages = new Dictionary<string, int>();
        var translated = 0;
        foreach (var batch in Batches(items))
        {
            cancel.ThrowIfCancellationRequested();
            var answers = await TranslateAsync(batch.Select(i => i.Text).ToList(), target, cancel);
            var pairs = new List<object[]>();
            for (var i = 0; i < Math.Min(answers.Count, batch.Count); i++)
            {
                var (text, language) = answers[i];
                if (language is not null)
                    languages[language] = languages.GetValueOrDefault(language) + batch[i].Text.Length;
                if (!string.IsNullOrEmpty(text) && text != batch[i].Text)
                    pairs.Add([batch[i].Id, text]);
            }
            if (pairs.Count > 0)
            {
                await core.ExecuteScriptAsync($"window.__soruneTr && window.__soruneTr.apply({JsonSerializer.Serialize(pairs)})");
                translated += pairs.Count;
            }
        }

        var source = languages.Count == 0 ? null : languages.MaxBy(l => l.Value).Key;
        return new Result(translated, source);
    }

    /// <summary>Google's answer for each text: (translation, detected language).</summary>
    public static async Task<List<(string Text, string? Language)>> TranslateAsync(IReadOnlyList<string> texts, string target, CancellationToken cancel)
    {
        var url = $"https://translate.googleapis.com/translate_a/t?client=gtx&sl=auto&tl={Uri.EscapeDataString(target)}";
        using var content = new FormUrlEncodedContent(texts.Select(t => new KeyValuePair<string, string>("q", t)));
        using var response = await Http.PostAsync(url, content, cancel);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancel));
        return Parse(doc.RootElement);
    }

    /// <summary>Accepts both shapes Google uses: [["text","en"], …] and ["text", …].</summary>
    public static List<(string Text, string? Language)> Parse(JsonElement root)
    {
        var list = new List<(string, string?)>();
        if (root.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
                list.Add((WebUtility.HtmlDecode(item.GetString() ?? ""), null));
            else if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0 && item[0].ValueKind == JsonValueKind.String)
                list.Add((WebUtility.HtmlDecode(item[0].GetString() ?? ""),
                          item.GetArrayLength() > 1 && item[1].ValueKind == JsonValueKind.String ? item[1].GetString() : null));
            else
                list.Add(("", null));
        }
        return list;
    }

    /// <summary>The language's name in the interface language ("en" → "İngilizce" / "English").</summary>
    public static string LanguageName(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return L.T("Bilinmeyen dil");
        var key = code.ToLowerInvariant();
        if (L.Language == "tr" && TurkishNames.TryGetValue(key.Split('-')[0], out var turkish))
            return turkish;
        try
        {
            // Unknown codes still give a culture whose name is just the code.
            var name = CultureInfo.GetCultureInfo(key).EnglishName;
            return string.Equals(name, key, StringComparison.OrdinalIgnoreCase) ? key.ToUpperInvariant() : name;
        }
        catch (CultureNotFoundException)
        {
            return key.ToUpperInvariant();
        }
    }

    private static readonly Dictionary<string, string> TurkishNames = new()
    {
        ["en"] = "İngilizce", ["tr"] = "Türkçe", ["de"] = "Almanca", ["fr"] = "Fransızca", ["es"] = "İspanyolca",
        ["it"] = "İtalyanca", ["pt"] = "Portekizce", ["ru"] = "Rusça", ["ja"] = "Japonca", ["ko"] = "Korece",
        ["zh"] = "Çince", ["ar"] = "Arapça", ["fa"] = "Farsça", ["nl"] = "Felemenkçe", ["pl"] = "Lehçe",
        ["uk"] = "Ukraynaca", ["el"] = "Yunanca", ["sv"] = "İsveççe", ["no"] = "Norveççe", ["da"] = "Danca",
        ["fi"] = "Fince", ["cs"] = "Çekçe", ["hu"] = "Macarca", ["ro"] = "Romence", ["bg"] = "Bulgarca",
        ["az"] = "Azerbaycanca", ["hi"] = "Hintçe", ["id"] = "Endonezyaca", ["vi"] = "Vietnamca", ["th"] = "Tayca",
        ["he"] = "İbranice", ["iw"] = "İbranice", ["ka"] = "Gürcüce", ["kk"] = "Kazakça", ["sr"] = "Sırpça",
    };

    private sealed record Item(int Id, string Text);

    private static List<Item> ParsePairs(string json)
    {
        var list = new List<Item>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var pair in doc.RootElement.EnumerateArray())
        {
            if (pair.ValueKind == JsonValueKind.Array && pair.GetArrayLength() == 2
                && pair[0].TryGetInt32(out var id) && pair[1].ValueKind == JsonValueKind.String
                && pair[1].GetString() is { Length: > 0 } text)
                list.Add(new Item(id, text));
        }
        return list;
    }

    private static IEnumerable<List<Item>> Batches(List<Item> items)
    {
        var batch = new List<Item>();
        var chars = 0;
        foreach (var item in items)
        {
            if (batch.Count > 0 && (batch.Count >= MaxBatchItems || chars + item.Text.Length > MaxBatchChars))
            {
                yield return batch;
                batch = [];
                chars = 0;
            }
            batch.Add(item);
            chars += item.Text.Length;
        }
        if (batch.Count > 0)
            yield return batch;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Sorune");
        return client;
    }
}
