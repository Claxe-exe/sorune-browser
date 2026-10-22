using System.IO;
using Claxe.Models;

namespace Claxe.Services;

/// <summary>A look the user saved under a name: base theme plus every appearance override.</summary>
public sealed class UserTheme
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Theme { get; set; } = "";
    public string? Wallpaper { get; set; }
    public string? AccentColor { get; set; }
    public double? ChromeOpacity { get; set; }
    public string CornerStyle { get; set; } = "soft";
    public string Density { get; set; } = "normal";
    public string NewTabEffect { get; set; } = "theme";
    public double? NewTabDim { get; set; }
    public int NewTabBlur { get; set; }
    public string NewTabAlign { get; set; } = "left";
    public string SoundPack { get; set; } = "classic";
}

/// <summary>
/// Saved looks (user-themes.json). A custom wallpaper is copied to its own "theme-…" file, so
/// replacing the current custom wallpaper later never breaks a saved theme.
/// </summary>
public sealed class UserThemeStore
{
    public const int MaxThemes = 24;
    private const string CustomPrefix = "custom:";
    private readonly List<UserTheme> _items = JsonStore.Load<List<UserTheme>>(FilePath) ?? [];

    public IReadOnlyList<UserTheme> Items => _items;

    public static string FilePath => Path.Combine(AppPaths.DataRoot, "user-themes.json");

    public UserTheme? Save(string? name, AppSettings settings)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || _items.Count >= MaxThemes)
            return null;
        if (name.Length > 30)
            name = name[..30];

        var theme = new UserTheme
        {
            Name = name,
            Theme = settings.Theme,
            Wallpaper = settings.Wallpaper,
            AccentColor = settings.AccentColor,
            ChromeOpacity = settings.ChromeOpacity,
            CornerStyle = settings.CornerStyle,
            Density = settings.Density,
            NewTabEffect = settings.NewTabEffect,
            NewTabDim = settings.NewTabDim,
            NewTabBlur = settings.NewTabBlur,
            NewTabAlign = settings.NewTabAlign,
            SoundPack = settings.SoundPack,
        };

        if (theme.Wallpaper?.StartsWith(CustomPrefix, StringComparison.Ordinal) == true)
        {
            var source = Path.Combine(ThemeService.CustomWallpapersFolder, theme.Wallpaper[CustomPrefix.Length..]);
            if (File.Exists(source))
            {
                var copy = $"theme-{theme.Id}{Path.GetExtension(source)}";
                File.Copy(source, Path.Combine(ThemeService.CustomWallpapersFolder, copy), overwrite: true);
                theme.Wallpaper = CustomPrefix + copy;
            }
            else
            {
                theme.Wallpaper = null;
            }
        }

        _items.Add(theme);
        Persist();
        return theme;
    }

    public UserTheme? Find(string? id) => _items.FirstOrDefault(t => t.Id == id);

    public void Delete(string? id)
    {
        if (Find(id) is not { } theme)
            return;

        _items.Remove(theme);
        Persist();
        if (theme.Wallpaper is { } wallpaper && wallpaper.StartsWith(CustomPrefix + "theme-", StringComparison.Ordinal))
        {
            try
            {
                File.Delete(Path.Combine(ThemeService.CustomWallpapersFolder, wallpaper[CustomPrefix.Length..]));
            }
            catch (IOException ex)
            {
                Log.Info($"Tema duvar kâğıdı silinemedi: {ex.Message}");
            }
        }
    }

    private void Persist()
    {
        try
        {
            JsonStore.Save(FilePath, _items);
        }
        catch (IOException ex)
        {
            Log.Error("Kayıtlı temalar yazılamadı.", ex);
        }
    }
}
