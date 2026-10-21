namespace Claxe.Services;

public sealed record SearchEngine(string Id, string Name, string QueryTemplate)
{
    public string BuildUrl(string query) => QueryTemplate.Replace("{q}", Uri.EscapeDataString(query));
}

public static class SearchEngines
{
    public static IReadOnlyList<SearchEngine> All { get; } =
    [
        new("google", "Google", "https://www.google.com/search?q={q}"),
        new("duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q={q}"),
        new("bing", "Bing", "https://www.bing.com/search?q={q}"),
        new("brave", "Brave Search", "https://search.brave.com/search?q={q}"),
    ];

    public static SearchEngine Default => All[0];

    public static SearchEngine Find(string? id)
        => All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Default;
}
