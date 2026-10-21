using System.IO;
using System.Text.Json;

namespace Claxe.Services;

/// <summary>
/// Interface language. Turkish text is the key; Assets/i18n/{lang}.json maps it to the other
/// language, and anything missing simply stays Turkish. The same dictionary is handed to the
/// internal pages, so WPF, C# and HTML all translate from one file. Chosen once at startup.
/// </summary>
public static class L
{
    private static Dictionary<string, string> _strings = [];

    /// <summary>"tr" or "en".</summary>
    public static string Language { get; private set; } = "tr";

    /// <summary>BCP 47 tag for dates, numbers and WebView2.</summary>
    public static string Locale => Language == "en" ? "en-US" : "tr-TR";

    public static IReadOnlyDictionary<string, string> Strings => _strings;

    public static void Load(string? language)
    {
        Language = language == "en" ? "en" : "tr";
        _strings = [];
        if (Language == "tr")
            return;

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "i18n", Language + ".json");
        try
        {
            _strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error($"Dil dosyası okunamadı: {path}", ex);
        }
    }

    /// <summary>Translates Turkish <paramref name="text"/>; {0}, {1} ... are filled from <paramref name="args"/>.</summary>
    public static string T(string text, params object?[] args)
    {
        var result = _strings.TryGetValue(text, out var translated) ? translated : text;
        return args.Length == 0 ? result : string.Format(result, args);
    }
}
