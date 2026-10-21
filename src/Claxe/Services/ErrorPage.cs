using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Claxe.Services;

/// <summary>
/// Claxe-styled replacement for WebView2's network error page. It is drawn into the
/// existing error document (no extra navigation), so Back, Reload and history behave
/// exactly as with the built-in page. Certificate warnings are never replaced.
/// </summary>
public static class ErrorPage
{
    /// <summary>Title and explanation for errors Claxe restyles; null keeps WebView2's own page.</summary>
    public static (string Title, string Detail)? Describe(CoreWebView2WebErrorStatus status) => status switch
    {
        CoreWebView2WebErrorStatus.HostNameNotResolved =>
            (L.T("Bu siteye ulaşılamıyor"), L.T("Adres bulunamadı. Yazımı kontrol et ya da internet bağlantını dene.")),
        CoreWebView2WebErrorStatus.Disconnected =>
            (L.T("İnternet bağlantısı yok"), L.T("Bilgisayarın internete bağlı görünmüyor. Bağlantını kontrol edip tekrar dene.")),
        CoreWebView2WebErrorStatus.CannotConnect or CoreWebView2WebErrorStatus.ServerUnreachable =>
            (L.T("Bu siteye bağlanılamadı"), L.T("Sunucu yanıt vermiyor ya da bağlantıyı reddetti. Biraz sonra tekrar dene.")),
        CoreWebView2WebErrorStatus.Timeout =>
            (L.T("Site çok geç yanıt verdi"), L.T("Bağlantı zaman aşımına uğradı. Sunucu yoğun olabilir.")),
        CoreWebView2WebErrorStatus.ConnectionReset =>
            (L.T("Bağlantı kesildi"), L.T("Sayfa yüklenirken bağlantı sıfırlandı. Tekrar denemek genelde işe yarar.")),
        CoreWebView2WebErrorStatus.ErrorHttpInvalidServerResponse =>
            (L.T("Sunucu geçersiz yanıt gönderdi"), L.T("Site şu an düzgün çalışmıyor olabilir.")),
        CoreWebView2WebErrorStatus.RedirectFailed =>
            (L.T("Yönlendirme başarısız"), L.T("Site seni çok fazla ya da hatalı yere yönlendiriyor.")),
        CoreWebView2WebErrorStatus.UnexpectedError =>
            (L.T("Sayfa yüklenemedi"), L.T("Beklenmeyen bir hata oluştu. Tekrar dene.")),
        _ => null,
    };

    public static string BuildScript(string? url, string title, string detail)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : url ?? "";
        var theme = ThemeService.Current.Colors;
        var data = JsonSerializer.Serialize(new
        {
            title,
            detail,
            host,
            accent = ThemeService.Accent,
            accent2 = theme.AccentSecondary,
            @base = theme.Base,
            surface = theme.Surface,
            text = theme.TextPrimary,
            dim = theme.TextSecondary,
            mascot = InternalPages.AssetsOrigin + "Mascot/sleepy.png",
            retry = L.T("Tekrar dene"),
        });

        // Only touches Chromium's error document (never a real page). Styles are set through
        // the DOM API so the error page's own content security policy can't strip them.
        return $$"""
            (() => {
              if (location.protocol !== 'chrome-error:') return;
              const d = {{data}};
              const css = (el, s) => { Object.assign(el.style, s); return el; };
              const make = (tag, text, s) => { const el = document.createElement(tag); if (text) el.textContent = text; return css(el, s || {}); };
              document.head.replaceChildren();
              document.body.replaceChildren();
              document.title = d.title;
              css(document.documentElement, { height: '100%', background: d.base, colorScheme: 'dark' });
              css(document.body, {
                margin: '0', minHeight: '100%', display: 'grid', placeItems: 'center',
                background: `radial-gradient(ellipse at 50% 30%, ${d.surface} 0%, ${d.base} 70%)`,
                color: d.text, fontFamily: '"Segoe UI Variable Text", "Segoe UI", sans-serif'
              });
              const box = make('div', '', { textAlign: 'center', maxWidth: '520px', padding: '24px' });
              const img = make('img', '', { height: '150px', marginBottom: '8px', filter: `drop-shadow(0 0 18px ${d.accent}66)` });
              img.alt = '';
              img.onerror = () => img.remove();
              img.src = d.mascot;
              const h1 = make('h1', d.title, { fontSize: '26px', fontWeight: '600', margin: '8px 0 10px' });
              const p = make('p', d.detail, { color: d.dim, fontSize: '15px', lineHeight: '1.5', margin: '0 0 6px' });
              const site = make('p', d.host, { color: d.accent, fontSize: '13px', margin: '0 0 24px', letterSpacing: '0.04em' });
              const button = make('button', d.retry, {
                height: '40px', padding: '0 22px', border: '0', borderRadius: '12px', cursor: 'pointer',
                background: `linear-gradient(90deg, ${d.accent}, ${d.accent2})`, color: '#120e18',
                fontSize: '14px', fontWeight: '600', boxShadow: `0 0 22px ${d.accent}55`
              });
              button.onclick = () => location.reload();
              box.append(img, h1, p, site, button);
              document.body.append(box);
              button.focus();
            })();
            """;
    }
}
