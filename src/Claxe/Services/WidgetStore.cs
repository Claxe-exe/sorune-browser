using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Claxe.Services;

/// <summary>
/// New tab widgets (widgets.dat, DPAPI-encrypted: notes and to-dos can be private). The page
/// sends the whole list; only known widget types and fields are kept, with length limits.
/// </summary>
public sealed partial class WidgetStore
{
    public const int MaxWidgets = 12;

    private static readonly Dictionary<string, Func<JsonElement, JsonObject>> Types = new()
    {
        ["clock"] = w => new JsonObject
        {
            ["name"] = Str(w, "name", 40),
            ["seconds"] = Bool(w, "seconds"),
            ["hour12"] = Bool(w, "hour12"),
            ["hidden"] = Bool(w, "hidden"),
        },
        ["weather"] = w => new JsonObject
        {
            ["city"] = Str(w, "city", 80),
            ["unit"] = Str(w, "unit", 1) == "f" ? "f" : "c",
        },
        ["todo"] = w => new JsonObject
        {
            ["title"] = Str(w, "title", 40),
            ["items"] = new JsonArray(Array(w, "items").Take(50)
                .Select(i => (JsonNode)new JsonObject { ["text"] = Str(i, "text", 200), ["done"] = Bool(i, "done") })
                .ToArray()),
        },
        ["note"] = w => new JsonObject
        {
            ["title"] = Str(w, "title", 40),
            ["text"] = Str(w, "text", 5000),
        },
        ["countdown"] = w => new JsonObject
        {
            ["title"] = Str(w, "title", 60),
            ["date"] = Str(w, "date", 30),
        },
        ["timer"] = w => new JsonObject
        {
            ["focus"] = Int(w, "focus", 1, 120, 25),
            ["rest"] = Int(w, "rest", 1, 60, 5),
        },
    };

    private JsonArray _items;

    public WidgetStore()
    {
        _items = JsonStore.LoadProtected<JsonArray>(FilePath) is { } saved
            ? Sanitize(JsonSerializer.SerializeToElement(saved))
            : Defaults();
    }

    public JsonArray Items => _items;

    /// <summary>Replaces the list with the cleaned-up version of what the page sent.</summary>
    public JsonArray Save(JsonElement widgets)
    {
        _items = Sanitize(widgets);
        try
        {
            JsonStore.SaveProtected(FilePath, _items);
        }
        catch (IOException ex)
        {
            Log.Error("Widget'lar kaydedilemedi.", ex);
        }
        return _items;
    }

    private static JsonArray Defaults() =>
    [
        new JsonObject { ["id"] = "clock", ["type"] = "clock", ["name"] = "", ["seconds"] = false, ["hour12"] = false, ["hidden"] = false },
        new JsonObject { ["id"] = "weather", ["type"] = "weather", ["city"] = "", ["unit"] = "c" },
        new JsonObject { ["id"] = "todo", ["type"] = "todo", ["title"] = "", ["items"] = new JsonArray() },
    ];

    private static JsonArray Sanitize(JsonElement widgets)
    {
        var result = new JsonArray();
        if (widgets.ValueKind != JsonValueKind.Array)
            return Defaults();

        var ids = new HashSet<string>();
        foreach (var widget in widgets.EnumerateArray())
        {
            if (result.Count >= MaxWidgets || widget.ValueKind != JsonValueKind.Object)
                break;

            var type = Str(widget, "type", 20);
            var id = Str(widget, "id", 40);
            if (!Types.TryGetValue(type, out var clean) || !IdPattern().IsMatch(id) || !ids.Add(id))
                continue;
            // One clock: it is the big one at the top of the page.
            if (type == "clock" && result.Any(w => (string?)w!["type"] == "clock"))
                continue;

            var item = clean(widget);
            item.Insert(0, "type", type);
            item.Insert(0, "id", id);
            result.Add(item);
        }

        if (!result.Any(w => (string?)w!["type"] == "clock"))
            result.Insert(0, Defaults()[0]!.DeepClone());
        return result;
    }

    private static string Str(JsonElement e, string name, int max)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!.Length > max ? v.GetString()![..max] : v.GetString()!
            : "";

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement e, string name, int min, int max, int fallback)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? Math.Clamp(i, min, max)
            : fallback;

    private static IEnumerable<JsonElement> Array(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object)
            : [];

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex IdPattern();

    private static string FilePath => Path.Combine(AppPaths.DataRoot, "widgets.dat");
}
