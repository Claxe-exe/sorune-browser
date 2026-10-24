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
    /// <summary>Fonts bundled for the new tab (Pages/fonts, SIL Open Font License).</summary>
    private static readonly string[] NewTabFonts = ["system", "orbitron", "bebas", "pacifico", "pixel", "righteous", "script"];

    private const long MaxWallpaperBytes = 30 * 1024 * 1024;
    private static readonly string[] WallpaperExtensions = [".jpg", ".jpeg", ".png", ".webp", ".bmp"];
    private static readonly string[] CornerStyles = ["sharp", "soft", "round"];
    private static readonly string[] Densities = ["compact", "normal", "comfortable"];

    /// <summary>Raised after a setting changed and was saved; the argument is the setting key.</summary>
    public event EventHandler<string>? Changed;

    public AppSettings Settings => settings;

    /// <summary>The folder downloads actually go to (shown on the page).</summary>
    public Func<string?>? DownloadFolderInUse { get; set; }

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
            "searchSuggestions" => SetBool(value, v => settings.SearchSuggestions = v),
            "smartScreenEnabled" => SetBool(value, v => settings.SmartScreenEnabled = v),
            "secureDns" => SetBool(value, v => settings.SecureDns = v),
            "dnsProvider" => Set(StringOrNull(value), v => SecureDns.Providers.Any(p => p.Id == v), v =>
            {
                settings.DnsProvider = v!;
                SecureDns.Current = SecureDns.Find(v);
            }),
            "searchEngine" => Set(StringOrNull(value), v => SearchEngines.All.Any(e => e.Id == v), v => settings.SearchEngine = v!),
            "soundVolume" => Set(NumberOrNull(value), v => v is >= 0 and <= 1, v => settings.SoundVolume = v!.Value),
            "animationsEnabled" => SetBool(value, v => settings.AnimationsEnabled = v),
            "mascotEnabled" => SetBool(value, v => settings.MascotEnabled = v),
            "soundsEnabled" => SetBool(value, v => settings.SoundsEnabled = v),
            "restoreSession" => SetBool(value, v => settings.RestoreSession = v),
            "language" => Set(StringOrNull(value), v => v is "tr" or "en", v => settings.Language = v!),
            "sidebarEnabled" => SetBool(value, v => settings.SidebarEnabled = v),
            "sidebarPosition" => Set(StringOrNull(value), v => v is "left" or "right", v => settings.SidebarPosition = v!),
            "performanceMode" => SetBool(value, v => settings.PerformanceMode = v),
            "panelUnloadMinutes" => Set(NumberOrNull(value), v => v is 0 or 15 or 30 or 60 or 120, v => settings.PanelUnloadMinutes = (int)v!.Value),
            "sleepAfterMinutes" => Set(NumberOrNull(value), v => v is 0 or 5 or 10 or 15 or 30 or 60, v => settings.SleepAfterMinutes = (int)v!.Value),
            "sleepPinnedTabs" => SetBool(value, v => settings.SleepPinnedTabs = v),
            "siteTintEnabled" => SetBool(value, v => settings.SiteTintEnabled = v),
            "siteColorScheme" => Set(StringOrNull(value), v => v is "auto" or "dark" or "light", v => settings.SiteColorScheme = v!),
            "forceDarkSites" => SetBool(value, v => settings.ForceDarkSites = v),
            "verticalTabs" => SetBool(value, v => settings.VerticalTabs = v),
            "verticalTabsWidth" => Set(NumberOrNull(value), v => v is >= 160 and <= 400, v => settings.VerticalTabsWidth = (int)v!.Value),
            "mascotTalks" => SetBool(value, v => settings.MascotTalks = v),
            "mascotPeek" => Set(NumberOrNull(value), v => v is >= 0.2 and <= 0.9, v => settings.MascotPeek = v!.Value),
            "soundPack" => Set(StringOrNull(value), v => SoundService.Packs.Contains(v), v => settings.SoundPack = v!),
            "newTabEffect" => Set(StringOrNull(value), v => v is "theme" or "petals" or "sparks" or "bubbles" or "snow" or "none", v => settings.NewTabEffect = v!),
            "newTabDim" => Set(NumberOrNull(value), v => v is null or (>= 0 and <= 0.9), v => settings.NewTabDim = v),
            "newTabBlur" => Set(NumberOrNull(value), v => v is >= 0 and <= 30, v => settings.NewTabBlur = (int)v!.Value),
            "newTabAlign" => Set(StringOrNull(value), v => v is "left" or "center", v => settings.NewTabAlign = v!),
            "newTabShowSearch" => SetBool(value, v => settings.NewTabShowSearch = v),
            "newTabShowShortcuts" => SetBool(value, v => settings.NewTabShowShortcuts = v),
            "newTabShowWidgets" => SetBool(value, v => settings.NewTabShowWidgets = v),
            "newTabClockFont" => Set(StringOrNull(value), v => NewTabFonts.Contains(v), v => settings.NewTabClockFont = v!),
            "newTabLabelFont" => Set(StringOrNull(value), v => NewTabFonts.Contains(v), v => settings.NewTabLabelFont = v!),
            "newTabCards" => Set(NumberOrNull(value), v => v is null or (>= 0 and <= 0.9), v => settings.NewTabCards = v),
            "newTabShowGreeting" => SetBool(value, v => settings.NewTabShowGreeting = v),
            "pageFrameEnabled" => SetBool(value, v => settings.PageFrameEnabled = v),
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
            error = L.T("Desteklenen biçimler: JPG, PNG, WEBP, BMP.");
            return false;
        }

        var info = new FileInfo(sourcePath);
        if (!info.Exists || info.Length > MaxWallpaperBytes)
        {
            error = L.T("Görsel bulunamadı ya da 30 MB'tan büyük.");
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
            error = L.T("Görsel kopyalanamadı.");
            return false;
        }
    }

    public UserThemeStore UserThemes { get; } = new();

    public void SetAutoScreenTranslate(bool value)
    {
        settings.AutoScreenTranslate = value;
        Commit("autoScreenTranslate");
    }

    public void SetAutoScreenTranslateSeconds(int seconds)
    {
        settings.AutoScreenTranslateSeconds = Math.Clamp(seconds, 3, 20);
        Commit("autoScreenTranslateSeconds");
    }

    public void SetVerticalTabs(bool value)
    {
        settings.VerticalTabs = value;
        Commit("verticalTabs");
    }

    /// <summary>Puts a saved look back: base theme, wallpaper, colours, layout, new tab and sounds.</summary>
    public bool ApplyUserTheme(string? id)
    {
        if (UserThemes.Find(id) is not { } theme)
            return false;

        if (ThemeService.ThemeExists(theme.Theme))
            settings.Theme = theme.Theme;
        settings.Wallpaper = ThemeService.WallpaperExists(theme.Wallpaper) ? theme.Wallpaper : null;
        settings.AccentColor = ThemeService.IsHexColor(theme.AccentColor) ? theme.AccentColor : null;
        settings.ChromeOpacity = theme.ChromeOpacity is >= 0.4 and <= 0.97 ? theme.ChromeOpacity : null;
        settings.CornerStyle = CornerStyles.Contains(theme.CornerStyle) ? theme.CornerStyle : settings.CornerStyle;
        settings.Density = Densities.Contains(theme.Density) ? theme.Density : settings.Density;
        settings.NewTabEffect = theme.NewTabEffect is "theme" or "petals" or "sparks" or "bubbles" or "snow" or "none" ? theme.NewTabEffect : "theme";
        settings.NewTabDim = theme.NewTabDim is null or (>= 0 and <= 0.9) ? theme.NewTabDim : null;
        settings.NewTabBlur = Math.Clamp(theme.NewTabBlur, 0, 30);
        settings.NewTabAlign = theme.NewTabAlign is "center" ? "center" : "left";
        settings.SoundPack = SoundService.Packs.Contains(theme.SoundPack) ? theme.SoundPack : settings.SoundPack;
        Commit("userTheme");
        return true;
    }

    public bool SaveUserTheme(string? name)
    {
        try
        {
            var saved = UserThemes.Save(name, settings) is not null;
            if (saved)
                Changed?.Invoke(this, "userThemes");
            return saved;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Tema kaydedilemedi.", ex);
            return false;
        }
    }

    public void DeleteUserTheme(string? id)
    {
        UserThemes.Delete(id);
        Changed?.Invoke(this, "userThemes");
    }

    /// <summary>Copies the user's mascot image into the profile (PNG with transparency works best).</summary>
    public bool TryImportMascot(string sourcePath, out string? error)
    {
        error = null;
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var info = new FileInfo(sourcePath);
        if (extension is not (".png" or ".webp" or ".gif" or ".jpg" or ".jpeg") || !info.Exists || info.Length > 10 * 1024 * 1024)
        {
            error = L.T("PNG, WEBP, GIF ya da JPG seç (en fazla 10 MB). Arka planı şeffaf PNG en güzel durur.");
            return false;
        }

        try
        {
            var folder = ThemeService.CustomWallpapersFolder;
            Directory.CreateDirectory(folder);
            var name = $"mascot-{DateTime.Now:yyyyMMdd-HHmmss}{extension}";
            File.Copy(sourcePath, Path.Combine(folder, name));
            DeleteMascots(except: name);
            settings.MascotCustom = name;
            Commit("mascotCustom");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Maskot görseli kopyalanamadı: {sourcePath}", ex);
            error = L.T("Görsel kopyalanamadı.");
            return false;
        }
    }

    public void RemoveCustomMascot()
    {
        settings.MascotCustom = null;
        DeleteMascots(except: null);
        Commit("mascotCustom");
    }

    /// <summary>Full path of the custom mascot image, or null.</summary>
    public string? CustomMascotPath
        => settings.MascotCustom is { } name && File.Exists(Path.Combine(ThemeService.CustomWallpapersFolder, name))
            ? Path.Combine(ThemeService.CustomWallpapersFolder, name)
            : null;

    private static void DeleteMascots(string? except)
    {
        var folder = ThemeService.CustomWallpapersFolder;
        if (!Directory.Exists(folder))
            return;
        foreach (var file in Directory.EnumerateFiles(folder, "mascot-*").Where(f => Path.GetFileName(f) != except))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException ex)
            {
                Log.Info($"Eski maskot silinemedi: {ex.Message}");
            }
        }
    }

    /// <summary>Picked with the folder dialog (null = back to Windows' Downloads folder).</summary>
    public void SetDownloadFolder(string? folder)
    {
        settings.DownloadFolder = folder is not null && Directory.Exists(folder) ? folder : null;
        Commit("downloadFolder");
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
            activeLanguage = L.Language,
            forceDarkActive = BrowserEnvironment.ForceDark,
            secureDnsActive = BrowserEnvironment.SecureDnsPort is not null,
            dnsProviders = SecureDns.Providers.Select(p => new { p.Id, p.Name, p.Note }),
            downloadFolder = DownloadFolderInUse?.Invoke(),
            mascotUrl = CustomMascotPath is null ? null : InternalPages.WallpapersOrigin + Uri.EscapeDataString(settings.MascotCustom!),
            wallpaperPalette = ColorExtractor.Palette(ThemeService.WallpaperPath),
            userThemes = UserThemes.Items.Select(t => new
            {
                t.Id,
                t.Name,
                accent = t.AccentColor ?? ThemeService.AccentOf(t.Theme),
                picture = ThemeService.WallpaperUrlFor(t.Wallpaper, t.Theme),
                active = t.Theme == settings.Theme && t.Wallpaper == settings.Wallpaper && t.AccentColor == settings.AccentColor
                         && t.ChromeOpacity == settings.ChromeOpacity && t.NewTabEffect == settings.NewTabEffect,
            }),
            maxUserThemes = UserThemeStore.MaxThemes,
            pendingRestore = BackupService.HasPendingRestore,
            soundPacks = SoundService.Packs,
            defaultBrowser = new { registeredExe = DefaultBrowser.RegisteredExe, currentExe = Environment.ProcessPath },
            update = new
            {
                state = UpdateService.State.ToString(),
                current = UpdateService.CurrentVersion,
                available = UpdateService.AvailableVersion,
            },
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
