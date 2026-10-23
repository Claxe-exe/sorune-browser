namespace Claxe.Models;

public sealed class AppSettings
{
    /// <summary>"tr" or "en"; applied at the next start.</summary>
    public string Language { get; set; } = "tr";

    /// <summary>Id from <see cref="Services.SearchEngines"/>.</summary>
    public string SearchEngine { get; set; } = "google";

    /// <summary>SmartScreen warning pages for sites and downloads Microsoft reports as unsafe.</summary>
    public bool SmartScreenEnabled { get; set; } = true;

    /// <summary>Ask the search engine for suggestions while typing in the address bar (not in InPrivate tabs).</summary>
    public bool SearchSuggestions { get; set; } = true;

    /// <summary>Reopen the tabs from the previous run on startup.</summary>
    public bool RestoreSession { get; set; } = true;

    /// <summary>File name (without .json) in Assets/Themes.</summary>
    public string Theme { get; set; } = "midnight-mei";

    /// <summary>
    /// Null = the theme's own wallpaper; "preset:{file}" = Assets/Wallpapers;
    /// "custom:{file}" = an image the user added (stored in the profile).
    /// </summary>
    public string? Wallpaper { get; set; }

    /// <summary>#RRGGBB overriding the theme accent; null = theme accent.</summary>
    public string? AccentColor { get; set; }

    /// <summary>Opacity of the chrome tint over the wallpaper (lower = more glass); null = theme value.</summary>
    public double? ChromeOpacity { get; set; }

    /// <summary>"sharp", "soft" or "round".</summary>
    public string CornerStyle { get; set; } = "soft";

    /// <summary>"compact", "normal" or "comfortable".</summary>
    public string Density { get; set; } = "normal";

    /// <summary>Interface motion: tab intro, mascot, new tab page effects.</summary>
    public bool AnimationsEnabled { get; set; } = true;

    /// <summary>The character peeking over the toolbar.</summary>
    public bool MascotEnabled { get; set; } = true;

    public bool SoundsEnabled { get; set; } = true;

    /// <summary>0..1</summary>
    public double SoundVolume { get; set; } = 0.5;

    /// <summary>The web panel strip on the left.</summary>
    public bool SidebarEnabled { get; set; } = true;

    /// <summary>"left" or "right" side of the window.</summary>
    public string SidebarPosition { get; set; } = "left";

    /// <summary>Width of an open web panel in DIPs.</summary>
    public double PanelWidth { get; set; } = 420;

    /// <summary>Turns off decorative motion and puts background tabs to sleep sooner.</summary>
    public bool PerformanceMode { get; set; }

    /// <summary>The mascot's speech bubbles and reminders.</summary>
    public bool MascotTalks { get; set; } = true;

    /// <summary>File name of the user's own mascot image (in the profile's wallpapers folder); null = built-in.</summary>
    public string? MascotCustom { get; set; }

    /// <summary>How much of a custom mascot shows above the toolbar (0.2..0.9).</summary>
    public double MascotPeek { get; set; } = 0.5;

    /// <summary>Interface sound pack: "classic", "soft", "retro" or "crystal".</summary>
    public string SoundPack { get; set; } = "classic";

    /// <summary>New tab: "theme" (the theme's own), "petals", "sparks", "bubbles", "snow" or "none".</summary>
    public string NewTabEffect { get; set; } = "theme";

    /// <summary>New tab: darkening over the wallpaper, 0..0.9; null = the theme's.</summary>
    public double? NewTabDim { get; set; }

    /// <summary>New tab: wallpaper blur in pixels (0..30).</summary>
    public int NewTabBlur { get; set; }

    /// <summary>New tab: "left" or "center".</summary>
    public string NewTabAlign { get; set; } = "left";

    public bool NewTabShowSearch { get; set; } = true;
    public bool NewTabShowShortcuts { get; set; } = true;
    public bool NewTabShowWidgets { get; set; } = true;

    /// <summary>Tabs as a list on the left instead of the strip at the top (off unless chosen).</summary>
    public bool VerticalTabs { get; set; }

    public int VerticalTabsWidth { get; set; } = 240;

    /// <summary>What sites are told about the preferred colour scheme: "auto" (Windows), "dark" or "light".</summary>
    public string SiteColorScheme { get; set; } = "auto";

    /// <summary>Chromium's experimental auto-dark for sites without a dark theme (applied after a restart).</summary>
    public bool ForceDarkSites { get; set; }

    /// <summary>The toolbar fades into the colour at the top of the open site.</summary>
    public bool SiteTintEnabled { get; set; } = true;

    /// <summary>Pages sit inset as a framed card with the wallpaper around them.</summary>
    public bool PageFrameEnabled { get; set; }

    /// <summary>Background tabs untouched this long go to sleep; 0 = never.</summary>
    public int SleepAfterMinutes { get; set; } = 10;

    /// <summary>Closed web panels left alone this long are unloaded to free memory (0 = keep them, for notifications).</summary>
    public int PanelUnloadMinutes { get; set; }

    /// <summary>Pinned tabs may sleep too (off: pinned tabs always stay awake).</summary>
    public bool SleepPinnedTabs { get; set; }

    /// <summary>Where downloads are saved; null = Windows' Downloads folder.</summary>
    public string? DownloadFolder { get; set; }

    /// <summary>Version of the last run; a different one means "just updated" (shows what's new).</summary>
    public string? LastSeenVersion { get; set; }
}
