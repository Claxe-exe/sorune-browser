using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Claxe.Services;

/// <summary>
/// "Translate screen": text inside pictures too, like reading a foreign webtoon. The visible
/// part of the page is captured and translated by Google Translate's image mode (in a helper
/// window off the screen, in the user's own browser profile). Translate returns the same
/// picture with the text replaced; comparing it with the capture leaves only the changed
/// parts (speech bubbles, captions), which are laid over the page at the same place. They are
/// anchored to the page, so they scroll with it; press again further down for the next screen.
///
/// Translate has no public API for this: it drives the web page. If Google changes that page,
/// this fails with a message instead of doing anything odd.
/// </summary>
public static class ScreenTranslator
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Earlier patches are hidden while capturing, so they aren't sent to Google again. Returns
    /// where the screen was, so the patches land there even if the page scrolls meanwhile.
    /// </summary>
    private const string HideOverlaysScript = """
        document.querySelectorAll("[data-sorune-lens]").forEach((el) => el.style.visibility = "hidden");
        ({ x: scrollX, y: scrollY, w: innerWidth, h: innerHeight })
        """;

    /// <summary>
    /// What the screen shows, in short: address, scroll position, size, picture count and where
    /// the element in the middle sits (that also moves on sites that scroll inside a box).
    /// The same answer twice means nothing moved.
    /// </summary>
    private const string ViewScript = """
        (() => {
            const middle = document.elementFromPoint(innerWidth / 2, innerHeight / 2);
            const top = middle ? Math.round(middle.getBoundingClientRect().top) : 0;
            return `${location.href}|${Math.round(scrollX)}|${Math.round(scrollY)}|${innerWidth}x${innerHeight}|${document.images.length}|${top}`;
        })()
        """;

    public static async Task<string> ViewSignatureAsync(CoreWebView2 page) => await page.ExecuteScriptAsync(ViewScript);

    private const string ShowOverlaysScript = """
        document.querySelectorAll("[data-sorune-lens]").forEach((el) => el.style.visibility = ""); true
        """;

    public const string RemoveOverlaysScript = """
        document.querySelectorAll("[data-sorune-lens]").forEach((el) => el.remove()); true
        """;

    /// <summary>Puts the capture into Translate's image picker (as if the file had been chosen).</summary>
    private const string UploadScript = """
        ((b64) => {
            const input = [...document.querySelectorAll("input[type=file]")].find((i) => /image\/jpeg/.test(i.accept));
            if (!input) return "waiting";
            const bytes = Uint8Array.from(atob(b64), (c) => c.charCodeAt(0));
            const transfer = new DataTransfer();
            transfer.items.add(new File([bytes], "screen.jpg", { type: "image/jpeg" }));
            input.files = transfer.files;
            input.dispatchEvent(new Event("change", { bubbles: true }));
            return "sent";
        })
        """;

    /// <summary>
    /// Waits for the translated picture (a blob: image of the uploaded size, shown once the
    /// "download translation" button is there) and reads it as a data: URL.
    /// </summary>
    private const string ResultScript = """
        ((width) => {
            if (/consent\./.test(location.host)) return { state: "consent" };
            const ready = [...document.querySelectorAll("button, [role=button]")]
                .some((b) => /indir|download|herunterladen|télécharger|descargar/i.test(b.getAttribute("aria-label") || b.textContent || ""));
            const image = [...document.querySelectorAll("img")].find((i) => i.src.startsWith("blob:") && i.naturalWidth === width);
            if (!ready || !image) return { state: "waiting" };
            if (!window.__soruneShot) {
                window.__soruneShot = "pending";
                fetch(image.src).then((r) => r.blob())
                    .then((blob) => new Promise((done) => { const reader = new FileReader(); reader.onload = () => done(reader.result); reader.readAsDataURL(blob); }))
                    .then((url) => window.__soruneShot = url, () => window.__soruneShot = "error");
                return { state: "reading" };
            }
            if (window.__soruneShot === "pending") return { state: "reading" };
            if (window.__soruneShot === "error") return { state: "error" };
            return { state: "done", image: window.__soruneShot };
        })
        """;

    /// <summary>
    /// Translates what <paramref name="page"/> shows into <paramref name="language"/> and lays it
    /// over the page. Returns a note for the user when it didn't work, null when it did.
    /// </summary>
    public static async Task<string?> TranslateVisibleAsync(CoreWebView2 page, string language)
    {
        byte[] screen;
        View view;
        try
        {
            view = JsonSerializer.Deserialize<View>(await page.ExecuteScriptAsync(HideOverlaysScript), JsonOptions) ?? new View(0, 0, 0, 0);
            using var stream = new MemoryStream();
            await page.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, stream);
            screen = stream.ToArray();
        }
        finally
        {
            await page.ExecuteScriptAsync(ShowOverlaysScript);
        }

        var original = Decode(screen);
        string? translatedUrl;
        try
        {
            (translatedUrl, var note) = await RunTranslateAsync(screen, original.PixelWidth, language);
            if (note is not null)
                return note;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or JsonException or TaskCanceledException)
        {
            Log.Info($"Ekran çevirisi başarısız: {ex.Message}");
            return L.T("Google Çeviri'ye ulaşılamadı, sonra tekrar dene.");
        }

        if (translatedUrl is null)
            return L.T("Bu ekranda çevrilecek yazı bulamadım.");

        var comma = translatedUrl.IndexOf(',');
        var translated = Decode(Convert.FromBase64String(translatedUrl[(comma + 1)..]));
        if (ChangedParts(original, translated) is not { } patch)
            return L.T("Bu ekranda çevrilecek yazı bulamadım.");

        await page.ExecuteScriptAsync(PlaceScript(patch, view));
        return null;
    }

    // ---------------------------------------------------------------- Google Translate

    private static async Task<(string? Image, string? Note)> RunTranslateAsync(byte[] jpeg, int width, string language)
    {
        var window = CreateHelperWindow(out var webView);
        try
        {
            window.Show();
            await webView.EnsureCoreWebView2Async(await BrowserEnvironment.GetAsync());
            var core = webView.CoreWebView2;
            core.Settings.AreDefaultScriptDialogsEnabled = false;
            core.Navigate($"https://translate.google.com/?sl=auto&tl={Uri.EscapeDataString(language)}&op=images");

            var upload = $"{UploadScript}({JsonSerializer.Serialize(Convert.ToBase64String(jpeg))})";
            var sent = false;
            string? state = null;
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(400);
                if (core.Source?.Contains("google.", StringComparison.OrdinalIgnoreCase) != true)
                    continue;

                if (!sent)
                {
                    sent = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(upload)) == "sent";
                    state = sent ? "sent" : "no picker";
                    continue;
                }

                using var result = JsonDocument.Parse(await core.ExecuteScriptAsync($"{ResultScript}({width})"));
                state = result.RootElement.TryGetProperty("state", out var s) ? s.GetString() : null;
                switch (state)
                {
                    case "done":
                        return (result.RootElement.GetProperty("image").GetString(), null);
                    case "error":
                        return (null, L.T("Çevrilen resim okunamadı, tekrar dene."));
                    case "consent":
                        return (null, L.T("Google önce bir onay sayfası gösteriyor: google.com'u Sorune'de bir kez açıp seçimini yap, sonra tekrar dene."));
                }
            }

            // Where it got stuck (only the address's host and path).
            var at = Uri.TryCreate(core.Source, UriKind.Absolute, out var source) ? source.Host + source.AbsolutePath : "?";
            Log.Info($"Ekran çevirisi zaman aşımı: son durum {state ?? "sayfa yüklenmedi"}, adres {at}");
            // Translate shows no result for a picture without text; after a long wait that is the likely reason.
            return (null, sent ? null : L.T("Google Çeviri zamanında yanıt vermedi, tekrar dene."));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// A browser window placed beyond every monitor, so it never shows up (not even on a second
    /// screen). Translate's image mode works from the network, so the page needn't be painted.
    /// </summary>
    private static Window CreateHelperWindow(out WebView2 webView)
    {
        webView = new WebView2();
        return new Window
        {
            Width = 1280,
            Height = 900,
            Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth + 200,
            Top = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight + 200,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            Title = "Sorune Translate",
            Content = webView,
        };
    }

    // ---------------------------------------------------------------- what changed

    private sealed record Patch(int OverlayWidth, int OverlayHeight, int X, int Y, int Width, int Height, string Rgba);

    /// <summary>The screen in page coordinates (CSS pixels) at the moment of the capture.</summary>
    private sealed record View(double X, double Y, double W, double H);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static BitmapSource Decode(byte[] bytes)
    {
        var decoder = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var bitmap = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        bitmap.Freeze();
        return bitmap;
    }

    private const int Block = 8;

    /// <summary>
    /// Compares the capture with Translate's picture in 8×8 blocks; blocks that differ more than
    /// JPEG noise (plus two blocks around them, so no trace of the old letters is left) are kept from the
    /// translation, everything else is transparent. Null when nothing changed.
    /// </summary>
    private static Patch? ChangedParts(BitmapSource original, BitmapSource translated)
    {
        if (translated.PixelWidth != original.PixelWidth || translated.PixelHeight != original.PixelHeight)
            translated = new TransformedBitmap(translated, new ScaleTransform(
                (double)original.PixelWidth / translated.PixelWidth, (double)original.PixelHeight / translated.PixelHeight));

        int width = original.PixelWidth, height = original.PixelHeight, stride = width * 4;
        var a = new byte[stride * height];
        var b = new byte[stride * height];
        original.CopyPixels(a, stride, 0);
        new FormatConvertedBitmap(translated, PixelFormats.Bgra32, null, 0).CopyPixels(b, stride, 0);

        int columns = (width + Block - 1) / Block, rows = (height + Block - 1) / Block;
        var changed = new bool[columns, rows];
        for (var by = 0; by < rows; by++)
        {
            for (var bx = 0; bx < columns; bx++)
            {
                long sum = 0;
                var count = 0;
                for (var y = by * Block; y < Math.Min(height, (by + 1) * Block); y++)
                {
                    for (var x = bx * Block; x < Math.Min(width, (bx + 1) * Block); x++)
                    {
                        var i = y * stride + x * 4;
                        sum += Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
                        count++;
                    }
                }
                changed[bx, by] = sum / Math.Max(1, count) > 24;
            }
        }

        var keep = new bool[columns, rows];
        int left = columns, top = rows, right = -1, bottom = -1;
        for (var by = 0; by < rows; by++)
        {
            for (var bx = 0; bx < columns; bx++)
            {
                if (!changed[bx, by])
                    continue;
                for (var dy = -2; dy <= 2; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        int nx = bx + dx, ny = by + dy;
                        if (nx < 0 || ny < 0 || nx >= columns || ny >= rows)
                            continue;
                        keep[nx, ny] = true;
                        left = Math.Min(left, nx);
                        right = Math.Max(right, nx);
                        top = Math.Min(top, ny);
                        bottom = Math.Max(bottom, ny);
                    }
                }
            }
        }
        if (right < 0)
            return null;

        int px = left * Block, py = top * Block;
        int pw = Math.Min(width, (right + 1) * Block) - px, ph = Math.Min(height, (bottom + 1) * Block) - py;
        var rgba = new byte[pw * ph * 4];
        for (var y = 0; y < ph; y++)
        {
            for (var x = 0; x < pw; x++)
            {
                if (!keep[(px + x) / Block, (py + y) / Block])
                    continue;
                var from = (py + y) * stride + (px + x) * 4;
                var to = (y * pw + x) * 4;
                rgba[to] = b[from + 2];
                rgba[to + 1] = b[from + 1];
                rgba[to + 2] = b[from];
                rgba[to + 3] = 255;
            }
        }
        return new Patch(width, height, px, py, pw, ph, Convert.ToBase64String(rgba));
    }

    /// <summary>
    /// Draws the patch on a canvas pinned to the document where the screen was (so it scrolls
    /// with the page). Patches from an earlier translation of the same screen are replaced.
    /// Pixels go in as ImageData: no image URL, so the site's CSP can't block it.
    /// </summary>
    private static string PlaceScript(Patch patch, View view)
        => $$"""
            ((ow, oh, px, py, pw, ph, b64, vx, vy, vw, vh) => {
                if (!vw || !vh) { vx = scrollX; vy = scrollY; vw = innerWidth; vh = innerHeight; }
                for (const old of document.querySelectorAll("canvas[data-sorune-lens]")) {
                    const left = parseFloat(old.style.left), top = parseFloat(old.style.top);
                    if (left >= vx - 1 && top >= vy - 1 && left + parseFloat(old.style.width) <= vx + vw + 1
                        && top + parseFloat(old.style.height) <= vy + vh + 1) old.remove();
                }
                const bytes = Uint8ClampedArray.from(atob(b64), (c) => c.charCodeAt(0));
                const canvas = document.createElement("canvas");
                canvas.width = pw;
                canvas.height = ph;
                canvas.getContext("2d").putImageData(new ImageData(bytes, pw, ph), 0, 0);
                const sx = vw / ow, sy = vh / oh;
                canvas.dataset.soruneLens = "";
                Object.assign(canvas.style, {
                    position: "absolute", left: (vx + px * sx) + "px", top: (vy + py * sy) + "px",
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
            })({{patch.OverlayWidth}}, {{patch.OverlayHeight}}, {{patch.X}}, {{patch.Y}}, {{patch.Width}}, {{patch.Height}}, "{{patch.Rgba}}",
               {{Num(view.X)}}, {{Num(view.Y)}}, {{Num(view.W)}}, {{Num(view.H)}})
            """;

    private static string Num(double value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
