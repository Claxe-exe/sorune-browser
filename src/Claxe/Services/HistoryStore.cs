using System.Globalization;
using System.IO;
using System.Windows.Threading;

namespace Claxe.Services;

public sealed class HistoryEntry
{
    public long Id { get; set; }
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime VisitedAt { get; set; }
}

/// <summary>
/// Browsing history, encrypted with DPAPI (history.dat). Kept in memory, saved a moment
/// after changes, capped by count and age.
/// </summary>
public sealed class HistoryStore
{
    private const int MaxEntries = 10_000;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(180);
    private static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(30);
    private static readonly CompareInfo Compare = new CultureInfo("tr-TR").CompareInfo;

    private readonly List<HistoryEntry> _entries; // oldest first
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private long _nextId;

    public HistoryStore()
    {
        _entries = JsonStore.LoadProtected<List<HistoryEntry>>(FilePath) ?? [];
        _entries.RemoveAll(e => DateTime.UtcNow - e.VisitedAt > MaxAge);
        _nextId = _entries.Count == 0 ? 1 : _entries.Max(e => e.Id) + 1;
        _saveTimer.Tick += (_, _) => Flush();
    }

    private static string FilePath => Path.Combine(AppPaths.DataRoot, "history.dat");

    public void AddVisit(string url, string title)
    {
        var now = DateTime.UtcNow;
        // Redirects, reloads and in-page navigations shouldn't flood the list.
        if (_entries.Count > 0 && _entries[^1] is { } last && last.Url == url && now - last.VisitedAt < MergeWindow)
        {
            last.Title = title;
            last.VisitedAt = now;
        }
        else
        {
            _entries.Add(new HistoryEntry { Id = _nextId++, Url = url, Title = title, VisitedAt = now });
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }

        ScheduleSave();
    }

    /// <summary>Titles often arrive after the navigation finished; update the latest visit of that URL.</summary>
    public void UpdateTitle(string url, string title)
    {
        for (var i = _entries.Count - 1; i >= 0 && i >= _entries.Count - 20; i--)
        {
            if (_entries[i].Url != url)
                continue;

            if (_entries[i].Title != title)
            {
                _entries[i].Title = title;
                ScheduleSave();
            }
            return;
        }
    }

    /// <summary>Newest first. <paramref name="beforeId"/> pages through older results.</summary>
    public IReadOnlyList<HistoryEntry> Query(string? text, long? beforeId, int limit)
    {
        IEnumerable<HistoryEntry> items = Enumerable.Reverse(_entries);
        if (beforeId is { } before)
            items = items.Where(e => e.Id < before);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var needle = text.Trim();
            items = items.Where(e =>
                Compare.IndexOf(e.Title, needle, CompareOptions.IgnoreCase) >= 0 ||
                Compare.IndexOf(e.Url, needle, CompareOptions.IgnoreCase) >= 0);
        }

        return items.Take(Math.Clamp(limit, 1, 500)).ToList();
    }

    public void Delete(IEnumerable<long> ids)
    {
        var set = ids.ToHashSet();
        if (_entries.RemoveAll(e => set.Contains(e.Id)) > 0)
            ScheduleSave();
    }

    /// <summary>Removes visits since <paramref name="since"/> (UTC); null clears everything.</summary>
    public void Clear(DateTime? since)
    {
        _entries.RemoveAll(e => since is null || e.VisitedAt >= since);
        Flush();
    }

    public void Flush()
    {
        _saveTimer.Stop();
        try
        {
            JsonStore.SaveProtected(FilePath, _entries);
        }
        catch (IOException ex)
        {
            Log.Error("Geçmiş kaydedilemedi.", ex);
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
