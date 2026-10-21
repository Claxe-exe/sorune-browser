namespace Claxe.Models;

public sealed class SessionState
{
    public List<SessionTab> Tabs { get; set; } = [];
    public int ActiveIndex { get; set; }
    public List<SessionGroup> Groups { get; set; } = [];
}

public sealed class SessionTab
{
    public string Url { get; set; } = "";
    public string? Title { get; set; }
    public bool IsPinned { get; set; }
    public string? GroupId { get; set; }
}

public sealed class SessionGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public bool IsCollapsed { get; set; }
}
