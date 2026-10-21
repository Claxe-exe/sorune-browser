using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Claxe.Services;

public static class JsonStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Returns null when the file does not exist. A corrupt file is moved aside
    /// (so it can be inspected) and also yields null.
    /// </summary>
    public static T? Load<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            var backup = path + ".corrupt";
            Log.Error($"Bozuk JSON dosyası '{path}', '{backup}' olarak saklandı.", ex);
            File.Move(path, backup, overwrite: true);
            return null;
        }
    }

    /// <summary>Writes to a temp file first so a crash never leaves a half-written file.</summary>
    public static void Save<T>(string path, T value)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}
