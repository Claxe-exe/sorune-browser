namespace Claxe.Models;

public sealed class AppSettings
{
    /// <summary>"tr" or "en"; applied at the next start.</summary>
    public string Language { get; set; } = "tr";

    /// <summary>Id from <see cref="Services.SearchEngines"/>.</summary>
    public string SearchEngine { get; set; } = "google";

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

    /// <summary>Version of the last run; a different one means "just updated" (shows what's new).</summary>
    public string? LastSeenVersion { get; set; }
}
