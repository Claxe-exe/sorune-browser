namespace Claxe.Models;

public sealed class AppSettings
{
    /// <summary>Id from <see cref="Services.SearchEngines"/>.</summary>
    public string SearchEngine { get; set; } = "google";

    /// <summary>Reopen the tabs from the previous run on startup.</summary>
    public bool RestoreSession { get; set; } = true;

    /// <summary>File name (without .json) in Assets/Themes.</summary>
    public string Theme { get; set; } = "midnight-mei";

    /// <summary>Interface motion: tab intro, mascot, new tab page effects.</summary>
    public bool AnimationsEnabled { get; set; } = true;

    /// <summary>The character peeking over the toolbar.</summary>
    public bool MascotEnabled { get; set; } = true;
}
