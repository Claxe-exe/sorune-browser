using System.IO;

namespace Claxe.Services;

public sealed class Bookmark
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Folder name; empty = not in a folder.</summary>
    public string Folder { get; set; } = "";

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Bookmarks in bookmarks.json (plain JSON, so they're easy to back up or move).</summary>
public sealed class BookmarkStore
{
    private const int MaxTitleLength = 200;
    private const int MaxFolderLength = 40;
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

    /// <summary>Moves a bookmark into a folder (created on the fly); empty or null takes it out.</summary>
    public void SetFolder(string? id, string? folder)
    {
        if (_items.FirstOrDefault(b => b.Id == id) is not { } bookmark)
            return;

        folder = folder?.Trim() ?? "";
        bookmark.Folder = folder.Length > MaxFolderLength ? folder[..MaxFolderLength] : folder;
        Save();
    }

    /// <summary>Renames a folder for every bookmark in it; an empty name dissolves the folder.</summary>
    public void RenameFolder(string? from, string? to)
    {
        if (string.IsNullOrEmpty(from))
            return;

        to = to?.Trim() ?? "";
        if (to.Length > MaxFolderLength)
            to = to[..MaxFolderLength];

        var changed = false;
        foreach (var bookmark in _items.Where(b => b.Folder == from))
        {
            bookmark.Folder = to;
            changed = true;
        }

        if (changed)
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
