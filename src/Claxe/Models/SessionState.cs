namespace Claxe.Models;

public sealed class SessionState
{
    public List<SessionTab> Tabs { get; set; } = [];
    public int ActiveIndex { get; set; }
}

public sealed class SessionTab
{
    public string Url { get; set; } = "";
    public string? Title { get; set; }
}
