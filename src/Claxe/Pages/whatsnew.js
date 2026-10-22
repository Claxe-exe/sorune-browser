// "What's new" page, opened once after Sorune updates itself.
"use strict";

if (window.top !== window.self) {
    document.documentElement.replaceChildren();
    throw new Error("Sorune sayfası çerçeve içinde açılamaz.");
}

const host = window.chrome && window.chrome.webview;
const $ = (id) => document.getElementById(id);

function send(message) {
    if (host) host.postMessage(message);
}

// Cards for the big release below; "open" asks the app to show the feature. Smaller updates
// since the version installed before are listed from releases.js under the cards.
const FEATURES_VERSION = "0.9.0";
const FEATURES = [
    {
        icon: "🌐",
        title: "Sayfa çevirisi",
        text: "Başka dildeki bir sayfaya sağ tıkla ve \"Sayfayı Türkçeye çevir\"i seç: Google Çeviri sayfayı yerinde çevirir, girişin ve sayfanın düzeni bozulmaz. Adres çubuğunda hangi dilden çevrildiği yazar; tıklayınca orijinale dönersin.",
    },
    {
        icon: "📜",
        title: "Yenilikler artık doğru sürümü gösteriyor",
        text: "Bu sayfa önceden her güncellemede aynı kartları gösteriyordu. Artık yalnızca senin sürümünden sonra gelenleri gösteriyor; küçük güncellemeler aşağıda liste halinde.",
        action: "Tüm yama notları",
        open: "changelog",
    },
];

const english = window.i18n && window.i18n.lang === "en";
const RELEASES = window.RELEASES || [];

/** 1 when a is newer than b, -1 when older, 0 when equal ("0.8.10" > "0.8.9"). */
function compareVersions(a, b) {
    const pa = String(a).split("+")[0].split(".").map(Number);
    const pb = String(b).split("+")[0].split(".").map(Number);
    for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
        const d = (pa[i] || 0) - (pb[i] || 0);
        if (d) return Math.sign(d);
    }
    return 0;
}

/** The version that was installed before this update (empty when opened by hand). */
const previousVersion = new URLSearchParams(location.search).get("from") || "";

function richText(text) {
    const fragment = document.createDocumentFragment();
    text.split("**").forEach((part, index) => {
        if (!part) return;
        if (index % 2) {
            const strong = document.createElement("strong");
            strong.textContent = part;
            fragment.append(strong);
        } else {
            fragment.append(document.createTextNode(part));
        }
    });
    return fragment;
}

/** Releases after the previous version, up to the running one; without "from", just the newest. */
function releasesSince(current) {
    const upTo = current || (RELEASES[0] && RELEASES[0].version);
    if (!previousVersion) return RELEASES.filter((r) => r.version === upTo);
    return RELEASES.filter((r) => /^\d/.test(r.version)
        && compareVersions(r.version, previousVersion) > 0
        && compareVersions(r.version, upTo) <= 0);
}

function renderAlso(current) {
    // The big release has its cards; its own lines would only repeat them.
    const releases = releasesSince(current).filter((r) => !(showCards(current) && r.version === FEATURES_VERSION));
    const list = $("also-list");
    list.replaceChildren();
    for (const release of releases) {
        for (const item of release.items) {
            const li = document.createElement("li");
            const tag = document.createElement("span");
            tag.className = "tag";
            tag.textContent = release.version;
            li.append(tag, richText(english ? item[1] : item[0]));
            list.append(li);
        }
    }
    $("also").hidden = list.children.length === 0;
}

/** The cards belong to FEATURES_VERSION: shown when this update crossed it (or when opened by hand). */
function showCards(current) {
    if (current && compareVersions(current, FEATURES_VERSION) < 0) return false;
    return !previousVersion || compareVersions(previousVersion, FEATURES_VERSION) < 0;
}

function render(current) {
    const root = $("features");
    root.replaceChildren();
    root.hidden = !showCards(current);
    renderAlso(current);
    if (root.hidden) return;
    FEATURES.forEach((feature, index) => {
        const card = document.createElement("article");
        card.className = "feature";
        card.style.setProperty("--delay", `${0.08 + index * 0.06}s`);

        const icon = document.createElement("div");
        icon.className = "feature-icon";
        icon.textContent = feature.icon;

        const body = document.createElement("div");
        body.className = "feature-body";
        const title = document.createElement("h2");
        title.textContent = t(feature.title);
        const text = document.createElement("p");
        text.textContent = t(feature.text);
        body.append(title, text);

        if (feature.action) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "btn ghost";
            button.textContent = t(feature.action);
            button.addEventListener("click", () => send({ type: "whatsnew:open", target: feature.open }));
            body.append(button);
        }

        card.append(icon, body);
        root.append(card);
    });
}

$("done").addEventListener("click", () => send({ type: "whatsnew:open", target: "close" }));

function hexToRgb(hex) {
    const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex || "");
    return m ? `${parseInt(m[1], 16)}, ${parseInt(m[2], 16)}, ${parseInt(m[3], 16)}` : null;
}

if (host) {
    host.addEventListener("message", (event) => {
        const message = event.data || {};
        if (message.type !== "config") return;
        const theme = message.theme || {};
        const root = document.documentElement.style;
        if (theme.accent) root.setProperty("--accent", theme.accent);
        if (theme.accentSecondary) root.setProperty("--accent2", theme.accentSecondary);
        const overlay = hexToRgb(theme.overlay);
        if (overlay) root.setProperty("--overlay", overlay);
        if (theme.wallpaper) {
            root.setProperty("--wallpaper", `url("${theme.wallpaper}")`);
            $("wall").classList.add("ready");
        }
        document.body.classList.toggle("motion", message.animations !== false);
        if (message.version) {
            const version = String(message.version).split("+")[0];
            if ($("version").textContent !== version) {
                $("version").textContent = version;
                render(version);
            }
        }
    });
}

render(null);
send({ type: "ready" });
