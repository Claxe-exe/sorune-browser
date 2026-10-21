// Sorune page translation. The app injects window.__soruneI18n (language + dictionary) on its
// own pages only. Turkish text is the key; anything missing from the dictionary stays Turkish.
// Loaded after the page's markup and before its script, so static text is translated pre-paint.
"use strict";

(() => {
    const data = window.__soruneI18n || { lang: "tr", locale: "tr-TR", strings: {} };
    const dict = data.strings || {};

    window.i18n = { lang: data.lang, locale: data.locale };

    /** Translates Turkish text; {0}, {1} ... are filled from the extra arguments. */
    window.t = (text, ...args) => {
        let result = Object.prototype.hasOwnProperty.call(dict, text) ? dict[text] : text;
        args.forEach((arg, i) => { result = result.split(`{${i}}`).join(String(arg)); });
        return result;
    };

    if (data.lang === "tr") return;
    document.documentElement.lang = data.lang;

    const lookup = (raw) => {
        const key = raw.replace(/\s+/g, " ").trim();
        return key && Object.prototype.hasOwnProperty.call(dict, key) ? raw.replace(raw.trim(), dict[key]) : null;
    };

    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    const nodes = [];
    while (walker.nextNode()) nodes.push(walker.currentNode);
    for (const node of nodes) {
        const translated = lookup(node.nodeValue);
        if (translated !== null) node.nodeValue = translated;
    }

    for (const el of document.querySelectorAll("[placeholder], [title], [aria-label]")) {
        for (const attr of ["placeholder", "title", "aria-label"]) {
            const value = el.getAttribute(attr);
            const translated = value && lookup(value);
            if (translated) el.setAttribute(attr, translated);
        }
    }

    document.title = lookup(document.title) ?? document.title;
})();
