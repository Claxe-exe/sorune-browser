using System.IO;
using System.Reflection;
using System.Text.Json;
using Claxe.Models;
using Claxe.Services;

namespace Claxe.ViewModels;

/// <summary>
/// Validates and stores changes coming from the settings page. Every value is checked
/// here, so nothing a page sends can put the settings file into an invalid state.
/// </summary>
public sealed class SettingsController(AppSettings settings, PermissionStore permissions, PanelStore panels)
{
    private const long MaxWallpaperBytes = 30 * 1024 * 1024;
    private static readonly string[] WallpaperExtensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp"];
    private static readonly string[] CornerStyles = ["sharp", "soft", "round"];
    private static readonly string[] Densities = ["compact", "normal", "comfortable"];

    /// <summary>Raised after a setting changed and was saved; the argument is the setting key.</summary>
    public event EventHandler<string>? Changed;

    public AppSettings Settings => settings;

    public bool TryUpdate(string? key, JsonElement value)
    {
        var applied = key switch
        {
            "theme" => SetTheme(StringOrNull(value)),
            "wallpaper" => Set(StringOrNull(value), ThemeService.WallpaperExists, v => settings.Wallpaper = v),
            "accentColor" => Set(StringOrNull(value), v => v is null || ThemeService.IsHexColor(v), v => settings.AccentColor = v?.ToUpperInvariant()),
            "chromeOpacity" => Set(NumberOrNull(value), v => v is null or (>= 0.4 and <= 0.97), v => settings.ChromeOpacity = v),
            "cornerStyle" => Set(StringOrNull(value), v => CornerStyles.Contains(v), v => settings.CornerStyle = v!),
            "density" => Set(StringOrNull(value), v => Densities.Contains(v), v => settings.Density = v!),
            "searchEngine" => Set(StringOrNull(value), v => SearchEngines.All.Any(e => e.Id == v), v => settings.SearchEngine = v!),
            "soundVolume" => Set(NumberOrNull(value), v => v is >= 0 and <= 1, v => settings.SoundVolume = v!.Value),
            "animationsEnabled" => SetBool(value, v => settings.AnimationsEnabled = v),
            "mascotEnabled" => SetBool(value, v => settings.MascotEnabled = v),
            "soundsEnabled" => SetBool(value, v => settings.SoundsEnabled = v),
            "restoreSession" => SetBool(value, v => settings.RestoreSession = v),
            "sidebarEnabled" => SetBool(value, v => settings.SidebarEnabled = v),
            "performanceMode" => SetBool(value, v => settings.PerformanceMode = v),
            _ => false,
        };

        if (!applied)
        {
            Log.Info($"Ayar reddedildi: {key} = {value.GetRawText()}");
            return false;
        }

        Commit(key!);
        return true;
    }

    /// <summary>Copies an image into the profile and makes it the wallpaper.</summary>
    public bool TryImportWallpaper(string sourcePath, out string? error)
    {
        error = null;
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (!WallpaperExtensions.Contains(extension))
        {
            error = "Desteklenen biçimler: JPG, PNG, WEBP, BMP.";
            return false;
        }

        var info = new FileInfo(sourcePath);
        if (!info.Exists || info.Length > MaxWallpaperBytes)
        {
            error = "Görsel bulunamadı ya da 30 MB'tan büyük.";
            return false;
        }

        try
        {
            var folder = ThemeService.CustomWallpapersFolder;
            Directory.CreateDirectory(folder);
            // A new name every time, so pages never show a cached older image.
            var name = $"custom-{DateTime.Now:yyyyMMdd-HHmmss}{extension}";
            File.Copy(sourcePath, Path.Combine(folder, name));
            DeleteCustomWallpapers(except: name);

            settings.Wallpaper = "custom:" + name;
            Commit("wallpaper");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Duvar kâğıdı kopyalanamadı: {sourcePath}", ex);
            error = "Görsel kopyalanamadı.";
            return false;
        }
    }

    /// <summary>Panel width is changed by dragging, not from the page; saved without a broadcast.</summary>
    public void SetPanelWidth(double width)
    {
        settings.PanelWidth = Math.Clamp(Math.Round(width), 280, 900);
        SettingsStore.Save(settings);
    }

    public void RemoveCustomWallpaper()
    {
        DeleteCustomWallpapers(except: null);
        if (settings.Wallpaper?.StartsWith("custom:", StringComparison.Ordinal) == true)
            settings.Wallpaper = null;
        Commit("wallpaper");
    }

    /// <summary>Everything the settings page needs to render.</summary>
    public object BuildState()
    {
        var custom = CustomWallpaperFiles().FirstOrDefault();
        BrowserEnvironment.IsRuntimeAvailable(out var webViewVersion);
        return new
        {
            type = "settings",
            settings,
            themes = ThemeService.ListThemes(),
            wallpapers = ThemeService.ListPresetWallpapers(),
            customWallpaper = custom is null
                ? null
                : new { id = "custom:" + custom, url = InternalPages.WallpapersOrigin + Uri.EscapeDataString(custom) },
            themeDefaults = new
            {
                accent = ThemeService.Current.Colors.Accent,
                accentSecondary = ThemeService.Current.Colors.AccentSecondary,
                chromeOpacity = ThemeService.Current.ChromeTintOpacity,
            },
            current = new { accent = ThemeService.Accent, wallpaperUrl = ThemeService.WallpaperUrl },
            searchEngines = SearchEngines.All.Select(e => new { e.Id, e.Name }),
            permissions = permissions.Items
                .OrderBy(p => p.Origin)
                .Select(p => new { p.Origin, kind = p.Kind.ToString(), what = PermissionStore.Describe(p.Kind), p.Allowed }),
            panels = panels.Items.Select(p => new { p.Id, p.Name, p.Url }),
            maxPanels = PanelStore.MaxPanels,
            about = new
            {
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3),
                webView2 = webViewVersion,
                profile = AppPaths.DataRoot,
            },
        };
    }

    private bool SetTheme(string? id)
    {
        if (id is null || !ThemeService.ThemeExists(id))
            return false;

        settings.Theme = id;
        // A theme brings its own look: drop per-theme overrides.
        settings.AccentColor = null;
        settings.Wallpaper = null;
        settings.ChromeOpacity = null;
        return true;
    }

    private static bool Set<T>(T value, Func<T, bool> isValid, Action<T> apply)
    {
        if (!isValid(value))
            return false;

        apply(value);
        return true;
    }

    private static bool SetBool(JsonElement value, Action<bool> apply)
    {
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;

        apply(value.GetBoolean());
        return true;
    }

    private static string? StringOrNull(JsonElement value)
        => value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? NumberOrNull(JsonElement value)
        => value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private void Commit(string key)
    {
        SettingsStore.Save(settings);
        Changed?.Invoke(this, key);
    }

    private static IEnumerable<string> CustomWallpaperFiles()
    {
        var folder = ThemeService.CustomWallpapersFolder;
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "custom-*").Select(Path.GetFileName).OfType<string>().OrderDescending()
            : [];
    }

    private static void DeleteCustomWallpapers(string? except)
    {
        foreach (var file in CustomWallpaperFiles().Where(f => f != except).ToList())
        {
            try
            {
                File.Delete(Path.Combine(ThemeService.CustomWallpapersFolder, file));
            }
            catch (IOException ex)
            {
                // Still open in a page; it is cleaned up on the next change.
                Log.Info($"Eski duvar kâğıdı silinemedi: {file} ({ex.Message})");
            }
        }
    }
}
