// Click sparks on Sorune's own pages: a short burst of dashes wherever the user clicks, off unless chosen.
"use strict";

window.ClickSparks = (() => {
    const COUNT = 8;
    const LIFE_MS = 420;
    const REACH = 26;
    const DASH = 9;

    let enabled = false;
    let canvas = null;
    let ctx = null;
    let bursts = [];
    let frame = 0;

    function fit() {
        const ratio = window.devicePixelRatio || 1;
        const width = Math.round(window.innerWidth * ratio);
        const height = Math.round(window.innerHeight * ratio);
        if (canvas.width !== width || canvas.height !== height) {
            canvas.width = width;
            canvas.height = height;
        }
        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
    }

    function draw(now) {
        ctx.clearRect(0, 0, window.innerWidth, window.innerHeight);
        bursts = bursts.filter((burst) => now - burst.at < LIFE_MS);
        for (const burst of bursts) {
            const progress = Math.max(0, (now - burst.at) / LIFE_MS);
            const eased = 1 - (1 - progress) ** 3;
            const length = DASH * (1 - eased);
            ctx.strokeStyle = burst.color;
            ctx.lineWidth = 2;
            ctx.lineCap = "round";
            ctx.beginPath();
            for (let i = 0; i < COUNT; i++) {
                const angle = (i / COUNT) * Math.PI * 2;
                const from = REACH * eased;
                ctx.moveTo(burst.x + Math.cos(angle) * from, burst.y + Math.sin(angle) * from);
                ctx.lineTo(burst.x + Math.cos(angle) * (from + length), burst.y + Math.sin(angle) * (from + length));
            }
            ctx.stroke();
        }
        frame = bursts.length > 0 ? requestAnimationFrame(draw) : 0;
    }

    function onPointerDown(event) {
        if (!canvas) {
            canvas = document.createElement("canvas");
            canvas.className = "click-sparks";
            canvas.setAttribute("aria-hidden", "true");
            canvas.style.position = "fixed";
            canvas.style.inset = "0";
            canvas.style.width = "100%";
            canvas.style.height = "100%";
            canvas.style.pointerEvents = "none";
            canvas.style.zIndex = "9999";
            ctx = canvas.getContext("2d");
            document.body.append(canvas);
        }
        fit();
        const color = getComputedStyle(document.documentElement).getPropertyValue("--accent").trim() || "#b583ff";
        bursts.push({ x: event.clientX, y: event.clientY, at: performance.now(), color });
        if (!frame) frame = requestAnimationFrame(draw);
    }

    function set(on) {
        if (on === enabled) return;
        enabled = on;
        if (on) {
            window.addEventListener("pointerdown", onPointerDown, { capture: true, passive: true });
        } else {
            window.removeEventListener("pointerdown", onPointerDown, { capture: true });
            bursts = [];
            if (canvas) canvas.remove();
            canvas = null;
        }
    }

    return { set };
})();
