namespace Claxe.Models;

public sealed class SessionState
{
    public List<SessionTab> Tabs { get; set; } = [];
    public int ActiveIndex { get; set; }
    public List<SessionGroup> Groups { get; set; } = [];

    /// <summary>Indexes into <see cref="Tabs"/> of the split screen pair; -1 when not split.</summary>
    public int SplitLeft { get; set; } = -1;
    public int SplitRight { get; set; } = -1;

    /// <summary>Share of the width taken by the left pane.</summary>
    public double SplitRatio { get; set; } = 0.5;

    /// <summary>The other windows (the first window's tabs are the ones above).</summary>
    public List<SessionState> Windows { get; set; } = [];

    /// <summary>Where the window was (screen DIPs: left, top, width, height); null for the first window.</summary>
    public double[]? Bounds { get; set; }

    public bool IsMaximized { get; set; }

    /// <summary>Every window's tabs in one list (for the time machine and crash recovery counts).</summary>
    public SessionState Merged()
    {
        var merged = new SessionState { Tabs = [.. Tabs], Groups = [.. Groups] };
        foreach (var window in Windows)
        {
            merged.Tabs.AddRange(window.Tabs);
            merged.Groups.AddRange(window.Groups);
        }
        return merged;
    }
}

public sealed class SessionTab
{
    public string Url { get; set; } = "";
    public string? Title { get; set; }
    public bool IsPinned { get; set; }
    public string? GroupId { get; set; }

    /// <summary>The tab's own volume, 0..100.</summary>
    public int Volume { get; set; } = 100;
}

public sealed class SessionGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public bool IsCollapsed { get; set; }
}
