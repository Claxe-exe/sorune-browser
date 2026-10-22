using System.Globalization;

namespace Claxe.Services;

/// <summary>
/// Per-tab volume. WebView2 has no volume API and all tabs share one audio process (so one
/// Windows audio session), which rules out the Windows mixer. Instead every page (and frame)
/// gets a small script before its own scripts run: media elements play at "page volume ×
/// tab volume" (pages still read back their own value), and Web Audio goes through a gain
/// node in front of the real output. Muting stays WebView2's own IsMuted.
/// </summary>
public static class TabAudio
{
    private const string Key = "__soruneTabVolume";

    public static string Script(int volume) => """
        (() => {
          const KEY = "__soruneTabVolume";
          const initial = __VALUE__;
          if (window[KEY]) { window[KEY](initial); return; }
          let factor = initial;
          const clamp = (v) => Math.min(1, Math.max(0, v));

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
            native.set.call(el, clamp(wanted.get(el) * factor));
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
              native.set.call(this, clamp(v * factor));
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

          // Web Audio: each context's "destination" is a gain node in front of the real one.
          const gains = new Set();
          if (window.BaseAudioContext) {
            const destination = Object.getOwnPropertyDescriptor(BaseAudioContext.prototype, "destination");
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
              factor = clamp(Number(value));
              for (const ref of known) {
                const el = ref.deref();
                if (el) native.set.call(el, clamp(wanted.get(el) * factor)); else known.delete(ref);
              }
              document.querySelectorAll("audio, video").forEach(apply);
              for (const ref of gains) {
                const gain = ref.deref();
                if (!gain) { gains.delete(ref); continue; }
                try { gain.gain.setTargetAtTime(factor, gain.context.currentTime, 0.02); } catch (e) { gain.gain.value = factor; }
              }
            },
          });
        })();
        """.Replace("__VALUE__", Factor(volume));

    /// <summary>Changes the volume of an already loaded document (no-op where the script isn't there).</summary>
    public static string SetCall(int volume) => $"window.{Key} && window.{Key}({Factor(volume)});";

    private static string Factor(int volume) => (Math.Clamp(volume, 0, 100) / 100.0).ToString("0.##", CultureInfo.InvariantCulture);
}
