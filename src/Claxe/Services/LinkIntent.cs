namespace Claxe.Services;

/// <summary>
/// WebView2 doesn't say whether a new window came from a middle click or Ctrl+click (which
/// Chrome opens as a background tab) or from target=_blank (foreground). This script, added to
/// every page, tells Sorune just before such a click. The message carries nothing else; a site
/// sending it on its own can only make its own pop-ups open in the background.
/// </summary>
public static class LinkIntent
{
    public const string BackgroundMessage = "{\"__soruneOpen\":\"background\"}";

    public const string Script = """
        (() => {
          if (window.__soruneLinkIntent) return;
          Object.defineProperty(window, "__soruneLinkIntent", { value: true });
          const tell = (event) => {
            const link = event.target && event.target.closest && event.target.closest("a[href]");
            if (!link || !window.chrome || !window.chrome.webview) return;
            const background = event.button === 1 || (event.button === 0 && (event.ctrlKey || event.metaKey) && !event.shiftKey);
            if (background) window.chrome.webview.postMessage({ __soruneOpen: "background" });
          };
          addEventListener("mousedown", tell, true);
          addEventListener("click", tell, true);
        })();
        """;
}
