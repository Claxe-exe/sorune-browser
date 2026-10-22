using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Claxe.Services;
using Microsoft.Web.WebView2.Core;

namespace Claxe.ViewModels;

/// <summary>Backs the Extensions section of the settings page.</summary>
public sealed class ExtensionController(
    Func<string?> pickFolder,
    Func<string?> pickArchive,
    Action<string> openInNewTab,
    Func<CoreWebView2Profile, Task> refreshSidebar)
{
    private bool _busy;

    public async void Handle(TabViewModel page, string type, JsonElement message)
    {
        if (page.Core?.Profile is not { } profile)
            return;

        string? Text(string name)
            => message.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        switch (type)
        {
            case "ext:list":
                await PostListAsync(page, profile);
                break;

            case "ext:install" when Text("which") is { } which:
                await RunAsync(page, profile, () => ExtensionService.InstallRecommendedAsync(profile, which));
                break;

            case "ext:installFolder":
                if (pickFolder() is { } folder)
                    await RunAsync(page, profile, () => ExtensionService.InstallFolderAsync(profile, folder));
                break;

            case "ext:installFile":
                if (pickArchive() is { } file)
                    await RunAsync(page, profile, () => ExtensionService.InstallArchiveAsync(profile, file));
                break;

            case "ext:toggle" when Text("id") is { } id:
                await RunAsync(page, profile, async () =>
                {
                    await ExtensionService.SetEnabledAsync(profile, id, message.TryGetProperty("enabled", out var e) && e.ValueKind == JsonValueKind.True);
                    return null;
                });
                break;

            case "ext:remove" when Text("id") is { } id:
                await RunAsync(page, profile, async () =>
                {
                    await ExtensionService.RemoveAsync(profile, id);
                    return null;
                });
                break;

            case "ext:options" when Text("id") is { } id:
                var info = (await ExtensionService.ListAsync(profile)).FirstOrDefault(e => e.Id == id);
                if (info?.OptionsPage is { } options)
                    openInNewTab($"chrome-extension://{id}/{options}");
                break;
        }
    }

    private async Task RunAsync(TabViewModel page, CoreWebView2Profile profile, Func<Task<CoreWebView2BrowserExtension?>> work)
    {
        if (_busy)
            return;

        _busy = true;
        page.PostToInternalPage(new { type = "extensionsBusy", busy = true });
        string? error = null, installed = null;
        try
        {
            installed = (await work())?.Name;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or HttpRequestException or TaskCanceledException
                                       or COMException or ArgumentException or UnauthorizedAccessException
                                       or InvalidDataException or JsonException)
        {
            Log.Error("Eklenti işlemi başarısız.", ex);
            error = ex is HttpRequestException or TaskCanceledException
                ? L.T("İndirilemedi. İnternet bağlantını kontrol et.")
                : ex is COMException or ArgumentException or InvalidDataException
                    ? L.T("WebView2 bu eklentiyi yükleyemedi. Bozuk ya da desteklenmeyen bir paket olabilir.")
                    : ex.Message;
        }
        finally
        {
            _busy = false;
        }

        await refreshSidebar(profile);
        await PostListAsync(page, profile, error, installed);
    }

    private async Task PostListAsync(TabViewModel page, CoreWebView2Profile profile, string? error = null, string? installed = null)
    {
        List<ExtensionInfo> items;
        try
        {
            items = await ExtensionService.ListAsync(profile);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            Log.Error("Eklentiler listelenemedi.", ex);
            items = [];
            error ??= L.T("Eklentiler okunamadı.");
        }

        page.PostToInternalPage(new
        {
            type = "extensions",
            busy = _busy,
            error,
            installed,
            items = items.Select(e => new
            {
                e.Id,
                e.Name,
                e.IsEnabled,
                e.Version,
                hasOptions = e.OptionsPage is not null,
                hasPopup = e.PopupPage is not null,
            }),
        });
    }
}
