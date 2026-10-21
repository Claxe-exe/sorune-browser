using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Claxe.Services;

/// <summary>A browser profile found on this PC. <see cref="Id"/> is what pages send back.</summary>
public sealed record ImportSource(string Id, string Browser, string Profile, bool HasBookmarks, bool HasHistory)
{
    internal string Folder { get; init; } = "";
    internal bool IsFirefox { get; init; }
}

public sealed record ImportedBookmark(string Url, string Title, string Folder, DateTime AddedAt);

public sealed record ImportedVisit(string Url, string Title, DateTime VisitedAt);

/// <summary>
/// Reads bookmarks and history from other browsers' profile folders, the same way other
/// browsers' import features do. Cookies and passwords are deliberately not imported:
/// Chromium encrypts them so only the owning browser can read them, and Claxe doesn't
/// try to get around that.
/// </summary>
public static class BrowserImporter
{
    private static string Local(string path) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), path);
    private static string Roaming(string path) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), path);

    /// <summary>Chromium-based browsers, most used first. Opera keeps a single profile in its root folder.</summary>
    private static readonly (string Key, string Name, string Root, bool SingleProfile)[] Chromium =
    [
        ("chrome", "Google Chrome", Local(@"Google\Chrome\User Data"), false),
        ("operagx", "Opera GX", Roaming(@"Opera Software\Opera GX Stable"), true),
        ("edge", "Microsoft Edge", Local(@"Microsoft\Edge\User Data"), false),
        ("brave", "Brave", Local(@"BraveSoftware\Brave-Browser\User Data"), false),
        ("opera", "Opera", Roaming(@"Opera Software\Opera Stable"), true),
        ("vivaldi", "Vivaldi", Local(@"Vivaldi\User Data"), false),
    ];

    public static IReadOnlyList<ImportSource> Detect()
    {
        var sources = new List<ImportSource>();
        foreach (var (key, name, root, single) in Chromium)
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                sources.AddRange(single ? DetectSingleProfile(key, name, root) : DetectProfiles(key, name, root));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Info($"{name} profilleri okunamadı: {ex.Message}");
            }
        }

        var firefox = Roaming(@"Mozilla\Firefox\Profiles");
        if (Directory.Exists(firefox))
        {
            foreach (var folder in Directory.EnumerateDirectories(firefox).Where(f => File.Exists(Path.Combine(f, "places.sqlite"))))
            {
                var dir = Path.GetFileName(folder);
                var label = dir.Contains('.') ? dir[(dir.IndexOf('.') + 1)..] : dir;
                sources.Add(new ImportSource("firefox|" + dir, "Firefox", label, true, true) { Folder = folder, IsFirefox = true });
            }
        }

        return sources;
    }

    /// <summary>Looks the source up again by id, so a page can never point the importer at an arbitrary path.</summary>
    public static ImportSource? Find(string? id) => Detect().FirstOrDefault(s => s.Id == id);

    public static IReadOnlyList<ImportedBookmark> ReadBookmarks(ImportSource source)
        => source.IsFirefox ? ReadFirefoxBookmarks(source) : ReadChromiumBookmarks(source);

    public static IReadOnlyList<ImportedVisit> ReadHistory(ImportSource source, int limit)
    {
        var query = source.IsFirefox
            ? "SELECT url, title, last_visit_date FROM moz_places WHERE last_visit_date IS NOT NULL AND hidden = 0 ORDER BY last_visit_date DESC LIMIT $limit"
            : "SELECT url, title, last_visit_time FROM urls WHERE hidden = 0 AND last_visit_time > 0 ORDER BY last_visit_time DESC LIMIT $limit";

        return WithDatabaseCopy(source, source.IsFirefox ? "places.sqlite" : "History", connection =>
        {
            using var command = connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();

            var visits = new List<ImportedVisit>();
            while (reader.Read())
            {
                var url = reader.GetString(0);
                if (!IsWebUrl(url))
                    continue;

                var title = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var time = reader.GetInt64(2);
                var visitedAt = source.IsFirefox
                    ? DateTime.UnixEpoch.AddTicks(time * 10)  // microseconds since 1970
                    : FromWebKitTime(time);                   // microseconds since 1601
                visits.Add(new ImportedVisit(url, title, visitedAt));
            }
            return visits;
        });
    }

    // ---------------------------------------------------------------- detection

    private static IEnumerable<ImportSource> DetectProfiles(string key, string name, string root)
    {
        // "Local State" holds the display names the user gave each profile.
        var names = new Dictionary<string, string>();
        var localState = Path.Combine(root, "Local State");
        if (File.Exists(localState))
        {
            using var doc = JsonDocument.Parse(ReadShared(localState));
            if (doc.RootElement.TryGetProperty("profile", out var profile) && profile.TryGetProperty("info_cache", out var cache))
            {
                foreach (var entry in cache.EnumerateObject())
                {
                    if (entry.Value.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } display)
                        names[entry.Name] = display;
                }
            }
        }

        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var dir = Path.GetFileName(folder);
            if (dir != "Default" && !dir.StartsWith("Profile ", StringComparison.Ordinal))
                continue;

            var source = Describe(key, name, folder, dir, names.GetValueOrDefault(dir, dir == "Default" ? L.T("Varsayılan") : dir));
            if (source is not null)
                yield return source;
        }
    }

    private static IEnumerable<ImportSource> DetectSingleProfile(string key, string name, string root)
    {
        // Older Opera versions keep the files in the root, newer ones in "Default".
        foreach (var (folder, dir) in new[] { (root, "root"), (Path.Combine(root, "Default"), "Default") })
        {
            if (Directory.Exists(folder) && Describe(key, name, folder, dir, L.T("Varsayılan")) is { } source)
            {
                yield return source;
                yield break;
            }
        }
    }

    private static ImportSource? Describe(string key, string name, string folder, string dir, string profile)
    {
        var bookmarks = File.Exists(Path.Combine(folder, "Bookmarks"));
        var history = File.Exists(Path.Combine(folder, "History"));
        return bookmarks || history
            ? new ImportSource($"{key}|{dir}", name, profile, bookmarks, history) { Folder = folder }
            : null;
    }

    // ---------------------------------------------------------------- bookmarks

    private static IReadOnlyList<ImportedBookmark> ReadChromiumBookmarks(ImportSource source)
    {
        var path = Path.Combine(source.Folder, "Bookmarks");
        if (!File.Exists(path))
            return [];

        using var doc = JsonDocument.Parse(ReadShared(path));
        var result = new List<ImportedBookmark>();
        if (!doc.RootElement.TryGetProperty("roots", out var roots))
            return result;

        // Bookmarks bar goes straight into "<Browser>", the other roots get a sub-folder.
        foreach (var (rootKey, label) in new[] { ("bookmark_bar", ""), ("other", L.T("Diğer")), ("synced", L.T("Mobil")) })
        {
            if (roots.TryGetProperty(rootKey, out var node))
                WalkChromium(node, label.Length > 0 ? [label] : [], result, ShortName(source.Browser));
        }
        return result;
    }

    private static void WalkChromium(JsonElement folder, List<string> path, List<ImportedBookmark> result, string browser)
    {
        if (!folder.TryGetProperty("children", out var children))
            return;

        foreach (var child in children.EnumerateArray())
        {
            var type = child.TryGetProperty("type", out var t) ? t.GetString() : null;
            var name = child.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (type == "url" && child.TryGetProperty("url", out var u) && u.GetString() is { } url && IsWebUrl(url))
            {
                var added = child.TryGetProperty("date_added", out var d) && long.TryParse(d.GetString(), out var micro)
                    ? FromWebKitTime(micro)
                    : DateTime.UtcNow;
                result.Add(new ImportedBookmark(url, name, FolderName(browser, path), added));
            }
            else if (type == "folder")
            {
                WalkChromium(child, [.. path, name], result, browser);
            }
        }
    }

    private static IReadOnlyList<ImportedBookmark> ReadFirefoxBookmarks(ImportSource source)
        => WithDatabaseCopy(source, "places.sqlite", connection =>
        {
            // Folder tree first (type 2), then bookmarks (type 1); tags (under root 4) are skipped.
            var folders = new Dictionary<long, (string Title, long Parent)>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT id, IFNULL(title, ''), IFNULL(parent, 0) FROM moz_bookmarks WHERE type = 2";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                    folders[reader.GetInt64(0)] = (reader.GetString(1), reader.GetInt64(2));
            }

            var result = new List<ImportedBookmark>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = """
                    SELECT p.url, IFNULL(b.title, ''), b.parent, IFNULL(b.dateAdded, 0)
                    FROM moz_bookmarks b JOIN moz_places p ON p.id = b.fk WHERE b.type = 1
                    """;
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var url = reader.GetString(0);
                    if (!IsWebUrl(url))
                        continue;

                    var path = new List<string>();
                    var parent = reader.GetInt64(2);
                    var isTag = false;
                    // Walk up to the built-in roots (ids 1-6); their own names aren't useful.
                    for (var guard = 0; parent > 6 && folders.TryGetValue(parent, out var f) && guard < 32; guard++)
                    {
                        path.Insert(0, f.Title);
                        parent = f.Parent;
                    }
                    if (parent == 4)
                        isTag = true;
                    if (isTag)
                        continue;

                    var added = DateTime.UnixEpoch.AddTicks(reader.GetInt64(3) * 10);
                    result.Add(new ImportedBookmark(url, reader.GetString(1), FolderName("Firefox", path), added));
                }
            }
            return result;
        });

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// The browser may be running and holding its database open, so work on a private copy
    /// (with the WAL file, which can hold the newest rows) and delete it afterwards.
    /// </summary>
    private static List<T> WithDatabaseCopy<T>(ImportSource source, string file, Func<SqliteConnection, List<T>> read)
    {
        var original = Path.Combine(source.Folder, file);
        if (!File.Exists(original))
            return [];

        var temp = Path.Combine(AppPaths.DataRoot, "import-tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var copy = Path.Combine(temp, file);
            CopyShared(original, copy);
            if (File.Exists(original + "-wal"))
                CopyShared(original + "-wal", copy + "-wal");

            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = copy,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ToString());
            connection.Open();
            return read(connection);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException ex)
            {
                Log.Info($"Geçici içe aktarma klasörü silinemedi: {ex.Message}");
            }
        }
    }

    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void CopyShared(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = File.Create(destination);
        input.CopyTo(output);
    }

    private static string FolderName(string browser, List<string> path)
        => path.Count == 0 ? browser : browser + " / " + string.Join(" / ", path.Where(p => p.Length > 0));

    private static string ShortName(string browser) => browser.Replace("Google ", "").Replace("Microsoft ", "");

    private static DateTime FromWebKitTime(long microseconds)
        => microseconds > 0 ? DateTime.FromFileTimeUtc(microseconds * 10) : DateTime.UtcNow;

    private static bool IsWebUrl(string url)
        => url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
