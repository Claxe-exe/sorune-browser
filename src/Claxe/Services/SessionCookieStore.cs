using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

/// <summary>
/// WebView2 drops session cookies (the ones without an expiry, used by many logins) when
/// the browser closes. With session restore on, Claxe saves them at shutdown, encrypted
/// with DPAPI, and puts them back once before the first page of the next run loads.
/// </summary>
public static class SessionCookieStore
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);
    private static bool _restored;

    // Decrypted once and kept for this run, so a browser that crashes and restarts gets them again.
    private static Snapshot? _snapshot;

    /// <summary>Mirrors the RestoreSession setting.</summary>
    public static bool Enabled { get; set; } = true;

    private static string FilePath => Path.Combine(AppPaths.DataRoot, "session-cookies.dat");

    /// <summary>Called by every tab before its first navigation; only the first call does anything.</summary>
    public static void RestoreOnce(CoreWebView2 core)
    {
        if (_restored)
            return;

        _restored = true;
        Restore(core);
    }

    /// <summary>The browser process was replaced; the next tab re-applies the cookies.</summary>
    public static void OnBrowserRestarted() => _restored = false;

    public static async Task SaveAsync(CoreWebView2 core)
    {
        if (!Enabled)
            return;

        var cookies = await core.CookieManager.GetCookiesAsync(null);
        var snapshot = new Snapshot
        {
            SavedAt = DateTime.UtcNow,
            Cookies = cookies.Where(c => c.IsSession).Select(c => new SavedCookie
            {
                Name = c.Name,
                Value = c.Value,
                Domain = c.Domain,
                Path = c.Path,
                IsSecure = c.IsSecure,
                IsHttpOnly = c.IsHttpOnly,
                SameSite = c.SameSite.ToString(),
            }).ToList(),
        };

        try
        {
            JsonStore.SaveProtected(FilePath, snapshot);
            Log.Info($"Oturum çerezleri şifreli kaydedildi: {snapshot.Cookies.Count}");
        }
        catch (IOException ex)
        {
            Log.Error("Oturum çerezleri kaydedilemedi.", ex);
        }
    }

    /// <summary>Forgets saved session cookies (file and the copy kept for this run).</summary>
    public static void Delete()
    {
        _snapshot = null;
        try
        {
            File.Delete(FilePath);
        }
        catch (IOException ex)
        {
            Log.Error("Oturum çerezi dosyası silinemedi.", ex);
        }
    }

    private static void Restore(CoreWebView2 core)
    {
        if (!Enabled)
        {
            Delete();
            return;
        }

        if (_snapshot is null)
        {
            _snapshot = JsonStore.LoadProtected<Snapshot>(FilePath);
            // One-shot on disk: from here on the cookies live in the browser (and in memory).
            try
            {
                File.Delete(FilePath);
            }
            catch (IOException ex)
            {
                Log.Error("Oturum çerezi dosyası silinemedi.", ex);
            }
        }

        var snapshot = _snapshot;
        if (snapshot is null || DateTime.UtcNow - snapshot.SavedAt > MaxAge)
            return;

        var manager = core.CookieManager;
        var restored = 0;
        foreach (var saved in snapshot.Cookies)
        {
            try
            {
                var cookie = manager.CreateCookie(saved.Name, saved.Value, saved.Domain, saved.Path);
                cookie.IsSecure = saved.IsSecure;
                cookie.IsHttpOnly = saved.IsHttpOnly;
                if (Enum.TryParse<CoreWebView2CookieSameSiteKind>(saved.SameSite, out var sameSite))
                    cookie.SameSite = sameSite;
                manager.AddOrUpdateCookie(cookie);
                restored++;
            }
            catch (ArgumentException ex)
            {
                Log.Info($"Çerez geri yüklenemedi ({saved.Domain}): {ex.Message}");
            }
        }

        Log.Info($"Oturum çerezleri geri yüklendi: {restored}");
    }

    private sealed class Snapshot
    {
        public DateTime SavedAt { get; set; }
        public List<SavedCookie> Cookies { get; set; } = [];
    }

    private sealed class SavedCookie
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public string Domain { get; set; } = "";
        public string Path { get; set; } = "/";
        public bool IsSecure { get; set; }
        public bool IsHttpOnly { get; set; }
        public string SameSite { get; set; } = "";
    }
}
