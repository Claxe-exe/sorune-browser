using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Claxe.Services;

/// <summary>
/// Turns a <see cref="SiteStyle"/> into CSS and puts it on the page. The sheet is a constructed
/// stylesheet (adoptedStyleSheets): unlike a style element, a site's content security policy does
/// not block it, and it cannot load anything by @import.
/// </summary>
public static class SiteStyles
{
    private const string FontTargets =
        "body,body *:not(i):not(svg):not(svg *):not(pre):not(pre *):not(code)"
        + ":not([class*=\"icon\" i]):not([class*=\"material\" i]):not([class*=\"fa-\"]):not([class*=\"glyph\" i])";

    public static string BuildCss(SiteStyle? style, string accent)
    {
        if (style is not { Enabled: true })
            return "";

        var css = new StringBuilder();
        if (style.Hue != 0 || style.Saturation != 100 || style.Brightness != 100)
        {
            css.Append(CultureInfo.InvariantCulture,
                $"html{{filter:hue-rotate({style.Hue}deg) saturate({style.Saturation}%) brightness({style.Brightness}%)!important}}\n");
            // Pictures keep their own colours when the page is tinted.
            if (style.Hue != 0)
                css.Append(CultureInfo.InvariantCulture, $"img,video,canvas,iframe{{filter:hue-rotate({-style.Hue}deg)!important}}\n");
        }
        if (style.Font.Length > 0)
            css.Append(CultureInfo.InvariantCulture, $"{FontTargets}{{font-family:\"{style.Font}\",sans-serif!important}}\n");
        if (style.ThemeLinks && ThemeService.IsHexColor(accent))
            css.Append(CultureInfo.InvariantCulture, $"a,a *{{color:{accent}!important}}\n");
        foreach (var selector in style.Hidden)
            css.Append(selector).Append("{display:none!important}\n");
        css.Append(style.Css);
        return css.ToString();
    }

    /// <summary>Added to every tab: gives each new page its look before the page's own scripts run.</summary>
    public static string DocumentScript(IEnumerable<SiteStyle> styles, string accent)
    {
        var map = styles
            .Select(s => (s.Host, Css: BuildCss(s, accent)))
            .Where(s => s.Css.Length > 0)
            .ToDictionary(s => s.Host, s => s.Css);

        return """
            (() => {
              if (window.top !== window.self || window.__soruneSiteStyle) return;
              const styles = __STYLES__;
              const sheet = new CSSStyleSheet();
              // Some sites assign adoptedStyleSheets themselves, which would drop this sheet.
              const attach = () => {
                if (sheet.cssRules.length > 0 && !document.adoptedStyleSheets.includes(sheet))
                  document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
              };
              const apply = (css) => {
                sheet.replaceSync(css || "");
                attach();
              };
              Object.defineProperty(window, "__soruneSiteStyle", { value: apply });
              addEventListener("DOMContentLoaded", attach);
              addEventListener("load", attach);
              const host = location.hostname.toLowerCase().replace(/^www\./, "");
              if (Object.prototype.hasOwnProperty.call(styles, host)) apply(styles[host]);
            })();
            """.Replace("__STYLES__", JsonSerializer.Serialize(map));
    }

    /// <summary>Applies a look to the page that is already open.</summary>
    public static string ApplyCall(string css)
        => $"window.__soruneSiteStyle && window.__soruneSiteStyle({JsonSerializer.Serialize(css)});";

    /// <summary>The most colourful colour the page paints with ("#rrggbb"), or null: what "match my theme" turns into the accent.</summary>
    public const string DominantColorScript = """
        (() => {
          const totals = new Map();
          const add = (value, weight) => {
            const m = /rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:\s*[,/]\s*([\d.]+%?))?\s*\)/.exec(value);
            if (!m || (m[4] !== undefined && parseFloat(m[4]) < 0.6)) return;
            const [r, g, b] = [+m[1], +m[2], +m[3]];
            const max = Math.max(r, g, b);
            const min = Math.min(r, g, b);
            if (max < 60 || (max - min) / max < 0.4) return;
            const key = "#" + [r, g, b].map((v) => Math.round(v).toString(16).padStart(2, "0")).join("");
            totals.set(key, (totals.get(key) || 0) + weight);
          };
          const all = document.body ? document.body.querySelectorAll("a,button,svg,path,span,div,h1,h2,h3,header,nav,li") : [];
          for (let i = 0; i < all.length && i < 1500; i++) {
            const el = all[i];
            const box = el.getBoundingClientRect();
            if (box.width === 0 || box.height === 0) continue;
            const style = getComputedStyle(el);
            add(style.backgroundColor, Math.min(box.width * box.height, 40000));
            add(style.color, 200);
            if (el.tagName.toLowerCase() === "path") add(style.fill, 200);
          }
          let best = null;
          let most = 0;
          for (const [color, weight] of totals) if (weight > most) { best = color; most = weight; }
          return best;
        })()
        """;

    /// <summary>How far to turn the hue so that <paramref name="from"/> becomes <paramref name="to"/>, -180..180.</summary>
    public static int HueTurn(string from, string to)
    {
        var turn = (int)Math.Round(HueOf(to) - HueOf(from));
        return turn > 180 ? turn - 360 : turn < -180 ? turn + 360 : turn;
    }

    private static double HueOf(string hex)
    {
        var r = Convert.ToInt32(hex[1..3], 16) / 255.0;
        var g = Convert.ToInt32(hex[3..5], 16) / 255.0;
        var b = Convert.ToInt32(hex[5..7], 16) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var delta = max - Math.Min(r, Math.Min(g, b));
        if (delta == 0)
            return 0;

        var hue = max == r ? (g - b) / delta % 6 : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        return (hue * 60 + 360) % 360;
    }

    /// <summary>
    /// Lets the user point at elements to hide: a click reports a selector, the wheel moves to the
    /// surrounding element, Esc or a right click ends it. Only messages with the token count.
    /// </summary>
    public static string PickerScript(string token, string hint) => """
        (() => {
          if (window.__soruneSitePicker) window.__soruneSitePicker();
          const token = __TOKEN__;
          const tell = (message) => window.chrome.webview.postMessage({ __soruneSitePick: token, ...message });

          const frame = document.createElement("div");
          frame.style.cssText = "position:fixed;z-index:2147483646;pointer-events:none;border:2px solid #b583ff;border-radius:4px;background:rgba(181,131,255,.22);box-shadow:0 0 0 1px rgba(0,0,0,.4);display:none";
          const label = document.createElement("div");
          label.style.cssText = "position:fixed;z-index:2147483647;pointer-events:none;left:50%;top:12px;transform:translateX(-50%);padding:7px 14px;border-radius:10px;background:#1c1626;color:#eee9f7;font:600 13px 'Segoe UI',sans-serif;box-shadow:0 6px 20px rgba(0,0,0,.45)";
          label.textContent = __HINT__;
          document.documentElement.append(frame, label);

          let target = null;
          let origin = null;
          const show = () => {
            if (!target) { frame.style.display = "none"; return; }
            const box = target.getBoundingClientRect();
            frame.style.display = "block";
            frame.style.left = box.left + "px";
            frame.style.top = box.top + "px";
            frame.style.width = box.width + "px";
            frame.style.height = box.height + "px";
          };
          const pickable = (el) => el && el !== document.documentElement && el !== document.body;

          const stable = (name) => /^[A-Za-z_-][\w-]*$/.test(name) && name.length < 40 && !/\d{3,}|^css-|^sc-/i.test(name);
          const part = (el) => {
            if (el.id && stable(el.id)) return "#" + CSS.escape(el.id);
            let text = el.tagName.toLowerCase() + [...el.classList].filter(stable).slice(0, 2).map((c) => "." + CSS.escape(c)).join("");
            const parent = el.parentElement;
            if (parent && [...parent.children].filter((c) => c !== el && c.matches(text)).length > 0)
              text += ":nth-of-type(" + ([...parent.children].filter((c) => c.tagName === el.tagName).indexOf(el) + 1) + ")";
            return text;
          };
          const selectorOf = (el) => {
            const path = [];
            for (let node = el; pickable(node) && path.length < 7; node = node.parentElement) {
              path.unshift(part(node));
              const text = path.join(" > ");
              if (document.querySelectorAll(text).length === 1) return text;
            }
            return path.join(" > ");
          };

          const stop = (event) => { event.preventDefault(); event.stopImmediatePropagation(); };
          const onMove = (event) => {
            const el = document.elementFromPoint(event.clientX, event.clientY);
            target = pickable(el) ? el : null;
            origin = target;
            show();
          };
          const onWheel = (event) => {
            stop(event);
            if (!target) return;
            if (event.deltaY < 0 && pickable(target.parentElement)) target = target.parentElement;
            else if (event.deltaY > 0 && target !== origin) {
              let child = origin;
              while (child.parentElement !== target) child = child.parentElement;
              target = child;
            }
            show();
          };
          const onClick = (event) => {
            stop(event);
            if (target) tell({ hide: selectorOf(target) });
            target = null;
            show();
          };
          const finish = () => {
            for (const [name, handler] of events) removeEventListener(name, handler, true);
            frame.remove();
            label.remove();
            delete window.__soruneSitePicker;
          };
          const end = (event) => {
            stop(event);
            finish();
            tell({ done: true });
          };
          const onKey = (event) => { if (event.key === "Escape") end(event); };
          const events = [
            ["pointermove", onMove], ["wheel", onWheel], ["click", onClick], ["pointerdown", stop], ["pointerup", stop],
            ["mousedown", stop], ["mouseup", stop], ["auxclick", stop], ["contextmenu", end], ["keydown", onKey],
          ];
          for (const [name, handler] of events) addEventListener(name, handler, { capture: true, passive: false });
          Object.defineProperty(window, "__soruneSitePicker", { value: finish, configurable: true });
        })();
        """.Replace("__TOKEN__", JsonSerializer.Serialize(token)).Replace("__HINT__", JsonSerializer.Serialize(hint));
}
