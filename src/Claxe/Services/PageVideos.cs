using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Claxe.Services;

public sealed record PageVideo(string Url, string Name, string? Size);

/// <summary>
/// "Video on this page" (mini tools): finds plain video files a page links to or plays
/// (<video src>, <source>, links to .mp4/.webm/…) and downloads them like any other file.
/// Nothing is decrypted or reassembled: streamed video (blob:, HLS/DASH) is not a file and is
/// skipped, and YouTube is excluded on purpose (its terms forbid downloading).
/// </summary>
public static class PageVideos
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly string[] BlockedHosts = ["youtube.com", "youtu.be", "googlevideo.com", "youtube-nocookie.com", "ytimg.com"];

    private static readonly string[] Extensions = [".mp4", ".webm", ".mov", ".m4v", ".ogv", ".mkv"];

    /// <summary>Runs in the page: every video file address it can see, with a readable name.</summary>
    public const string FindScript = """
        (() => {
            const found = new Map();
            const add = (raw, label) => {
                try {
                    const url = new URL(raw, location.href);
                    if (!/^https?:$/.test(url.protocol) || found.has(url.href)) return;
                    found.set(url.href, (label || "").trim().slice(0, 120));
                } catch { }
            };
            for (const video of document.querySelectorAll("video")) {
                const label = video.getAttribute("title") || video.getAttribute("aria-label") || document.title;
                if (video.currentSrc) add(video.currentSrc, label);
                if (video.getAttribute("src")) add(video.getAttribute("src"), label);
                for (const source of video.querySelectorAll("source[src]")) add(source.getAttribute("src"), label);
            }
            for (const link of document.querySelectorAll("a[href]")) {
                if (/\.(mp4|webm|mov|m4v|ogv|mkv)(\?|#|$)/i.test(link.getAttribute("href"))) add(link.getAttribute("href"), link.textContent || document.title);
            }
            return [...found].slice(0, 30).map(([url, label]) => ({ url, label }));
        })()
        """;

    public static bool IsBlocked(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && BlockedHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
                                       || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses the script's result, drops anything blocked or not a plain file.</summary>
    public static List<PageVideo> Parse(string json)
    {
        var list = new List<PageVideo>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var url = item.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (url is null || IsBlocked(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || uri.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                || uri.AbsolutePath.EndsWith(".mpd", StringComparison.OrdinalIgnoreCase))
                continue;
            var label = item.TryGetProperty("label", out var l) ? l.GetString() : null;
            list.Add(new PageVideo(url, FileNameFor(uri, label), null));
        }
        return list;
    }

    /// <summary>Downloads into the folder; returns the saved file's path.</summary>
    public static async Task<string> DownloadAsync(PageVideo video, string? referer, string folder, IProgress<double?> progress, CancellationToken cancel)
    {
        if (IsBlocked(video.Url))
            throw new InvalidOperationException("YouTube videoları indirilmez.");

        using var request = new HttpRequestMessage(HttpMethod.Get, video.Url);
        request.Headers.UserAgent.TryParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0 Safari/537.36 Sorune");
        if (Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            request.Headers.Referrer = refererUri;

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel);
        response.EnsureSuccessStatusCode();
        var type = response.Content.Headers.ContentType?.MediaType ?? "";
        if (type.StartsWith("text/", StringComparison.OrdinalIgnoreCase) || type.Contains("mpegurl", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Bu adres bir video dosyası değil.");

        Directory.CreateDirectory(folder);
        var path = UniquePath(Path.Combine(folder, video.Name));
        var partial = path + ".part";
        var total = response.Content.Headers.ContentLength;
        try
        {
            await using (var input = await response.Content.ReadAsStreamAsync(cancel))
            await using (var output = File.Create(partial))
            {
                var buffer = new byte[128 * 1024];
                long done = 0;
                var lastReport = DateTime.MinValue;
                int read;
                while ((read = await input.ReadAsync(buffer, cancel)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancel);
                    done += read;
                    if (DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(250))
                    {
                        lastReport = DateTime.UtcNow;
                        progress.Report(total > 0 ? (double)done / total.Value : null);
                    }
                }
            }
            File.Move(partial, path);
            progress.Report(1);
            return path;
        }
        catch
        {
            try { File.Delete(partial); } catch (IOException) { }
            throw;
        }
    }

    private static string FileNameFor(Uri uri, string? label)
    {
        var fromUrl = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
        var extension = Path.GetExtension(fromUrl).ToLowerInvariant();
        if (!Extensions.Contains(extension))
            extension = ".mp4";
        var stem = Path.GetFileNameWithoutExtension(fromUrl);
        // Hashes and ids make poor names; the page title is friendlier when the URL has no words.
        if (string.IsNullOrWhiteSpace(stem) || stem.Length > 60 || !stem.Any(char.IsLetter)
            || System.Text.RegularExpressions.Regex.IsMatch(stem, "^[0-9a-fA-F_-]{8,}$"))
            stem = string.IsNullOrWhiteSpace(label) ? "video" : label;
        var safe = new string(stem.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray()).Trim();
        if (safe.Length > 80)
            safe = safe[..80].Trim();
        return (safe.Length == 0 ? "video" : safe) + extension;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path))
            return path;
        var folder = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }
}
