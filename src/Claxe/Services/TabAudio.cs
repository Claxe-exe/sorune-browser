using System.Globalization;

namespace Claxe.Services;

/// <summary>
/// Per-tab volume. WebView2 has no volume API and all tabs share one audio process (so one
/// Windows audio session), which rules out the Windows mixer. Instead every page (and frame)
/// gets a small script before its own scripts run: media elements play at "page volume ×
/// tab volume" (pages still read back their own value), and Web Audio goes through a gain
/// node in front of the real output. Muting stays WebView2's own IsMuted.
/// Above 100 % a media element cannot get louder by itself, so it is routed through Web Audio.
/// </summary>
public static class TabAudio
{
    /// <summary>The loudest a tab can be set, in percent.</summary>
    public const int MaxVolume = 300;

    private const string Key = "__soruneTabVolume";

    public static string Script(int volume) => """
        (() => {
          const KEY = "__soruneTabVolume";
          const initial = __VALUE__;
          if (window[KEY]) { window[KEY](initial); return; }
          let factor = initial;
          const clamp = (v) => Math.min(1, Math.max(0, v));
          const level = () => Math.min(1, factor);

          // <audio>/<video>: remember the volume the page asked for, play it scaled.
          const proto = HTMLMediaElement.prototype;
          const native = Object.getOwnPropertyDescriptor(proto, "volume");
          const wanted = new WeakMap();
          const tracked = new WeakSet();
          const known = new Set();
          const track = (el) => {
            if (tracked.has(el)) return;
            tracked.add(el);
            known.add(new WeakRef(el));
            if (!wanted.has(el)) wanted.set(el, native.get.call(el));
          };
          const apply = (el) => {
            track(el);
            native.set.call(el, clamp(wanted.get(el) * level()));
            if (factor > 1) route(el);
          };
          Object.defineProperty(proto, "volume", {
            configurable: true,
            enumerable: native.enumerable,
            get() { return wanted.has(this) ? wanted.get(this) : native.get.call(this); },
            set(value) {
              const v = Number(value);
              if (!(v >= 0 && v <= 1)) { native.set.call(this, value); return; }
              wanted.set(this, v);
              track(this);
              native.set.call(this, clamp(v * level()));
            },
          });
          const play = proto.play;
          proto.play = function (...args) {
            try { apply(this); } catch (e) { }
            return play.apply(this, args);
          };
          document.addEventListener("play", (e) => {
            if (e.target instanceof HTMLMediaElement) apply(e.target);
          }, true);
          document.addEventListener("loadedmetadata", (e) => {
            if (factor > 1 && e.target instanceof HTMLMediaElement) route(e.target);
          }, true);

          // Web Audio: each context's "destination" is a gain node in front of the real one.
          const gains = new Set();
          let route = () => { };
          let updateBoost = () => { };
          if (window.BaseAudioContext) {
            const destination = Object.getOwnPropertyDescriptor(BaseAudioContext.prototype, "destination");
            const Context = window.AudioContext;

            // Above 100 %. An element stays routed for good, so only what can still play is taken:
            // a cross-origin file without CORS or protected video would go silent. A context that
            // is not running yet (no click on the page) would silence it too, so it waits for one.
            let boost = null;
            const routed = new WeakSet();
            const waiting = new Set();
            const canRoute = (el) => {
              if (el.mediaKeys) return false;
              if (el.srcObject || el.crossOrigin !== null) return true;
              if (!el.currentSrc) return false;
              try {
                const url = new URL(el.currentSrc);
                return url.protocol === "blob:" || url.origin === location.origin;
              } catch (e) { return false; }
            };
            const connect = (el) => {
              routed.add(el);
              try { boost.context.createMediaElementSource(el).connect(boost.gain); } catch (e) { } // the page routes it itself: its own gain below applies
            };
            const flush = () => {
              if (!boost || boost.context.state !== "running") return;
              for (const ref of waiting) {
                const el = ref.deref();
                if (el && !routed.has(el) && canRoute(el)) connect(el);
              }
              waiting.clear();
            };
            const wake = () => {
              if (boost && boost.context.state !== "running") boost.context.resume().catch(() => { });
            };
            updateBoost = () => {
              if (!boost) return;
              boost.gain.gain.setTargetAtTime(Math.max(1, factor), boost.context.currentTime, 0.02);
              // Keeps peaks from clipping; does nothing at 100 %.
              boost.limiter.threshold.value = factor > 1 ? -3 : 0;
              boost.limiter.ratio.value = factor > 1 ? 12 : 1;
            };
            route = (el) => {
              if (routed.has(el) || !canRoute(el)) return;
              try {
                if (!boost) {
                  const context = new Context();
                  const gain = context.createGain();
                  const limiter = context.createDynamicsCompressor();
                  gain.connect(limiter);
                  limiter.connect(destination.get.call(context));
                  boost = { context, gain, limiter };
                  updateBoost();
                  context.addEventListener("statechange", flush);
                  addEventListener("pointerdown", wake, true);
                  addEventListener("keydown", wake, true);
                }
                if (boost.context.state === "running") connect(el);
                else {
                  waiting.add(new WeakRef(el));
                  wake();
                }
              } catch (e) { }
            };

            const byContext = new WeakMap();
            Object.defineProperty(BaseAudioContext.prototype, "destination", {
              configurable: true,
              enumerable: destination.enumerable,
              get() {
                const real = destination.get.call(this);
                if (window.OfflineAudioContext && this instanceof OfflineAudioContext) return real;
                let gain = byContext.get(this);
                if (!gain) {
                  gain = this.createGain();
                  gain.gain.value = factor;
                  gain.connect(real);
                  Object.defineProperty(gain, "maxChannelCount", { get: () => real.maxChannelCount });
                  byContext.set(this, gain);
                  gains.add(new WeakRef(gain));
                }
                return gain;
              },
            });
          }

          Object.defineProperty(window, KEY, {
            enumerable: false,
            value: (value) => {
              factor = Math.min(__MAX__, Math.max(0, Number(value)));
              for (const ref of known) {
                const el = ref.deref();
                if (el) apply(el); else known.delete(ref);
              }
              document.querySelectorAll("audio, video").forEach(apply);
              updateBoost();
              for (const ref of gains) {
                const gain = ref.deref();
                if (!gain) { gains.delete(ref); continue; }
                try { gain.gain.setTargetAtTime(factor, gain.context.currentTime, 0.02); } catch (e) { gain.gain.value = factor; }
              }
            },
          });
        })();
        """.Replace("__VALUE__", Factor(volume)).Replace("__MAX__", Factor(MaxVolume));

    /// <summary>Changes the volume of an already loaded document (no-op where the script isn't there).</summary>
    public static string SetCall(int volume) => $"window.{Key} && window.{Key}({Factor(volume)});";

    private static string Factor(int volume) => (Math.Clamp(volume, 0, MaxVolume) / 100.0).ToString("0.##", CultureInfo.InvariantCulture);
}
