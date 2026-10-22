using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

/// <summary>An installed browser extension as the settings page and sidebar see it.</summary>
public sealed record ExtensionInfo(
    string Id, string Name, bool IsEnabled, string? Version,
    string? PopupPage, string? OptionsPage, string? IconFile);

/// <summary>
/// Chromium extensions through WebView2 (unpacked only: WebView2 has no web store). Files live
/// in the profile's "extensions" folder; extensions.json maps WebView2's extension id to its
/// folder so the popup, options page and icon can be found in the manifest. The two
/// recommended ones are downloaded from their projects' official GitHub releases.
/// </summary>
public static class ExtensionService
{
    public const string UBlockLite = "ublock";
    public const string Bitwarden = "bitwarden";

    private static readonly HttpClient Http = CreateClient();
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private sealed class Entry
    {
        public string Id { get; set; } = "";
        public string Folder { get; set; } = "";
    }

    public static string Root => Path.Combine(AppPaths.DataRoot, "extensions");
    private static string MapFile => Path.Combine(AppPaths.DataRoot, "extensions.json");

    public static async Task<List<ExtensionInfo>> ListAsync(CoreWebView2Profile profile)
    {
        var map = LoadMap();
        var result = new List<ExtensionInfo>();
        foreach (var extension in await profile.GetBrowserExtensionsAsync())
        {
            var folder = map.FirstOrDefault(e => e.Id == extension.Id)?.Folder;
            var manifest = folder is null ? null : Manifest.Read(folder);
            result.Add(new ExtensionInfo(extension.Id, extension.Name, extension.IsEnabled, manifest?.Version,
                manifest?.Popup, manifest?.Options, manifest?.Icon is { } icon ? Path.Combine(folder!, icon) : null));
        }
        return result;
    }

    /// <summary>Copies an unpacked extension folder into the profile and loads it.</summary>
    public static Task<CoreWebView2BrowserExtension> InstallFolderAsync(CoreWebView2Profile profile, string source)
    {
        var root = FindManifestFolder(source) ?? throw new InvalidOperationException(L.T("Bu klasörde manifest.json yok; açılmış bir eklenti klasörü seç."));
        var target = NewTargetFolder(Path.GetFileName(root));
        CopyDirectory(root, target);
        return LoadAsync(profile, target);
    }

    /// <summary>A .zip of an unpacked extension, or a .crx package.</summary>
    public static async Task<CoreWebView2BrowserExtension> InstallArchiveAsync(CoreWebView2Profile profile, string file)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "Sorune-ext-" + Guid.NewGuid().ToString("N"));
        try
        {
            await Task.Run(() => Extract(file, scratch));
            return await InstallFolderAsync(profile, scratch);
        }
        finally
        {
            TryDelete(scratch);
        }
    }

    /// <summary>Downloads the latest official release of a recommended extension and installs it.</summary>
    public static async Task<CoreWebView2BrowserExtension> InstallRecommendedAsync(CoreWebView2Profile profile, string which)
    {
        var url = which switch
        {
            UBlockLite => await FindAssetAsync("https://api.github.com/repos/uBlockOrigin/uBOL-home/releases/latest",
                name => name.EndsWith(".chromium.zip", StringComparison.OrdinalIgnoreCase)),
            Bitwarden => await FindBitwardenAsync(),
            _ => null,
        } ?? throw new InvalidOperationException(L.T("Eklentinin son sürümü bulunamadı."));

        var file = Path.Combine(Path.GetTempPath(), $"Sorune-{which}-{Guid.NewGuid():N}.zip");
        try
        {
            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                await using var output = File.Create(file);
                await response.Content.CopyToAsync(output);
            }
            Log.Info($"Eklenti indirildi: {url}");
            return await InstallArchiveAsync(profile, file);
        }
        finally
        {
            TryDelete(file);
        }
    }

    public static async Task RemoveAsync(CoreWebView2Profile profile, string id)
    {
        var extension = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == id);
        if (extension is not null)
            await extension.RemoveAsync();

        var map = LoadMap();
        if (map.FirstOrDefault(e => e.Id == id) is { } entry)
        {
            map.Remove(entry);
            SaveMap(map);
            TryDelete(entry.Folder);
        }
    }

    public static async Task SetEnabledAsync(CoreWebView2Profile profile, string id, bool enabled)
    {
        var extension = (await profile.GetBrowserExtensionsAsync()).FirstOrDefault(e => e.Id == id);
        if (extension is not null && extension.IsEnabled != enabled)
            await extension.EnableAsync(enabled);
    }

    private static async Task<CoreWebView2BrowserExtension> LoadAsync(CoreWebView2Profile profile, string folder)
    {
        try
        {
            var extension = await profile.AddBrowserExtensionAsync(folder);
            var map = LoadMap();
            // Reinstalling the same extension replaces its old copy.
            foreach (var old in map.Where(e => e.Id == extension.Id && !string.Equals(e.Folder, folder, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                map.Remove(old);
                TryDelete(old.Folder);
            }
            if (!map.Any(e => e.Id == extension.Id))
                map.Add(new Entry { Id = extension.Id, Folder = folder });
            SaveMap(map);
            Log.Info($"Eklenti yüklendi: {extension.Name} ({extension.Id})");
            return extension;
        }
        catch
        {
            TryDelete(folder);
            throw;
        }
    }

    private static async Task<string?> FindAssetAsync(string api, Func<string, bool> match)
    {
        using var document = await GetJsonAsync(api);
        return AssetUrl(document.RootElement, match);
    }

    private static async Task<string?> FindBitwardenAsync()
    {
        using var document = await GetJsonAsync("https://api.github.com/repos/bitwarden/clients/releases?per_page=40");
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.GetProperty("tag_name").GetString()?.StartsWith("browser-v", StringComparison.Ordinal) != true
                || release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
                continue;

            // WebView2 is Edge's engine: the Edge build first, Chrome's otherwise.
            return AssetUrl(release, n => n.StartsWith("dist-edge-", StringComparison.Ordinal) && n.EndsWith(".zip", StringComparison.Ordinal))
                   ?? AssetUrl(release, n => n.StartsWith("dist-chrome-", StringComparison.Ordinal) && n.EndsWith(".zip", StringComparison.Ordinal));
        }
        return null;
    }

    private static string? AssetUrl(JsonElement release, Func<string, bool> match)
        => release.GetProperty("assets").EnumerateArray()
            .Where(a => match(a.GetProperty("name").GetString() ?? ""))
            .Select(a => a.GetProperty("browser_download_url").GetString())
            .FirstOrDefault(u => u?.StartsWith("https://github.com/", StringComparison.Ordinal) == true);

    private static async Task<JsonDocument> GetJsonAsync(string url)
    {
        using var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    /// <summary>Unzips a .zip, or the zip inside a .crx (CRX2/CRX3 header skipped). .NET refuses entries escaping the folder.</summary>
    internal static void Extract(string file, string target)
    {
        Directory.CreateDirectory(target);
        var bytes = File.ReadAllBytes(file);
        var offset = 0;
        if (bytes.Length > 16 && bytes[0] == 'C' && bytes[1] == 'r' && bytes[2] == '2' && bytes[3] == '4')
        {
            var version = BitConverter.ToUInt32(bytes, 4);
            offset = version == 3
                ? 12 + (int)BitConverter.ToUInt32(bytes, 8)
                : 16 + (int)BitConverter.ToUInt32(bytes, 8) + (int)BitConverter.ToUInt32(bytes, 12);
        }

        using var stream = new MemoryStream(bytes, offset, bytes.Length - offset);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        zip.ExtractToDirectory(target);
    }

    private static string? FindManifestFolder(string folder)
    {
        if (File.Exists(Path.Combine(folder, "manifest.json")))
            return folder;
        return Directory.EnumerateDirectories(folder)
            .SelectMany(d => new[] { d }.Concat(Directory.EnumerateDirectories(d)))
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "manifest.json")));
    }

    private static string NewTargetFolder(string name)
    {
        var safe = string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'));
        if (safe.Length is 0 or > 40)
            safe = "extension";
        return Path.Combine(Root, $"{safe}-{Guid.NewGuid().ToString("N")[..8]}");
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
    }

    private static List<Entry> LoadMap() => JsonStore.Load<List<Entry>>(MapFile) ?? [];

    private static void SaveMap(List<Entry> map)
    {
        try
        {
            JsonStore.Save(MapFile, map);
        }
        catch (IOException ex)
        {
            Log.Error("Eklenti listesi kaydedilemedi.", ex);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
            else if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Silinemedi: {path} ({ex.Message})");
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        client.DefaultRequestHeaders.UserAgent.TryParseAdd("Sorune/" + UpdateService.CurrentVersion);
        return client;
    }

    /// <summary>The few manifest fields Sorune needs: version, popup, options page, best icon.</summary>
    private sealed record Manifest(string? Version, string? Popup, string? Options, string? Icon)
    {
        public static Manifest? Read(string folder)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var root = document.RootElement;
                string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

                string? popup = null;
                foreach (var key in new[] { "action", "browser_action", "page_action" })
                {
                    if (root.TryGetProperty(key, out var action) && action.ValueKind == JsonValueKind.Object && Str(action, "default_popup") is { } p)
                    {
                        popup = p;
                        break;
                    }
                }

                var options = root.TryGetProperty("options_ui", out var ui) && ui.ValueKind == JsonValueKind.Object ? Str(ui, "page") : null;
                options ??= Str(root, "options_page");

                string? icon = null;
                if (root.TryGetProperty("icons", out var icons) && icons.ValueKind == JsonValueKind.Object)
                {
                    icon = icons.EnumerateObject()
                        .Where(p => p.Value.ValueKind == JsonValueKind.String && int.TryParse(p.Name, out _))
                        .OrderByDescending(p => int.Parse(p.Name))
                        .Select(p => p.Value.GetString())
                        .FirstOrDefault(i => i is not null && !i.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                                             && File.Exists(Path.Combine(folder, i.TrimStart('/'))));
                    icon = icon?.TrimStart('/');
                }

                return new Manifest(Str(root, "version"), popup?.TrimStart('/'), options?.TrimStart('/'), icon);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                Log.Info($"Eklenti manifest'i okunamadı ({folder}): {ex.Message}");
                return null;
            }
        }
    }
}
