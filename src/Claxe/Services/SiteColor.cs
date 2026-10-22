namespace Claxe.Services;

/// <summary>
/// Reads the colour a site shows right under the toolbar, so the toolbar can fade into it:
/// the first opaque background at the top centre of the page, else the site's theme-color,
/// else the page canvas. Returns "#rrggbb".
/// </summary>
public static class SiteColor
{
    public const string Script = """
        (() => {
          const hex = (c) => {
            const m = /rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:\s*[,/]\s*([\d.]+%?))?\s*\)/.exec(c || "");
            if (!m) return null;
            if (m[4] !== undefined) {
              const a = m[4].endsWith("%") ? parseFloat(m[4]) / 100 : parseFloat(m[4]);
              if (a < 0.85) return null;
            }
            return "#" + [m[1], m[2], m[3]].map(v => Math.min(255, Math.round(+v)).toString(16).padStart(2, "0")).join("");
          };
          let el = document.elementFromPoint(Math.floor(innerWidth / 2), 2);
          while (el) {
            const c = hex(getComputedStyle(el).backgroundColor);
            if (c) return c;
            el = el.parentElement;
          }
          const meta = [...document.querySelectorAll('meta[name="theme-color"]')]
            .find(m => !m.media || matchMedia(m.media).matches);
          if (meta && document.documentElement) {
            const probe = document.createElement("i");
            probe.style.color = meta.content;
            document.documentElement.append(probe);
            const c = hex(getComputedStyle(probe).color);
            probe.remove();
            if (c) return c;
          }
          const scheme = document.documentElement ? getComputedStyle(document.documentElement).colorScheme : "";
          return /dark/.test(scheme) && !/light/.test(scheme) ? "#121212" : "#ffffff";
        })()
        """;
}
