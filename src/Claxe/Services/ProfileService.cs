using System.Diagnostics;
using System.IO;

namespace Claxe.Services;

public sealed class ProfileInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Emoji { get; set; } = "🌸";
    public string Color { get; set; } = "#B583FF";
}

/// <summary>
/// Local profiles: each one is a complete, separate Sorune data folder (cookies, logins, tabs,
/// history, settings, extensions) and runs in its own window and process. The default profile is
/// the main data folder; others live in its "profiles" folder. A profile window knows the main
/// folder and its own id from environment variables set when it is launched.
/// </summary>
public static class ProfileService
{
    public const string DefaultId = "default";
    private const string BaseVariable = "SORUNE_PROFILE_BASE";
    private const string IdVariable = "SORUNE_PROFILE_ID";
    private const int MaxProfiles = 12;

    public static readonly string[] Colors = ["#B583FF", "#FF7BC8", "#FF6B7A", "#FF9F5A", "#FFD05A", "#5BD68B", "#5FE0E6", "#6AA6FF"];

    /// <summary>The main data folder that holds profiles.json and the other profiles.</summary>
    public static string BaseRoot { get; } = Environment.GetEnvironmentVariable(BaseVariable) is { Length: > 0 } root
        ? Path.GetFullPath(root)
        : AppPaths.DataRoot;

    public static string CurrentId { get; } = Environment.GetEnvironmentVariable(IdVariable) is { Length: > 0 } id ? id : DefaultId;

    public static bool IsDefault => CurrentId == DefaultId;

    private static string RegistryFile => Path.Combine(BaseRoot, "profiles.json");

    public static ProfileInfo Current => List().FirstOrDefault(p => p.Id == CurrentId) ?? Fallback(CurrentId);

    public static List<ProfileInfo> List()
    {
        var saved = JsonStore.Load<List<ProfileInfo>>(RegistryFile) ?? [];
        if (!saved.Any(p => p.Id == DefaultId))
            saved.Insert(0, Fallback(DefaultId));
        saved.RemoveAll(p => p.Id != DefaultId && !IsValidId(p.Id));
        return saved;
    }

    public static string DataRootOf(string id) => id == DefaultId ? BaseRoot : Path.Combine(BaseRoot, "profiles", id);

    public static bool IsRunning(string id) => id == CurrentId || SingleInstance.IsRunning(DataRootOf(id));

    public static ProfileInfo? Create(string? name, string? emoji, string? color)
    {
        var profiles = List();
        name = Clean(name, 30);
        if (name.Length == 0 || profiles.Count >= MaxProfiles)
            return null;

        var profile = new ProfileInfo
        {
            Id = Guid.NewGuid().ToString("N")[..10],
            Name = name,
            Emoji = Clean(emoji, 8) is { Length: > 0 } e ? e : "🌸",
            Color = IsColor(color) ? color!.ToUpperInvariant() : Colors[profiles.Count % Colors.Length],
        };
        profiles.Add(profile);
        Save(profiles);
        Directory.CreateDirectory(DataRootOf(profile.Id));
        return profile;
    }

    public static bool Update(string? id, string? name, string? emoji, string? color)
    {
        var profiles = List();
        if (profiles.FirstOrDefault(p => p.Id == id) is not { } profile)
            return false;

        if (Clean(name, 30) is { Length: > 0 } newName)
            profile.Name = newName;
        if (Clean(emoji, 8) is { Length: > 0 } newEmoji)
            profile.Emoji = newEmoji;
        if (IsColor(color))
            profile.Color = color!.ToUpperInvariant();
        Save(profiles);
        return true;
    }

    /// <summary>Deletes a profile and all its data. Not the main profile, not one that is open.</summary>
    public static bool Delete(string? id)
    {
        if (id is null || id == DefaultId || !IsValidId(id) || IsRunning(id))
            return false;

        var profiles = List();
        if (profiles.RemoveAll(p => p.Id == id) == 0)
            return false;
        Save(profiles);

        try
        {
            var folder = DataRootOf(id);
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Profil klasörü silinemedi: {id}", ex);
        }
        return true;
    }

    /// <summary>Opens the profile's window (or brings it forward when it is already open).</summary>
    public static void Launch(string id, IEnumerable<string>? args = null)
    {
        if (Environment.ProcessPath is not { } exe || !List().Any(p => p.Id == id))
            return;

        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        foreach (var arg in args ?? [])
            start.ArgumentList.Add(arg);
        // The child must not inherit this window's profile folder.
        start.Environment.Remove("SORUNE_PROFILE_DIR");
        start.Environment.Remove("CLAXE_PROFILE_DIR");
        start.Environment[BaseVariable] = BaseRoot;
        start.Environment[IdVariable] = id;
        if (id != DefaultId)
            start.Environment["SORUNE_PROFILE_DIR"] = DataRootOf(id);
        else if (BaseRoot != DefaultRootWithoutOverride())
            start.Environment["SORUNE_PROFILE_DIR"] = BaseRoot;

        Process.Start(start)?.Dispose();
        Log.Info($"Profil açılıyor: {id}");
    }

    private static string DefaultRootWithoutOverride()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sorune");

    private static ProfileInfo Fallback(string id) => id == DefaultId
        ? new ProfileInfo { Id = DefaultId, Name = L.T("Ana profil"), Emoji = "⭐", Color = Colors[0] }
        : new ProfileInfo { Id = id, Name = id, Emoji = "🌸", Color = Colors[1] };

    private static void Save(List<ProfileInfo> profiles)
    {
        try
        {
            JsonStore.Save(RegistryFile, profiles);
        }
        catch (IOException ex)
        {
            Log.Error("Profil listesi kaydedilemedi.", ex);
        }
    }

    private static string Clean(string? text, int max)
    {
        text = (text ?? "").Trim();
        return text.Length > max ? text[..max] : text;
    }

    private static bool IsValidId(string? id) => id is { Length: > 0 and <= 32 } && id.All(char.IsAsciiLetterOrDigit);

    private static bool IsColor(string? value) => value is { Length: 7 } && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit);
}
