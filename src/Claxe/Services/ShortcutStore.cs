using System.IO;
using Claxe.Models;

namespace Claxe.Services;

/// <summary>New tab page shortcuts, kept in %LOCALAPPDATA%\Claxe\shortcuts.json.</summary>
public sealed class ShortcutStore
{
    public const int MaxShortcuts = 12;
    private const int MaxTitleLength = 40;

    private static string FilePath => Path.Combine(AppPaths.DataRoot, "shortcuts.json");

    private readonly List<Shortcut> _items;

    public ShortcutStore()
    {
        _items = JsonStore.Load<List<Shortcut>>(FilePath) ??
        [
            new() { Title = "YouTube", Url = "https://www.youtube.com/" },
            new() { Title = "Twitch", Url = "https://www.twitch.tv/" },
            new() { Title = "GitHub", Url = "https://github.com/" },
            new() { Title = "Vikipedi", Url = "https://tr.wikipedia.org/" },
        ];

        // Anything edited by hand that isn't a plain web address is dropped.
        _items.RemoveAll(s => !IsWebUrl(s.Url));
    }

    public IReadOnlyList<Shortcut> Items => _items;

    /// <summary>Returns false (and changes nothing) for invalid input or a full list.</summary>
    public bool Add(string? title, string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || _items.Count >= MaxShortcuts)
            return false;

        var url = AddressResolver.Resolve(address, SearchEngines.Default);
        if (!IsWebUrl(url) || url.StartsWith(SearchEngines.Default.BuildUrl(""), StringComparison.Ordinal))
            return false;

        title = string.IsNullOrWhiteSpace(title) ? new Uri(url).Host.Replace("www.", "") : title.Trim();
        if (title.Length > MaxTitleLength)
            title = title[..MaxTitleLength];

        _items.RemoveAll(s => s.Url == url);
        _items.Add(new Shortcut { Title = title, Url = url });
        Save();
        return true;
    }

    public void Remove(string? url)
    {
        if (_items.RemoveAll(s => s.Url == url) > 0)
            Save();
    }

    private void Save()
    {
        try
        {
            JsonStore.Save(FilePath, _items);
        }
        catch (IOException ex)
        {
            Log.Error("Kısayollar kaydedilemedi.", ex);
        }
    }

    private static bool IsWebUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http");
}
