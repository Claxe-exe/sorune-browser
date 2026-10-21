using System.IO;

namespace Claxe.Services;

public sealed class WebPanel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
}

/// <summary>Sidebar web panels (panels.json).</summary>
public sealed class PanelStore
{
    public const int MaxPanels = 12;
    private const int MaxNameLength = 30;
    private readonly List<WebPanel> _items;

    public PanelStore()
    {
        _items = JsonStore.Load<List<WebPanel>>(FilePath) ??
        [
            new() { Id = "whatsapp", Name = "WhatsApp", Url = "https://web.whatsapp.com/" },
            new() { Id = "discord", Name = "Discord", Url = "https://discord.com/app" },
            new() { Id = "telegram", Name = "Telegram", Url = "https://web.telegram.org/" },
            new() { Id = "spotify", Name = "Spotify", Url = "https://open.spotify.com/" },
            new() { Id = "twitch", Name = "Twitch", Url = "https://www.twitch.tv/" },
        ];
        _items.RemoveAll(p => !IsWebUrl(p.Url) || p.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0);
    }

    public IReadOnlyList<WebPanel> Items => _items;

    public static string IconFolder => Path.Combine(AppPaths.DataRoot, "panel-icons");

    public WebPanel? Add(string? name, string? address)
    {
        if (string.IsNullOrWhiteSpace(address) || _items.Count >= MaxPanels)
            return null;

        var url = AddressResolver.Resolve(address, SearchEngines.Default);
        if (!IsWebUrl(url) || url.StartsWith(SearchEngines.Default.BuildUrl(""), StringComparison.Ordinal))
            return null;

        name = string.IsNullOrWhiteSpace(name) ? new Uri(url).Host.Replace("www.", "") : name.Trim();
        var panel = new WebPanel { Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name, Url = url };
        _items.Add(panel);
        Save();
        return panel;
    }

    public bool Remove(string? id)
    {
        if (_items.RemoveAll(p => p.Id == id) == 0)
            return false;

        Save();
        try
        {
            File.Delete(Path.Combine(IconFolder, id + ".png"));
        }
        catch (IOException ex)
        {
            Log.Info($"Panel simgesi silinemedi: {ex.Message}");
        }
        return true;
    }

    private static bool IsWebUrl(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http");

    private void Save()
    {
        try
        {
            JsonStore.Save(FilePath, _items);
        }
        catch (IOException ex)
        {
            Log.Error("Paneller kaydedilemedi.", ex);
        }
    }

    private static string FilePath => Path.Combine(AppPaths.DataRoot, "panels.json");
}
