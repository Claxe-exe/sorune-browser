namespace Claxe.Services;

public sealed record SearchEngine(string Id, string Name, string QueryTemplate, string? SuggestTemplate = null)
{
    public string BuildUrl(string query) => QueryTemplate.Replace("{q}", Uri.EscapeDataString(query));
}

public static class SearchEngines
{
    public static IReadOnlyList<SearchEngine> All { get; } =
    [
        new("google", "Google", "https://www.google.com/search?q={q}",
            "https://suggestqueries.google.com/complete/search?client=firefox&ie=utf-8&oe=utf-8&hl={lang}&q={q}"),
        new("duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q={q}", "https://duckduckgo.com/ac/?type=list&q={q}"),
        new("bing", "Bing", "https://www.bing.com/search?q={q}", "https://api.bing.com/osjson.aspx?query={q}"),
        new("brave", "Brave Search", "https://search.brave.com/search?q={q}", "https://search.brave.com/api/suggest?q={q}"),
    ];

    public static SearchEngine Default => All[0];

    public static SearchEngine Find(string? id)
        => All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
