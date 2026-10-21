using System.IO;

namespace Claxe.Services;

public sealed class Bookmark
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Bookmarks in bookmarks.json (plain JSON, so they're easy to back up or move).</summary>
public sealed class BookmarkStore
{
    private const int MaxTitleLength = 200;
    private readonly List<Bookmark> _items;

    public BookmarkStore()
    {
        _items = JsonStore.Load<List<Bookmark>>(FilePath) ?? [];
        _items.RemoveAll(b => !AddressResolver.IsNavigable(b.Url));
    }

    public event EventHandler? Changed;

    private static string FilePath => Path.Combine(AppPaths.DataRoot, "bookmarks.json");

    /// <summary>Newest first.</summary>
    public IReadOnlyList<Bookmark> Items => Enumerable.Reverse(_items).ToList();

    public bool Contains(string? url) => url is not null && _items.Any(b => b.Url == url);

    /// <summary>Adds the page, or removes it if it is already bookmarked. Returns true when added.</summary>
    public bool Toggle(string url, string title)
    {
        if (_items.RemoveAll(b => b.Url == url) > 0)
        {
            Save();
            return false;
        }

        if (!AddressResolver.IsNavigable(url))
            return false;

        _items.Add(new Bookmark { Url = url, Title = Trim(title, url) });
        Save();
        return true;
    }

    public void Remove(string? id)
    {
        if (_items.RemoveAll(b => b.Id == id) > 0)
            Save();
    }

    public void Rename(string? id, string? title)
    {
        if (_items.FirstOrDefault(b => b.Id == id) is not { } bookmark)
            return;

        bookmark.Title = Trim(title, bookmark.Url);
        Save();
    }

    private static string Trim(string? title, string fallback)
    {
        title = string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();
        return title.Length > MaxTitleLength ? title[..MaxTitleLength] : title;
    }

    private void Save()
    {
        try
        {
            JsonStore.Save(FilePath, _items);
        }
        catch (IOException ex)
        {
            Log.Error("Yer imleri kaydedilemedi.", ex);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
