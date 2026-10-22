namespace Claxe.Services;

/// <summary>
/// Reader mode. The article is read out of the page as plain blocks (headings, paragraphs,
/// list items, quotes, code, https images) — never as HTML — so the reader page can show it
/// with textContent only and nothing from the site can run there.
/// </summary>
public static class ReaderMode
{
    /// <summary>True when the page has enough running text to be worth reading mode.</summary>
    public const string DetectScript = """
        (() => {
          const article = document.querySelector("article");
          if (article && (article.innerText || "").trim().length > 1500) return true;
          let total = 0;
          for (const p of document.querySelectorAll("p")) {
            const n = (p.innerText || "").trim().length;
            if (n > 80) total += n;
            if (total > 2000) return true;
          }
          return false;
        })()
        """;

    public const string ExtractScript = """
        (() => {
          const len = (el) => (el.innerText || "").trim().length;
          let root = document.querySelector("article");
          if (!root || len(root) < 600) {
            // The element holding the most paragraph text.
            const score = new Map();
            let best = null, bestScore = 0;
            for (const p of document.querySelectorAll("p")) {
              const parent = p.parentElement;
              if (!parent) continue;
              const s = (score.get(parent) || 0) + len(p);
              score.set(parent, s);
              if (s > bestScore) { bestScore = s; best = parent; }
            }
            root = best;
          }
          if (!root) return null;

          const blocks = [];
          const clean = (t) => (t || "").replace(/[ \t]+\n/g, "\n").replace(/\n{3,}/g, "\n\n").trim();
          const skip = "nav,aside,footer,form,button,script,style,noscript,iframe,svg,canvas,[role=navigation],[aria-hidden=true],.share,.related,.ad,.advert";
          const walk = (el) => {
            for (const node of el.children) {
              if (blocks.length > 3000) return;
              if (node.matches(skip)) continue;
              const tag = node.tagName.toLowerCase();
              if (/^h[1-6]$/.test(tag)) {
                const text = clean(node.innerText);
                if (text) blocks.push({ t: tag <= "h2" ? "h2" : "h3", text });
              } else if (tag === "p") {
                const text = clean(node.innerText);
                if (text.length > 1) blocks.push({ t: "p", text });
                for (const img of node.querySelectorAll("img")) addImage(img);
              } else if (tag === "img") {
                addImage(node);
              } else if (tag === "ul" || tag === "ol") {
                for (const li of node.querySelectorAll(":scope > li")) {
                  const text = clean(li.innerText);
                  if (text) blocks.push({ t: "li", text, ordered: tag === "ol" });
                }
              } else if (tag === "blockquote") {
                const text = clean(node.innerText);
                if (text) blocks.push({ t: "quote", text });
              } else if (tag === "pre") {
                blocks.push({ t: "pre", text: node.innerText });
              } else if (tag === "figcaption") {
                const text = clean(node.innerText);
                if (text) blocks.push({ t: "caption", text });
              } else if (node.children.length) {
                walk(node);
              } else {
                const text = clean(node.innerText);
                if (text.length > 80) blocks.push({ t: "p", text });
              }
            }
          };
          const addImage = (img) => {
            const src = img.currentSrc || img.src || "";
            if (src.startsWith("https://") && (img.naturalWidth || img.width) >= 160) blocks.push({ t: "img", src, alt: img.alt || "" });
          };
          walk(root);

          const meta = (selector) => document.querySelector(selector)?.content || "";
          return {
            title: meta("meta[property='og:title']") || document.title,
            site: meta("meta[property='og:site_name']") || location.hostname.replace(/^www\./, ""),
            byline: meta("meta[name='author']"),
            url: location.href,
            blocks,
          };
        })()
        """;
}
