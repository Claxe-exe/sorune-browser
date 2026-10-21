using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

public sealed class SitePermission
{
    public string Origin { get; set; } = "";
    public CoreWebView2PermissionKind Kind { get; set; }
    public bool Allowed { get; set; }
}

/// <summary>Remembered per-site permission decisions (permissions.json).</summary>
public sealed class PermissionStore
{
    private readonly List<SitePermission> _items;

    public PermissionStore() => _items = JsonStore.Load<List<SitePermission>>(FilePath) ?? [];

    private static string FilePath => Path.Combine(AppPaths.DataRoot, "permissions.json");

    public IReadOnlyList<SitePermission> Items => _items;

    public bool? Find(string origin, CoreWebView2PermissionKind kind)
        => _items.FirstOrDefault(p => p.Origin == origin && p.Kind == kind)?.Allowed;

    public void Remember(string origin, CoreWebView2PermissionKind kind, bool allowed)
    {
        _items.RemoveAll(p => p.Origin == origin && p.Kind == kind);
        _items.Add(new SitePermission { Origin = origin, Kind = kind, Allowed = allowed });
        Save();
    }

    public void Forget(string? origin, CoreWebView2PermissionKind? kind)
    {
        if (_items.RemoveAll(p => p.Origin == origin && (kind is null || p.Kind == kind)) > 0)
            Save();
    }

    public void Clear()
    {
        _items.Clear();
        Save();
    }

    /// <summary>"https://site.com" from a page URL (permissions are per origin, like other browsers).</summary>
    public static string OriginOf(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : url;

    public static string Describe(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Camera => "Kamera",
        CoreWebView2PermissionKind.Microphone => "Mikrofon",
        CoreWebView2PermissionKind.Geolocation => "Konum",
        CoreWebView2PermissionKind.Notifications => "Bildirimler",
        CoreWebView2PermissionKind.ClipboardRead => "Panoyu okuma",
        CoreWebView2PermissionKind.OtherSensors => "Hareket ve ışık sensörleri",
        CoreWebView2PermissionKind.MultipleAutomaticDownloads => "Birden çok dosyayı otomatik indirme",
        CoreWebView2PermissionKind.FileReadWrite => "Dosyaları okuma ve yazma",
        CoreWebView2PermissionKind.Autoplay => "Otomatik oynatma",
        CoreWebView2PermissionKind.LocalFonts => "Yerel yazı tipleri",
        CoreWebView2PermissionKind.MidiSystemExclusiveMessages => "MIDI cihazları",
        CoreWebView2PermissionKind.WindowManagement => "Pencere yönetimi",
        _ => "Diğer bir izin",
    };

    /// <summary>Segoe Fluent Icons glyph for the prompt.</summary>
    public static string Glyph(CoreWebView2PermissionKind kind) => kind switch
    {
        CoreWebView2PermissionKind.Camera => "",
        CoreWebView2PermissionKind.Microphone => "",
        CoreWebView2PermissionKind.Geolocation => "",
        CoreWebView2PermissionKind.Notifications => "",
        CoreWebView2PermissionKind.ClipboardRead => "",
        CoreWebView2PermissionKind.MultipleAutomaticDownloads => "",
        _ => "",
    };

    private void Save()
    {
        try
        {
            JsonStore.Save(FilePath, _items);
        }
        catch (IOException ex)
        {
            Log.Error("Site izinleri kaydedilemedi.", ex);
        }
    }
}
