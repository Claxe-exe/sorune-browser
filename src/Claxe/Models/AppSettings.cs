namespace Claxe.Models;

public sealed class AppSettings
{
    /// <summary>Id from <see cref="Services.SearchEngines"/>.</summary>
    public string SearchEngine { get; set; } = "google";

    /// <summary>Reopen the tabs from the previous run on startup.</summary>
    public bool RestoreSession { get; set; } = true;
}
