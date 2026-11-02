// Live new tab backgrounds: small WebGL scenes drawn in the theme's colours. They take the place of
// the wallpaper, run at 30 frames a second at a reduced resolution, and pause with the tab
// (requestAnimationFrame does not fire while it is hidden). Without animations one still frame is drawn.
"use strict";

window.Backdrops = (() => {
    const COMMON = `
        precision highp float;
        uniform float uT;
        uniform vec2 uR;
        uniform vec2 uM;
        uniform vec3 uA;
        uniform vec3 uB;
        uniform vec3 uBg;
        float hash(vec2 p) {
            p = fract(p * vec2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return fract(p.x * p.y);
        }
        float noise(vec2 p) {
            vec2 i = floor(p);
            vec2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), f.x),
                       mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), f.x), f.y);
        }
        float fbm(vec2 p) {
            float value = 0.0;
            float amp = 0.5;
            for (int i = 0; i < 4; i++) {
                value += amp * noise(p);
                p = p * 2.03 + vec2(11.7, 3.1);
                amp *= 0.5;
            }
            return value;
        }
    `;

    const SCENES = {
        // Curtains of light hanging from the upper part of the window.
        aurora: { scale: 0.5, source: `
            void main() {
                vec2 uv = gl_FragCoord.xy / uR;
                float t = uT * 0.1;
                vec3 col = uBg;
                for (int i = 0; i < 3; i++) {
                    float f = float(i);
                    float x = uv.x * (1.6 + f * 0.5) + f * 3.1;
                    float h = 0.62 + f * 0.07 + 0.3 * (fbm(vec2(x + t * (0.7 + f * 0.3), t * 0.6 + f)) - 0.5) + 0.04 * (uM.x - 0.5);
                    float d = uv.y - h;
                    float band = exp(-abs(d) * (d > 0.0 ? 10.0 : 3.0));
                    float ripple = 0.65 + 0.35 * noise(vec2(x * 9.0, t * 3.0 + f));
                    col += mix(uA, uB, f / 2.0 + 0.2 * sin(x + t)) * band * ripple * 0.36;
                }
                gl_FragColor = vec4(col, 1.0);
            }` },

        // A silky veil drifting in the dark, bending around the pointer.
        veil: { scale: 0.5, source: `
            void main() {
                vec2 aspect = vec2(uR.x / uR.y, 1.0);
                vec2 p = (gl_FragCoord.xy / uR - 0.5) * aspect * 2.2;
                vec2 m = (uM - 0.5) * aspect * 2.2;
                float t = uT * 0.07;
                p += (p - m) * 0.25 * exp(-dot(p - m, p - m) * 1.5);
                vec2 q = vec2(fbm(p + t), fbm(p + vec2(5.2, 1.3) - t));
                float r = fbm(p + 2.6 * q + vec2(1.7, 9.2) + 0.4 * t);
                float light = smoothstep(0.3, 0.8, r);
                gl_FragColor = vec4(uBg + mix(uA, uB, q.x) * light * light * 0.8, 1.0);
            }` },

        // A slow flight through layers of twinkling stars.
        galaxy: { scale: 0.75, source: `
            vec3 stars(vec2 p, float seed) {
                vec3 col = vec3(0.0);
                vec2 id = floor(p);
                vec2 f = fract(p) - 0.5;
                for (int y = -1; y <= 1; y++) {
                    for (int x = -1; x <= 1; x++) {
                        vec2 o = vec2(float(x), float(y));
                        vec2 cell = id + o + seed;
                        float n = hash(cell);
                        if (n < 0.55) continue;
                        vec2 q = f - o - (vec2(hash(cell + 1.3), hash(cell + 7.1)) - 0.5) * 0.8;
                        float size = mix(0.008, 0.035, hash(cell + 3.7));
                        float star = min(size / max(length(q), 0.001), 1.0);
                        star *= star * smoothstep(0.5, 0.1, length(q));
                        float twinkle = 0.6 + 0.4 * sin(uT * (1.0 + n * 3.0) + n * 40.0);
                        col += mix(vec3(1.0), n > 0.8 ? uA : uB, 0.45) * star * twinkle;
                    }
                }
                return col;
            }
            void main() {
                vec2 p = (gl_FragCoord.xy - 0.5 * uR) / uR.y + (uM - 0.5) * 0.04;
                float a = uT * 0.012;
                p = mat2(cos(a), -sin(a), sin(a), cos(a)) * p;
                vec3 col = uBg + mix(uA, uB, fbm(p * 1.6 + 4.0)) * pow(fbm(p * 2.2 + a), 2.5) * 0.45;
                for (int i = 0; i < 4; i++) {
                    float f = float(i);
                    float depth = fract(f / 4.0 + uT * 0.012);
                    float fade = smoothstep(0.0, 0.3, depth) * smoothstep(1.0, 0.85, depth);
                    col += stars(p * mix(16.0, 2.0, depth) + f * 17.0, f * 3.0) * fade;
                }
                gl_FragColor = vec4(col, 1.0);
            }` },

        // Pixel-art snowflakes falling in layers of depth.
        snow: { scale: 1, source: `
            void main() {
                float cell = 3.0;
                vec2 g = floor(gl_FragCoord.xy / cell) * cell;
                vec2 uv = g / uR;
                vec3 col = uBg + mix(uB, uA, uv.y) * (1.0 - uv.y) * 0.22;
                for (int i = 0; i < 5; i++) {
                    float f = float(i);
                    float scale = mix(70.0, 18.0, f / 4.0);
                    vec2 p = g / uR.y * scale;
                    p.y += uT * (0.5 + f * 0.35);
                    p.x += sin(p.y * 0.35 + f * 2.0) * 0.5 + uT * 0.05 * (uM.x - 0.5 + 0.3);
                    vec2 id = floor(p);
                    if (hash(id + f * 9.0) < 0.965) continue;
                    vec2 q = abs(fract(p) - 0.5);
                    float size = cell / uR.y * scale * (0.35 + f * 0.14);
                    float flake = step(max(q.x, q.y), size);
                    col += mix(vec3(0.92), uB, 0.2) * flake * (0.22 + f * 0.1);
                }
                gl_FragColor = vec4(col, 1.0);
            }` },

        // Satin folds with glossy highlights that turn towards the pointer.
        silk: { scale: 0.5, source: `
            float fold(vec2 p, float t) {
                float f = sin(p.x * 1.6 + t + 1.8 * sin(p.y * 1.1 - t * 0.7));
                return f + 0.55 * sin(p.y * 2.7 + 1.4 * f + t * 0.6);
            }
            void main() {
                vec2 aspect = vec2(uR.x / uR.y, 1.0);
                vec2 p = gl_FragCoord.xy / uR * aspect * 2.4;
                p = mat2(0.88, -0.48, 0.48, 0.88) * p;
                float t = uT * 0.18;
                float f = fold(p, t);
                vec2 toward = normalize(uM - 0.5 + vec2(0.3, 0.4));
                float slope = fold(p + toward * 0.03, t) - f;
                float light = clamp(0.5 + slope * 7.0, 0.0, 1.0);
                vec3 col = mix(uBg, mix(uA, uB, 0.5 + 0.25 * f), 0.22 + 0.34 * smoothstep(-1.5, 1.5, f));
                col += pow(light, 5.0) * mix(uA, vec3(1.0), 0.4) * 0.6;
                gl_FragColor = vec4(col, 1.0);
            }` },

        // A glowing liquid that swirls under the pointer.
        ether: { scale: 0.5, source: `
            void main() {
                vec2 aspect = vec2(uR.x / uR.y, 1.0);
                vec2 p = gl_FragCoord.xy / uR * aspect * 2.2;
                vec2 d = p - uM * aspect * 2.2;
                float near = exp(-dot(d, d) * 1.6);
                float t = uT * 0.22;
                p += vec2(-d.y, d.x) * near * 1.3;
                for (int i = 1; i <= 5; i++) {
                    float f = float(i);
                    p += vec2(sin(p.y * 0.8 * f + t * (0.7 + 0.1 * f)), cos(p.x * 0.75 * f - t * 0.8)) * 0.42 / f;
                }
                float mixer = 0.5 + 0.5 * sin(p.x * 1.4 + p.y * 1.1);
                float glow = smoothstep(0.15, 0.95, 0.5 + 0.5 * cos(p.x - p.y * 0.7));
                gl_FragColor = vec4(uBg + mix(uA, uB, mixer) * glow * glow * 0.55 + uA * near * 0.08, 1.0);
            }` },
    };

    const VERTEX = "attribute vec2 p; void main() { gl_Position = vec4(p, 0.0, 1.0); }";
    const FRAME_MS = 1000 / 30;

    const toVec3 = (hex, fallback) => {
        const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(hex || "") || /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})/i.exec(fallback);
        return [1, 2, 3].map((i) => parseInt(m[i], 16) / 255);
    };

    function compile(gl, type, source) {
        const shader = gl.createShader(type);
        gl.shaderSource(shader, source);
        gl.compileShader(shader);
        return gl.getShaderParameter(shader, gl.COMPILE_STATUS) ? shader : null;
    }

    /** Starts a scene just under the given element; returns its stop function, or null if it cannot run. */
    function start(kind, under, { accent, accent2, overlay, animate }) {
        const scene = SCENES[kind];
        if (!scene) return null;

        const canvas = document.createElement("canvas");
        canvas.className = "backdrop";
        canvas.setAttribute("aria-hidden", "true");
        const gl = canvas.getContext("webgl", { alpha: false, antialias: false, depth: false, stencil: false, powerPreference: "low-power" });
        if (!gl) return null;

        const vertex = compile(gl, gl.VERTEX_SHADER, VERTEX);
        const fragment = compile(gl, gl.FRAGMENT_SHADER, COMMON + scene.source);
        if (!vertex || !fragment) return null;
        const program = gl.createProgram();
        gl.attachShader(program, vertex);
        gl.attachShader(program, fragment);
        gl.bindAttribLocation(program, 0, "p");
        gl.linkProgram(program);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) return null;
        gl.useProgram(program);

        gl.bindBuffer(gl.ARRAY_BUFFER, gl.createBuffer());
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
        gl.enableVertexAttribArray(0);
        gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);

        const uniform = (name) => gl.getUniformLocation(program, name);
        const uT = uniform("uT");
        const uR = uniform("uR");
        const uM = uniform("uM");
        gl.uniform3fv(uniform("uA"), toVec3(accent, "#b583ff"));
        gl.uniform3fv(uniform("uB"), toVec3(accent2, "#dd7fd0"));
        gl.uniform3fv(uniform("uBg"), toVec3(overlay, "#0b0910"));

        const mouse = { x: 0.5, y: 0.5, tx: 0.5, ty: 0.5 };
        let frame = 0;
        let last = 0;
        let stopped = false;

        function draw(seconds) {
            gl.uniform1f(uT, seconds);
            gl.uniform2f(uM, mouse.x, mouse.y);
            gl.drawArrays(gl.TRIANGLES, 0, 3);
        }
        function loop(now) {
            frame = requestAnimationFrame(loop);
            if (now - last < FRAME_MS) return;
            last = now;
            mouse.x += (mouse.tx - mouse.x) * 0.08;
            mouse.y += (mouse.ty - mouse.y) * 0.08;
            draw(now / 1000);
        }
        function resize() {
            canvas.width = Math.max(1, Math.round(window.innerWidth * scene.scale));
            canvas.height = Math.max(1, Math.round(window.innerHeight * scene.scale));
            gl.viewport(0, 0, canvas.width, canvas.height);
            gl.uniform2f(uR, canvas.width, canvas.height);
            if (!animate) draw(40);
        }
        function onPointer(event) {
            mouse.tx = event.clientX / window.innerWidth;
            mouse.ty = 1 - event.clientY / window.innerHeight;
        }
        const onLost = () => canvas.remove();

        canvas.addEventListener("webglcontextlost", onLost);
        window.addEventListener("resize", resize);
        if (animate) window.addEventListener("pointermove", onPointer, { passive: true });
        resize();
        under.before(canvas);
        requestAnimationFrame(() => canvas.classList.add("ready"));
        if (animate) frame = requestAnimationFrame(loop);

        return () => {
            if (stopped) return;
            stopped = true;
            cancelAnimationFrame(frame);
            window.removeEventListener("resize", resize);
            window.removeEventListener("pointermove", onPointer);
            canvas.removeEventListener("webglcontextlost", onLost);
            canvas.remove();
            const lose = gl.getExtension("WEBGL_lose_context");
            if (lose) lose.loseContext();
        };
    }

    return { start };
})();
