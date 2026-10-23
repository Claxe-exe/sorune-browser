using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Claxe.Services;

/// <summary>
/// "Translate screen": text inside pictures too, like reading a foreign webtoon. The visible
/// part of the page is captured, Google Lens translates it (in a tiny helper window, in the
/// user's own browser profile) and the patches Lens paints over the text are laid on top of
/// the page at the same place. They are anchored to the page, so they scroll with it; press
/// again further down for the next screen.
///
/// Lens has no public API: this drives its web page. If Google changes that page, this fails
/// with a message instead of doing anything odd.
/// </summary>
public static class ScreenTranslator
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(25);

    /// <summary>Earlier patches are hidden while capturing, so they aren't sent to Google again.</summary>
    private const string HideOverlaysScript = """
        document.querySelectorAll("[data-sorune-lens]").forEach((el) => el.style.visibility = "hidden"); true
        """;

    private const string ShowOverlaysScript = """
        document.querySelectorAll("[data-sorune-lens]").forEach((el) => el.style.visibility = ""); true
        """;

    public const string RemoveOverlaysScript = """
        document.querySelectorAll("[data-sorune-lens]").forEach((el) => el.remove()); true
        """;

    /// <summary>What the Lens page looked like when it got stuck, for the log.</summary>
    private const string DiagnoseScript = """
        (() => {
            const imgs = [...document.querySelectorAll("img")].map((i) => Math.round(i.getBoundingClientRect().width)).sort((a, b) => b - a);
            const buttons = [...document.querySelectorAll("button, [role=button]")]
                .map((b) => b.getAttribute("aria-label") || "").filter((l) => /çevir|translat/i.test(l));
            return `viewport ${innerWidth}x${innerHeight}, ${document.visibilityState}, img ${imgs.length} (en geniş ${imgs.slice(0, 3).join("/")}), düğmeler [${buttons.join(" | ")}]`;
        })()
        """;

    /// <summary>Runs in the Lens result page: presses "translate text in image", then hands back the painted canvas.</summary>
    private const string LensStepScript = """
        (() => {
            if (/consent\./.test(location.host)) return { state: "consent" };
            const image = [...document.querySelectorAll("img")].find((i) => i.getBoundingClientRect().width > 300 && i.src.startsWith("data:"));
            if (!image) return { state: "loading" };
            const box = image.getBoundingClientRect();
            const canvas = [...document.querySelectorAll("canvas")].find((c) => c.width > 300 && Math.abs(c.getBoundingClientRect().width - box.width) < 4);
            if (!window.__soruneLensClicked) {
                const inside = (el) => { const r = el.getBoundingClientRect(); return r.x >= box.x - 4 && r.y >= box.y - 4 && r.right <= box.right + 4 && r.bottom <= box.bottom + 4; };
                const button = [...document.querySelectorAll("button, [role=button]")].find((b) => {
                    const label = (b.getAttribute("aria-label") || b.textContent || "").toLowerCase();
                    return /çevir|translat|übersetz|tradu/.test(label) && !/harf|translit/.test(label) && inside(b);
                });
                if (!button) return { state: "loading" };
                button.click();
                window.__soruneLensClicked = Date.now();
                return { state: "translating" };
            }
            if (!canvas) return { state: "translating" };
            const ctx = canvas.getContext("2d");
            const data = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
            let painted = 0;
            for (let i = 3; i < data.length; i += 64) if (data[i] > 8) painted++;
            // Done once the painting stops changing; nothing painted after a while means no text.
            const last = window.__soruneLensPainted;
            window.__soruneLensPainted = painted;
            const waited = Date.now() - window.__soruneLensClicked;
            if (painted > 0 && painted === last && waited > 1200) return { state: "done", image: canvas.toDataURL("image/png") };
            if (painted === 0 && waited > 9000) return { state: "empty" };
            return { state: "translating" };
        })()
        """;

    /// <summary>
    /// Translates what <paramref name="page"/> shows into <paramref name="language"/> and lays it
    /// over the page. Returns a note for the user when it didn't work, null when it did.
    /// </summary>
    public static async Task<string?> TranslateVisibleAsync(CoreWebView2 page, string language)
    {
        byte[] screen;
        try
        {
            await page.ExecuteScriptAsync(HideOverlaysScript);
            using var stream = new MemoryStream();
            await page.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, stream);
            screen = stream.ToArray();
        }
        finally
        {
            await page.ExecuteScriptAsync(ShowOverlaysScript);
        }

        string? overlayUrl;
        try
        {
            (overlayUrl, var note) = await RunLensAsync(screen, language);
            if (note is not null)
                return note;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or JsonException or TaskCanceledException)
        {
            Log.Info($"Ekran çevirisi başarısız: {ex.Message}");
            return L.T("Google Lens'e ulaşılamadı, sonra tekrar dene.");
        }

        if (overlayUrl is null || DecodePatch(overlayUrl) is not { } patch)
            return L.T("Bu ekranda çevrilecek yazı bulamadım.");

        await page.ExecuteScriptAsync(PlaceScript(patch));
        return null;
    }

    // ---------------------------------------------------------------- Lens

    private static async Task<(string? Image, string? Note)> RunLensAsync(byte[] jpeg, string language)
    {
        var window = CreateHelperWindow(out var webView);
        try
        {
            window.Show();
            await webView.EnsureCoreWebView2Async(await BrowserEnvironment.GetAsync());
            var core = webView.CoreWebView2;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;

            var boundary = "----SoruneLens" + Guid.NewGuid().ToString("N");
            var body = new MemoryStream();
            void Write(string text) => body.Write(Encoding.UTF8.GetBytes(text));
            Write($"--{boundary}\r\nContent-Disposition: form-data; name=\"encoded_image\"; filename=\"screen.jpg\"\r\nContent-Type: image/jpeg\r\n\r\n");
            body.Write(jpeg);
            Write($"\r\n--{boundary}--\r\n");
            body.Position = 0;

            var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var request = core.Environment.CreateWebResourceRequest(
                $"https://lens.google.com/v3/upload?hl={Uri.EscapeDataString(language)}&re=df&st={stamp}&ep=gsbubb",
                "POST", body, $"Content-Type: multipart/form-data; boundary={boundary}");
            core.NavigateWithWebResourceRequest(request);

            var deadline = DateTime.UtcNow + Timeout;
            string? state = null;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(400);
                if (core.Source?.Contains("google.", StringComparison.OrdinalIgnoreCase) != true)
                    continue;

                using var result = JsonDocument.Parse(await core.ExecuteScriptAsync(LensStepScript));
                state = result.RootElement.TryGetProperty("state", out var s) ? s.GetString() : null;
                switch (state)
                {
                    case "done":
                        return (result.RootElement.GetProperty("image").GetString(), null);
                    case "empty":
                        return (null, null);
                    case "consent":
                        return (null, L.T("Google önce bir onay sayfası gösteriyor: google.com'u Sorune'de bir kez açıp seçimini yap, sonra tekrar dene."));
                }
            }
            // Where it got stuck (only the address's host and path: the rest is Lens's session).
            var at = Uri.TryCreate(core.Source, UriKind.Absolute, out var source) ? source.Host + source.AbsolutePath : "?";
            string details;
            try
            {
                details = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(DiagnoseScript)) ?? "";
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or JsonException)
            {
                details = ex.Message;
            }
            Log.Info($"Ekran çevirisi zaman aşımı: son durum {state ?? "sayfa yüklenmedi"}, adres {at}, {details}");
            return (null, L.T("Google Lens zamanında yanıt vermedi, tekrar dene."));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// A full-size window pushed past the bottom-right corner of the screen, so only its top-left
    /// pixel is on screen. Chromium only paints pages it considers visible, and a window that is
    /// fully off-screen or covered counts as hidden; one uncovered pixel keeps Lens drawing, and
    /// the page still gets a real 1280×900 viewport to lay out in.
    /// </summary>
    private static Window CreateHelperWindow(out WebView2 webView)
    {
        webView = new WebView2();
        var area = SystemParameters.WorkArea;
        return new Window
        {
            Width = 1280,
            Height = 900,
            Left = area.Right - 1,
            Top = area.Bottom - 1,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Focusable = false,
            Title = "Sorune Lens",
            Content = webView,
        };
    }

    // ---------------------------------------------------------------- placing the patches

    private sealed record Patch(int OverlayWidth, int OverlayHeight, int X, int Y, int Width, int Height, string Rgba);

    /// <summary>Crops the painted part of Lens's canvas (transparent elsewhere) to RGBA for the page.</summary>
    private static Patch? DecodePatch(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0)
            return null;

        var decoder = BitmapDecoder.Create(new MemoryStream(Convert.FromBase64String(dataUrl[(comma + 1)..])), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        int width = bitmap.PixelWidth, height = bitmap.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        int left = width, top = height, right = -1, bottom = -1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (pixels[y * stride + x * 4 + 3] <= 8)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }
        }
        if (right < 0)
            return null;

        int w = right - left + 1, h = bottom - top + 1;
        var rgba = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var from = (top + y) * stride + (left + x) * 4;
                var to = (y * w + x) * 4;
                rgba[to] = pixels[from + 2];
                rgba[to + 1] = pixels[from + 1];
                rgba[to + 2] = pixels[from];
                rgba[to + 3] = pixels[from + 3];
            }
        }
        return new Patch(width, height, left, top, w, h, Convert.ToBase64String(rgba));
    }

    /// <summary>
    /// Draws the patch on a canvas pinned to the document where the screen was (so it scrolls
    /// with the page). Pixels go in as ImageData: no image URL, so the site's CSP can't block it.
    /// </summary>
    private static string PlaceScript(Patch patch)
        => $$"""
            ((ow, oh, px, py, pw, ph, b64) => {
                const bytes = Uint8ClampedArray.from(atob(b64), (c) => c.charCodeAt(0));
                const canvas = document.createElement("canvas");
                canvas.width = pw;
                canvas.height = ph;
                canvas.getContext("2d").putImageData(new ImageData(bytes, pw, ph), 0, 0);
                const sx = innerWidth / ow, sy = innerHeight / oh;
                canvas.dataset.soruneLens = "";
                Object.assign(canvas.style, {
                    position: "absolute", left: (scrollX + px * sx) + "px", top: (scrollY + py * sy) + "px",
                    width: (pw * sx) + "px", height: (ph * sy) + "px", zIndex: "2147483646",
                    pointerEvents: "none", margin: "0", padding: "0", border: "0", maxWidth: "none",
                });
                document.documentElement.appendChild(canvas);
                if (!document.querySelector("[data-sorune-lens-bar]")) {
                    const bar = document.createElement("div");
                    bar.dataset.soruneLens = "";
                    bar.dataset.soruneLensBar = "";
                    const shadow = bar.attachShadow({ mode: "closed" });
                    const button = document.createElement("button");
                    button.textContent = {{JsonSerializer.Serialize(L.T("Ekran çevirisini kaldır"))}};
                    Object.assign(button.style, {
                        font: "600 12px Segoe UI, sans-serif", color: "#fff", background: "rgba(20,16,28,.85)",
                        border: "1px solid rgba(181,131,255,.7)", borderRadius: "999px", padding: "6px 12px", cursor: "pointer",
                    });
                    button.onclick = () => document.querySelectorAll("[data-sorune-lens]").forEach((el) => el.remove());
                    shadow.append(button);
                    Object.assign(bar.style, { position: "fixed", right: "16px", bottom: "16px", zIndex: "2147483647" });
                    document.documentElement.appendChild(bar);
                }
                return true;
            })({{patch.OverlayWidth}}, {{patch.OverlayHeight}}, {{patch.X}}, {{patch.Y}}, {{patch.Width}}, {{patch.Height}}, "{{patch.Rgba}}")
            """;
}
