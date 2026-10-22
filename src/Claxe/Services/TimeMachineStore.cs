using System.IO;
using Claxe.Models;

namespace Claxe.Services;

/// <summary>A closed tab as Ctrl+Shift+T brings it back: where it was, pinned or in which group.</summary>
public sealed class ClosedTab
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
    public int Index { get; set; }
    public bool IsPinned { get; set; }
    public string? GroupId { get; set; }
    public string? GroupName { get; set; }
    public string? GroupColor { get; set; }
}

public sealed class ArchivedTab
{
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
}

/// <summary>The tabs of an earlier run, kept so they can be brought back from the time machine.</summary>
public sealed class ArchivedSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    public List<ArchivedTab> Tabs { get; set; } = [];
}

/// <summary>
/// The "tab time machine": recently closed tabs (newest first, 100 / 30 days) and the tab lists of
/// earlier runs (20). Both survive restarts and are DPAPI-encrypted like the history; InPrivate tabs
/// never get here.
/// </summary>
public sealed class TimeMachineStore
{
    public const int MaxClosed = 100;
    public const int MaxSessions = 20;
    private static readonly TimeSpan KeepClosed = TimeSpan.FromDays(30);

    private readonly List<ClosedTab> _closed;
    private readonly List<ArchivedSession> _sessions;

    public TimeMachineStore()
    {
        _closed = JsonStore.LoadProtected<List<ClosedTab>>(ClosedFile) ?? [];
        _closed.RemoveAll(c => DateTime.UtcNow - c.ClosedAt > KeepClosed || string.IsNullOrEmpty(c.Url));
        _sessions = JsonStore.LoadProtected<List<ArchivedSession>>(SessionsFile) ?? [];
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ClosedTab> Closed => _closed;
    public IReadOnlyList<ArchivedSession> Sessions => _sessions;

    public void AddClosed(ClosedTab tab)
    {
        _closed.Insert(0, tab);
        if (_closed.Count > MaxClosed)
            _closed.RemoveRange(MaxClosed, _closed.Count - MaxClosed);
        SaveClosed();
    }

    /// <summary>The most recently closed tab, taken off the list (Ctrl+Shift+T).</summary>
    public ClosedTab? TakeLatest()
    {
        if (_closed.Count == 0)
            return null;

        var latest = _closed[0];
        _closed.RemoveAt(0);
        SaveClosed();
        return latest;
    }

    public ClosedTab? Take(string? id)
    {
        if (_closed.FirstOrDefault(c => c.Id == id) is not { } tab)
            return null;

        _closed.Remove(tab);
        SaveClosed();
        return tab;
    }

    /// <summary>Keeps the previous run's tabs (skipped when they are the same as the last kept list).</summary>
    public void ArchiveSession(SessionState session, DateTime savedAt)
    {
        var tabs = session.Tabs
            .Where(t => AddressResolver.IsNavigable(t.Url) && !InternalPages.IsNewTab(t.Url))
            .Select(t => new ArchivedTab { Url = t.Url, Title = t.Title ?? "" })
            .ToList();
        if (tabs.Count == 0)
            return;
        if (_sessions.FirstOrDefault() is { } last && last.Tabs.Select(t => t.Url).SequenceEqual(tabs.Select(t => t.Url)))
            return;

        _sessions.Insert(0, new ArchivedSession { SavedAt = savedAt.ToUniversalTime(), Tabs = tabs });
        if (_sessions.Count > MaxSessions)
            _sessions.RemoveRange(MaxSessions, _sessions.Count - MaxSessions);
        SaveSessions();
    }

    public ArchivedSession? FindSession(string? id) => _sessions.FirstOrDefault(s => s.Id == id);

    public void RemoveSession(string? id)
    {
        if (_sessions.RemoveAll(s => s.Id == id) > 0)
            SaveSessions();
    }

    /// <summary>Clearing browsing history also empties the time machine.</summary>
    public void Clear(DateTime? since = null)
    {
        _closed.RemoveAll(c => since is null || c.ClosedAt >= since);
        _sessions.RemoveAll(s => since is null || s.SavedAt >= since);
        SaveClosed();
        SaveSessions();
    }

    private void SaveClosed() => Save(ClosedFile, _closed);

    private void SaveSessions() => Save(SessionsFile, _sessions);

    private void Save<T>(string file, T value)
    {
        try
        {
            JsonStore.SaveProtected(file, value);
        }
        catch (IOException ex)
        {
            Log.Error("Zaman makinesi kaydedilemedi.", ex);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string ClosedFile => Path.Combine(AppPaths.DataRoot, "closed-tabs.dat");
    private static string SessionsFile => Path.Combine(AppPaths.DataRoot, "sessions-archive.dat");
}
