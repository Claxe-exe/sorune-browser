using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Claxe.Services;

/// <summary>
/// "Back up and move": packs the profile's settings, looks and lists into one .sorune file
/// (a zip) that another computer can restore. Files encrypted for this Windows account are
/// decrypted into the backup and encrypted again for the new account on restore. Cookies,
/// passwords and the browser cache are never included.
///
/// A restore is only staged here; it is applied on the next start, before anything is
/// loaded, so the running app can't write its old data over it while it closes.
/// </summary>
public static class BackupService
{
    public const string Extension = ".sorune";
    private const int FormatVersion = 1;
    private const long MaxEntryBytes = 64L * 1024 * 1024;
    private const long MaxTotalBytes = 512L * 1024 * 1024;

    private static readonly string[] PlainFiles =
        ["settings.json", "bookmarks.json", "shortcuts.json", "panels.json", "permissions.json", "user-themes.json"];

    private static readonly string[] ProtectedFiles = ["widgets.dat"];

    /// <summary>Only included on request: what the user browsed.</summary>
    private static readonly string[] HistoryPlainFiles = ["session.json"];

    private static readonly string[] HistoryProtectedFiles = ["history.dat", "closed-tabs.dat", "sessions-archive.dat"];

    private static readonly string[] Folders = ["wallpapers", "panel-icons"];

    private static string PendingFolder => Path.Combine(AppPaths.DataRoot, "pending-restore");

    public static bool HasPendingRestore => File.Exists(Path.Combine(PendingFolder, "manifest.json"));

    public sealed record Summary(int Files, long Bytes);

    public static Summary Create(string target, bool includeHistory, IEnumerable<string> extensionNames)
    {
        var temp = target + ".tmp";
        var files = 0;
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            var manifest = new JsonObject
            {
                ["app"] = "Sorune",
                ["format"] = FormatVersion,
                ["version"] = typeof(BackupService).Assembly.GetName().Version?.ToString(3),
                ["createdAt"] = DateTime.UtcNow.ToString("O"),
                ["includesHistory"] = includeHistory,
                ["extensions"] = new JsonArray(extensionNames.Select(n => (JsonNode?)n).ToArray()),
            };
            Write(zip, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest));

            foreach (var name in includeHistory ? PlainFiles.Concat(HistoryPlainFiles) : PlainFiles)
            {
                var path = Path.Combine(AppPaths.DataRoot, name);
                if (!File.Exists(path))
                    continue;
                zip.CreateEntryFromFile(path, name, CompressionLevel.Optimal);
                files++;
            }

            foreach (var name in includeHistory ? ProtectedFiles.Concat(HistoryProtectedFiles) : ProtectedFiles)
            {
                var path = Path.Combine(AppPaths.DataRoot, name);
                if (!File.Exists(path))
                    continue;
                try
                {
                    Write(zip, "secure/" + Path.ChangeExtension(name, ".json"), Interop.Dpapi.Unprotect(File.ReadAllBytes(path)));
                    files++;
                }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    Log.Error($"Yedeğe alınamadı (çözülemedi): {name}", ex);
                }
            }

            foreach (var folder in Folders)
            {
                var path = Path.Combine(AppPaths.DataRoot, folder);
                if (!Directory.Exists(path))
                    continue;
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    if (new FileInfo(file).Length > MaxEntryBytes)
                        continue;
                    zip.CreateEntryFromFile(file, folder + "/" + Path.GetFileName(file), CompressionLevel.NoCompression);
                    files++;
                }
            }
        }

        File.Move(temp, target, overwrite: true);
        return new Summary(files, new FileInfo(target).Length);
    }

    /// <summary>
    /// Checks a backup and unpacks it to the pending folder. Returns the manifest (for showing
    /// what will be restored), or null with an error message.
    /// </summary>
    public static JsonObject? StageRestore(string source, out string? error)
    {
        error = null;
        CancelRestore();
        var staging = PendingFolder + ".tmp";
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);

            using var zip = ZipFile.OpenRead(source);
            if (zip.GetEntry("manifest.json") is not { } manifestEntry
                || JsonNode.Parse(ReadEntry(manifestEntry)) is not JsonObject manifest
                || (string?)manifest["app"] != "Sorune"
                || manifest["format"]?.GetValue<int>() is not (> 0 and <= FormatVersion))
            {
                error = L.T("Bu bir Sorune yedeği değil ya da daha yeni bir sürümle alınmış.");
                return null;
            }

            long total = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith('/'))
                    continue;
                if (!IsAllowed(entry.FullName))
                {
                    Log.Info($"Yedekte tanınmayan dosya atlandı: {entry.FullName}");
                    continue;
                }

                total += entry.Length;
                if (entry.Length > MaxEntryBytes || total > MaxTotalBytes)
                {
                    error = L.T("Yedek dosyası beklenenden büyük, açılmadı.");
                    return null;
                }

                var bytes = ReadEntry(entry);
                if (entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    JsonDocument.Parse(bytes).Dispose(); // refuse broken JSON now, not at the next start

                var path = Path.Combine(staging, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }

            Directory.Move(staging, PendingFolder);
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            Log.Error($"Yedek açılamadı: {source}", ex);
            error = L.T("Yedek dosyası okunamadı ya da bozuk.");
            TryDelete(staging);
            return null;
        }
    }

    public static void CancelRestore() => TryDelete(PendingFolder);

    /// <summary>Called at start-up, before any store is loaded.</summary>
    public static void ApplyPendingRestore()
    {
        if (!HasPendingRestore)
            return;

        try
        {
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(PendingFolder, "manifest.json"))) as JsonObject;

            // Files missing from the backup (history when it wasn't included) keep this computer's copy.
            foreach (var name in PlainFiles.Concat(HistoryPlainFiles))
            {
                var staged = Path.Combine(PendingFolder, name);
                if (File.Exists(staged))
                    File.Copy(staged, Path.Combine(AppPaths.DataRoot, name), overwrite: true);
            }

            foreach (var name in ProtectedFiles.Concat(HistoryProtectedFiles))
            {
                var staged = Path.Combine(PendingFolder, "secure", Path.ChangeExtension(name, ".json"));
                if (File.Exists(staged))
                    File.WriteAllBytes(Path.Combine(AppPaths.DataRoot, name), Interop.Dpapi.Protect(File.ReadAllBytes(staged)));
            }


            foreach (var folder in Folders)
            {
                var staged = Path.Combine(PendingFolder, folder);
                if (!Directory.Exists(staged))
                    continue;
                var target = Path.Combine(AppPaths.DataRoot, folder);
                Directory.CreateDirectory(target);
                foreach (var file in Directory.EnumerateFiles(staged))
                    File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }

            Log.Info($"Yedek geri yüklendi ({manifest?["version"]}, {manifest?["createdAt"]}).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception)
        {
            Log.Error("Yedek geri yüklenemedi.", ex);
        }
        finally
        {
            CancelRestore();
        }
    }

    /// <summary>Known names only, and file names without any folder part: nothing can land outside the profile.</summary>
    private static bool IsAllowed(string entry)
    {
        if (entry == "manifest.json" || PlainFiles.Contains(entry) || HistoryPlainFiles.Contains(entry))
            return true;

        var slash = entry.IndexOf('/');
        if (slash <= 0 || entry.IndexOf('/', slash + 1) >= 0)
            return false;

        var (folder, file) = (entry[..slash], entry[(slash + 1)..]);
        if (file.Length == 0 || file != Path.GetFileName(file) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || file.StartsWith('.'))
            return false;

        return folder == "secure"
            ? ProtectedFiles.Concat(HistoryProtectedFiles).Any(name => Path.ChangeExtension(name, ".json") == file)
            : Folders.Contains(folder);
    }


    private static void Write(ZipArchive zip, string name, byte[] bytes)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(bytes);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var memory = new MemoryStream();
        // Read at most one byte past the limit, so a lying header can't make us unpack gigabytes.
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            memory.Write(buffer, 0, read);
            if (memory.Length > MaxEntryBytes)
                throw new InvalidDataException("Yedek girdisi çok büyük.");
        }
        return memory.ToArray();
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"Klasör silinemedi: {folder} ({ex.Message})");
        }
    }
}
