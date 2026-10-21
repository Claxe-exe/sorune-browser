// New tab widgets: weather, to-do, notes, countdowns and a focus timer. The app stores the
// list (encrypted); this script renders it and sends the whole list back after each change.
// Uses send(), $(), t() and setClockOptions() from newtab.js / i18n.js.
"use strict";

window.Widgets = (() => {
    const TYPES = {
        weather: { name: "Hava durumu", icon: "⛅", single: true, defaults: { city: "", unit: "c" } },
        todo: { name: "Yapılacaklar", icon: "✅", single: false, defaults: { title: "", items: [] } },
        note: { name: "Not", icon: "📝", single: false, defaults: { title: "", text: "" } },
        countdown: { name: "Geri sayım", icon: "⏳", single: false, defaults: { title: "", date: "" } },
        timer: { name: "Odak zamanlayıcı", icon: "🍅", single: true, defaults: { focus: 25, rest: 5 } },
    };

    const WEATHER = [
        [[0], "☀️", "🌙", "Açık"],
        [[1], "🌤️", "🌙", "Çoğunlukla açık"],
        [[2], "⛅", "☁️", "Parçalı bulutlu"],
        [[3], "☁️", "☁️", "Kapalı"],
        [[45, 48], "🌫️", "🌫️", "Sisli"],
        [[51, 53, 55, 56, 57], "🌦️", "🌧️", "Çisenti"],
        [[61, 63, 65, 66, 67], "🌧️", "🌧️", "Yağmurlu"],
        [[71, 73, 75, 77], "🌨️", "🌨️", "Karlı"],
        [[80, 81, 82], "🌦️", "🌧️", "Sağanak"],
        [[85, 86], "🌨️", "🌨️", "Kar sağanağı"],
        [[95, 96, 99], "⛈️", "⛈️", "Gök gürültülü"],
    ];

    let widgets = [];
    let maxWidgets = 12;
    let editing = false;
    let saveTimer = 0;
    let addMenuOpen = false;
    const weatherByCity = new Map();
    const editingCity = new Set();
    const editingCountdown = new Set();
    const timers = new Map();

    const root = $("widgets");
    const weekday = new Intl.DateTimeFormat(i18n.locale, { weekday: "short" });
    const longDate = new Intl.DateTimeFormat(i18n.locale, { day: "numeric", month: "long", year: "numeric", hour: "2-digit", minute: "2-digit" });

    // ------------------------------------------------------------ helpers

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function button(className, text, title, onClick) {
        const b = el("button", className, text);
        b.type = "button";
        if (title) {
            b.title = title;
            b.setAttribute("aria-label", title);
        }
        b.addEventListener("click", onClick);
        return b;
    }

    function save() {
        clearTimeout(saveTimer);
        saveTimer = setTimeout(() => send({ type: "saveWidgets", widgets }), 400);
    }

    function newId(type) {
        return `${type}-${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
    }

    // ------------------------------------------------------------ public API

    function load(list, max) {
        if (max) maxWidgets = max;
        widgets = Array.isArray(list) ? list : [];
        const clock = widgets.find((w) => w.type === "clock");
        setClockOptions(clock || {});
        for (const w of widgets) {
            if (w.type === "weather" && w.city && !weatherByCity.has(w.city.toLowerCase())) requestWeather(w.city);
        }
        render();
    }

    function updateClock(options) {
        let clock = widgets.find((w) => w.type === "clock");
        if (!clock) {
            clock = { id: "clock", type: "clock" };
            widgets.unshift(clock);
        }
        Object.assign(clock, options);
        save();
    }

    function weather(message) {
        if (!message.city) return;
        weatherByCity.set(message.city.toLowerCase(), Object.assign({ at: Date.now() }, message));
        render();
    }

    function requestWeather(city) {
        send({ type: "weather", city });
    }

    // ------------------------------------------------------------ rendering

    function render() {
        // Keep focus/caret in a text field that survives the re-render (e.g. a note being typed).
        const active = document.activeElement;
        const focusKey = active && active.dataset ? active.dataset.focusKey : null;
        const caret = focusKey && "selectionStart" in active ? [active.selectionStart, active.selectionEnd] : null;

        root.replaceChildren();
        const visible = widgets.filter((w) => TYPES[w.type]);
        visible.forEach((w, index) => root.append(card(w, index, visible.length)));

        if (editing && visible.length < maxWidgets - 1) root.append(addTile());
        root.hidden = root.children.length === 0;

        if (focusKey) {
            const again = root.querySelector(`[data-focus-key="${CSS.escape(focusKey)}"]`);
            if (again) {
                again.focus();
                if (caret && again.setSelectionRange) again.setSelectionRange(caret[0], caret[1]);
            }
        }
    }

    function card(w, index, count) {
        const type = TYPES[w.type];
        const node = el("article", `widget ${w.type}`);
        node.dataset.id = w.id;

        const head = el("header", "widget-head");
        const title = el("span", "widget-title");
        title.append(el("span", "widget-icon", type.icon), document.createTextNode(widgetTitle(w)));
        head.append(title);

        const tools = el("div", "widget-tools");
        if (index > 0) tools.append(button("tool", "‹", t("Sola taşı"), () => move(w, -1)));
        if (index < count - 1) tools.append(button("tool", "›", t("Sağa taşı"), () => move(w, 1)));
        tools.append(button("tool danger", "×", t("Widget'ı kaldır"), () => remove(w)));
        head.append(tools);
        node.append(head);

        const body = el("div", "widget-body");
        switch (w.type) {
            case "weather": weatherBody(w, body); break;
            case "todo": todoBody(w, body); break;
            case "note": noteBody(w, body); break;
            case "countdown": countdownBody(w, body); break;
            case "timer": timerBody(w, body); break;
        }
        node.append(body);
        return node;
    }

    function widgetTitle(w) {
        if (w.type === "weather") {
            const data = w.city && weatherByCity.get(w.city.toLowerCase());
            return data && data.ok ? data.place : t("Hava durumu");
        }
        return (w.title || "").trim() || t(TYPES[w.type].name);
    }

    function addTile() {
        const tile = el("div", "widget add-tile");
        if (!addMenuOpen) {
            tile.append(button("add-button", "+", t("Widget ekle"), () => {
                addMenuOpen = true;
                render();
            }));
            tile.append(el("span", "add-label", t("Widget ekle")));
            return tile;
        }

        tile.classList.add("open");
        const list = el("div", "add-menu");
        for (const [key, type] of Object.entries(TYPES)) {
            if (type.single && widgets.some((w) => w.type === key)) continue;
            const option = button("add-option", "", "", () => add(key));
            option.append(el("span", "widget-icon", type.icon), document.createTextNode(t(type.name)));
            list.append(option);
        }
        list.append(button("add-cancel", t("Vazgeç"), "", () => {
            addMenuOpen = false;
            render();
        }));
        tile.append(list);
        return tile;
    }

    // ------------------------------------------------------------ weather

    function toUnit(celsius, unit) {
        return Math.round(unit === "f" ? celsius * 9 / 5 + 32 : celsius);
    }

    function describe(code, isDay) {
        const row = WEATHER.find(([codes]) => codes.includes(code)) || WEATHER[3];
        return { icon: isDay === false ? row[2] : row[1], text: t(row[3]) };
    }

    function weatherBody(w, body) {
        if (!w.city || editingCity.has(w.id)) {
            const form = el("form", "inline-form");
            const input = el("input");
            input.type = "text";
            input.placeholder = t("Şehir, ör. İstanbul");
            input.value = w.city || "";
            input.maxLength = 80;
            input.dataset.focusKey = `${w.id}-city`;
            form.append(input, el("button", "mini-btn", t("Göster")));
            form.addEventListener("submit", (event) => {
                event.preventDefault();
                const city = input.value.trim();
                if (!city) return;
                w.city = city;
                editingCity.delete(w.id);
                requestWeather(city);
                save();
                render();
            });
            body.append(form, el("p", "widget-hint", t("Yalnızca yazdığın şehir adı Open-Meteo'ya gönderilir.")));
            return;
        }

        const data = weatherByCity.get(w.city.toLowerCase());
        if (!data) {
            body.append(el("p", "widget-hint", t("Yükleniyor…")));
            return;
        }
        if (!data.ok) {
            body.append(el("p", "widget-hint", data.error === "notFound"
                ? t("\"{0}\" bulunamadı.", w.city)
                : t("Hava durumu alınamadı. İnternet bağlantını kontrol et.")));
            body.append(button("link-btn", t("Şehri değiştir"), "", () => {
                editingCity.add(w.id);
                render();
            }));
            return;
        }

        const now = describe(data.code, data.isDay);
        const main = el("div", "weather-main");
        main.append(el("span", "weather-icon", now.icon));
        const temp = el("span", "weather-temp", `${toUnit(data.temp, w.unit)}°`);
        main.append(temp);
        const side = el("div", "weather-side");
        side.append(el("span", "weather-desc", now.text));
        side.append(el("span", "weather-sub", t("Hissedilen {0}°", toUnit(data.feels, w.unit))));
        main.append(side);
        body.append(main);

        body.append(el("p", "weather-meta",
            t("Nem %{0} · Rüzgâr {1} km/s", Math.round(data.humidity), Math.round(data.wind))));

        const days = el("div", "weather-days");
        for (const day of (data.days || []).slice(1, 4)) {
            const d = el("div", "weather-day");
            d.append(el("span", "day-name", weekday.format(new Date(`${day.date}T12:00:00`))));
            d.append(el("span", "day-icon", describe(day.code, true).icon));
            d.append(el("span", "day-temp", `${toUnit(day.max, w.unit)}° / ${toUnit(day.min, w.unit)}°`));
            days.append(d);
        }
        body.append(days);

        const actions = el("div", "widget-actions");
        actions.append(button("link-btn", t("Şehri değiştir"), "", () => {
            editingCity.add(w.id);
            render();
        }));
        actions.append(button("link-btn", w.unit === "f" ? "°C" : "°F", t("Birimi değiştir"), () => {
            w.unit = w.unit === "f" ? "c" : "f";
            save();
            render();
        }));
        body.append(actions);
    }

    // ------------------------------------------------------------ to-do

    function todoBody(w, body) {
        w.items = Array.isArray(w.items) ? w.items : [];
        const list = el("ul", "todo-list");
        w.items.forEach((item, i) => {
            const li = el("li", item.done ? "done" : "");
            const check = button("todo-check", item.done ? "✓" : "", item.done ? t("Yapılmadı say") : t("Yapıldı"), () => {
                item.done = !item.done;
                save();
                render();
            });
            li.append(check, el("span", "todo-text", item.text));
            li.append(button("todo-remove", "×", t("Sil"), () => {
                w.items.splice(i, 1);
                save();
                render();
            }));
            list.append(li);
        });
        body.append(list);

        if (w.items.length < 50) {
            const form = el("form", "inline-form");
            const input = el("input");
            input.type = "text";
            input.placeholder = t("Yeni görev ekle…");
            input.maxLength = 200;
            input.dataset.focusKey = `${w.id}-new`;
            form.append(input);
            form.addEventListener("submit", (event) => {
                event.preventDefault();
                const text = input.value.trim();
                if (!text) return;
                w.items.push({ text, done: false });
                input.value = "";
                save();
                render();
            });
            body.append(form);
        }

        const done = w.items.filter((i) => i.done).length;
        if (w.items.length) {
            const footer = el("div", "widget-actions");
            footer.append(el("span", "widget-hint", t("{0}/{1} tamamlandı", done, w.items.length)));
            if (done) {
                footer.append(button("link-btn", t("Bitenleri temizle"), "", () => {
                    w.items = w.items.filter((i) => !i.done);
                    save();
                    render();
                }));
            }
            body.append(footer);
        }
    }

    // ------------------------------------------------------------ note

    function noteBody(w, body) {
        if (editing) {
            const title = el("input", "title-input");
            title.type = "text";
            title.placeholder = t("Başlık (isteğe bağlı)");
            title.maxLength = 40;
            title.value = w.title || "";
            title.dataset.focusKey = `${w.id}-title`;
            title.addEventListener("input", () => {
                w.title = title.value;
                save();
            });
            body.append(title);
        }
        const area = el("textarea", "note-text");
        area.placeholder = t("Bir şeyler yaz…");
        area.maxLength = 5000;
        area.value = w.text || "";
        area.spellcheck = false;
        area.dataset.focusKey = `${w.id}-text`;
        area.addEventListener("input", () => {
            w.text = area.value;
            save();
        });
        body.append(area);
    }

    // ------------------------------------------------------------ countdown

    function countdownBody(w, body) {
        if (!w.date || editingCountdown.has(w.id)) {
            const form = el("form", "stack-form");
            const title = el("input");
            title.type = "text";
            title.placeholder = t("Ne için? ör. Doğum günü");
            title.maxLength = 60;
            title.value = w.title || "";
            title.dataset.focusKey = `${w.id}-ctitle`;
            const date = el("input");
            date.type = "datetime-local";
            date.value = w.date || "";
            date.required = true;
            form.append(title, date, el("button", "mini-btn", t("Kaydet")));
            form.addEventListener("submit", (event) => {
                event.preventDefault();
                if (!date.value) return;
                w.title = title.value.trim();
                w.date = date.value;
                editingCountdown.delete(w.id);
                save();
                render();
            });
            body.append(form);
            return;
        }

        const big = el("div", "countdown-big");
        big.dataset.countdown = w.id;
        body.append(big);
        body.append(el("p", "widget-hint", longDate.format(new Date(w.date))));
        body.append(button("link-btn", t("Düzenle"), "", () => {
            editingCountdown.add(w.id);
            render();
        }));
        updateCountdown(w, big);
    }

    function updateCountdown(w, node) {
        const diff = new Date(w.date).getTime() - Date.now();
        node.replaceChildren();
        if (Number.isNaN(diff)) return;
        if (diff <= 0) {
            node.append(el("span", "countdown-done", t("🎉 Zamanı geldi!")));
            return;
        }
        const minutes = Math.floor(diff / 60000);
        const days = Math.floor(minutes / 1440);
        const hours = Math.floor((minutes % 1440) / 60);
        const mins = minutes % 60;
        if (days > 0) {
            node.append(el("span", "countdown-number", String(days)), el("span", "countdown-unit", t("gün")));
            node.append(el("span", "countdown-rest", t("{0} sa {1} dk", hours, mins)));
        } else {
            node.append(el("span", "countdown-number", `${hours}:${String(mins).padStart(2, "0")}`), el("span", "countdown-unit", t("saat")));
        }
    }

    // ------------------------------------------------------------ focus timer

    function timerState(w) {
        if (!timers.has(w.id)) timers.set(w.id, { mode: "focus", remaining: (w.focus || 25) * 60, running: false, endAt: 0 });
        return timers.get(w.id);
    }

    function timerBody(w, body) {
        const s = timerState(w);
        const modes = el("div", "timer-modes");
        for (const [mode, label] of [["focus", t("Odak")], ["rest", t("Mola")]]) {
            const b = button(`timer-mode${s.mode === mode ? " active" : ""}`, label, "", () => {
                Object.assign(s, { mode, running: false, remaining: (mode === "focus" ? w.focus : w.rest) * 60 });
                render();
            });
            modes.append(b);
        }
        body.append(modes);

        const time = el("div", "timer-time");
        time.dataset.timer = w.id;
        body.append(time);

        const controls = el("div", "timer-controls");
        controls.append(button("mini-btn primary", s.running ? t("Duraklat") : t("Başlat"), "", () => {
            if (s.running) {
                s.remaining = Math.max(0, Math.round((s.endAt - Date.now()) / 1000));
                s.running = false;
            } else {
                s.endAt = Date.now() + s.remaining * 1000;
                s.running = true;
            }
            render();
        }));
        controls.append(button("mini-btn", "↺", t("Sıfırla"), () => {
            Object.assign(s, { running: false, remaining: (s.mode === "focus" ? w.focus : w.rest) * 60 });
            render();
        }));
        body.append(controls);

        if (editing) {
            const settings = el("div", "timer-settings");
            for (const [key, label, max] of [["focus", t("Odak (dk)"), 120], ["rest", t("Mola (dk)"), 60]]) {
                const field = el("label");
                const input = el("input");
                input.type = "number";
                input.min = "1";
                input.max = String(max);
                input.value = w[key];
                input.addEventListener("change", () => {
                    w[key] = Math.min(max, Math.max(1, parseInt(input.value, 10) || 1));
                    if (!s.running && s.mode === key) s.remaining = w[key] * 60;
                    save();
                    render();
                });
                field.append(el("span", "", label), input);
                settings.append(field);
            }
            body.append(settings);
        }
        updateTimer(w, time);
    }

    function updateTimer(w, node) {
        const s = timerState(w);
        let left = s.remaining;
        if (s.running) {
            left = Math.max(0, Math.round((s.endAt - Date.now()) / 1000));
            if (left === 0) {
                s.running = false;
                s.mode = s.mode === "focus" ? "rest" : "focus";
                s.remaining = (s.mode === "focus" ? w.focus : w.rest) * 60;
                chime();
                render();
                return;
            }
        }
        node.textContent = `${String(Math.floor(left / 60)).padStart(2, "0")}:${String(left % 60).padStart(2, "0")}`;
        node.classList.toggle("running", s.running);
    }

    function chime() {
        try {
            const audio = new AudioContext();
            [0, 0.28, 0.56].forEach((at) => {
                const osc = audio.createOscillator();
                const gain = audio.createGain();
                osc.frequency.value = 880;
                gain.gain.setValueAtTime(0.0001, audio.currentTime + at);
                gain.gain.exponentialRampToValueAtTime(0.2, audio.currentTime + at + 0.02);
                gain.gain.exponentialRampToValueAtTime(0.0001, audio.currentTime + at + 0.22);
                osc.connect(gain).connect(audio.destination);
                osc.start(audio.currentTime + at);
                osc.stop(audio.currentTime + at + 0.25);
            });
            setTimeout(() => audio.close(), 1200);
        } catch {
            // No audio device: the timer still switches.
        }
    }

    // ------------------------------------------------------------ edit mode

    function add(type) {
        addMenuOpen = false;
        widgets.push(Object.assign({ id: newId(type), type }, structuredClone(TYPES[type].defaults)));
        save();
        render();
    }

    function remove(w) {
        widgets = widgets.filter((x) => x !== w);
        timers.delete(w.id);
        save();
        render();
    }

    function move(w, delta) {
        const shown = widgets.filter((x) => TYPES[x.type]);
        const target = shown[shown.indexOf(w) + delta];
        if (!target) return;
        const a = widgets.indexOf(w);
        const b = widgets.indexOf(target);
        [widgets[a], widgets[b]] = [widgets[b], widgets[a]];
        save();
        render();
    }

    function setEditing(value) {
        editing = value;
        addMenuOpen = false;
        document.body.classList.toggle("editing", editing);
        $("customize-text").textContent = editing ? t("Bitti") : t("Özelleştir");
        render();
    }

    $("customize").addEventListener("click", () => setEditing(!editing));

    // Live parts: timers every quarter second, countdowns every 30 seconds.
    setInterval(() => {
        for (const node of root.querySelectorAll("[data-timer]")) {
            const w = widgets.find((x) => x.id === node.dataset.timer);
            if (w && timers.get(w.id)?.running) updateTimer(w, node);
        }
    }, 250);
    setInterval(() => {
        for (const node of root.querySelectorAll("[data-countdown]")) {
            const w = widgets.find((x) => x.id === node.dataset.countdown);
            if (w) updateCountdown(w, node);
        }
    }, 30000);
    // Weather refreshes every 30 minutes (the app caches it for 20).
    setInterval(() => {
        for (const w of widgets) if (w.type === "weather" && w.city) requestWeather(w.city);
    }, 30 * 60 * 1000);

    return { load, updateClock, weather };
})();
