using System.Net.Http;
using System.Text.Json;

namespace Claxe.Services;

/// <summary>
/// Search suggestions from the chosen search engine (OpenSearch JSON: ["query", ["s1", "s2", …]]).
/// Only what is typed in the address bar is sent, and only when the setting is on and the tab is
/// not InPrivate.
/// </summary>
public static class SearchSuggestions
{
    private static readonly HttpClient Http = CreateClient();

    public static async Task<IReadOnlyList<string>> FetchAsync(SearchEngine engine, string query, CancellationToken cancel)
    {
        if (engine.SuggestTemplate is null || query.Length is 0 or > 200)
            return [];

        try
        {
            var url = engine.SuggestTemplate
                .Replace("{q}", Uri.EscapeDataString(query))
                .Replace("{lang}", L.Language);
            using var response = await Http.GetAsync(url, cancel);
            if (!response.IsSuccessStatusCode)
                return [];

            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(cancel));
            if (json.RootElement is not { ValueKind: JsonValueKind.Array } root || root.GetArrayLength() < 2
                || root[1].ValueKind != JsonValueKind.Array)
                return [];

            return root[1].EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!.Trim())
                .Where(text => text.Length is > 0 and <= 200)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return []; // offline, slow or an odd answer: the local suggestions are enough
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Sorune");
        return client;
    }
}
