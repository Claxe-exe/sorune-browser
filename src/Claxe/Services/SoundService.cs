using System.IO;
using System.Media;

namespace Claxe.Services;

public enum UiSound
{
    Click,
    TabOpen,
    TabClose,
    Toggle,
}

/// <summary>
/// Short interface sounds, synthesized in memory (no audio files to ship or license).
/// Volume is baked into the samples, so changing it rebuilds the small buffers.
/// SoundPlayer plays one sound at a time, which suits quick UI blips.
/// </summary>
public static class SoundService
{
    private const int SampleRate = 44100;

    private static readonly Dictionary<UiSound, SoundPlayer> Players = [];
    private static double _volume = 0.5;

    public static bool Enabled { get; set; } = true;

    public static double Volume
    {
        get => _volume;
        set
        {
            value = Math.Clamp(value, 0, 1);
            if (Math.Abs(value - _volume) < 0.001)
                return;

            _volume = value;
            foreach (var player in Players.Values)
                player.Dispose();
            Players.Clear();
        }
    }

    public static void Play(UiSound sound)
    {
        if (!Enabled || _volume <= 0)
            return;

        try
        {
            if (!Players.TryGetValue(sound, out var player))
            {
                player = new SoundPlayer(new MemoryStream(Render(sound, _volume)));
                player.Load();
                Players[sound] = player;
            }

            player.Play();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            // No audio device, or it is busy: sounds are decoration, never an error for the user.
            Log.Info($"Arayüz sesi çalınamadı ({sound}): {ex.Message}");
        }
    }

    private static byte[] Render(UiSound sound, double volume)
    {
        var samples = sound switch
        {
            UiSound.Click => Click(),
            UiSound.TabOpen => Sweep(520, 900, 0.11),
            UiSound.TabClose => Sweep(780, 430, 0.12),
            UiSound.Toggle => Concat(Tone(660, 0.045), Tone(990, 0.07)),
            _ => [],
        };

        // Perceived loudness is roughly logarithmic; squaring keeps the low end usable.
        var gain = volume * volume * 0.9;
        return ToWav(samples, gain);
    }

    /// <summary>A soft, high "tick": a fast-decaying sine with a hint of noise for the attack.</summary>
    private static double[] Click()
    {
        var random = new Random(7);
        return Generate(0.03, t =>
            Math.Exp(-t / 0.004) * (0.75 * Math.Sin(2 * Math.PI * 2300 * t) + 0.25 * (random.NextDouble() * 2 - 1)) * 0.5);
    }

    /// <summary>A rounded "pop" gliding between two pitches, with a quiet octave on top.</summary>
    private static double[] Sweep(double from, double to, double seconds)
    {
        var phase = 0.0;
        return Generate(seconds, t =>
        {
            var progress = t / seconds;
            var frequency = from + (to - from) * (1 - Math.Pow(1 - progress, 2));
            phase += 2 * Math.PI * frequency / SampleRate;
            var envelope = Math.Min(1, t / 0.005) * Math.Exp(-t / (seconds * 0.35));
            return envelope * (0.8 * Math.Sin(phase) + 0.2 * Math.Sin(2 * phase)) * 0.55;
        });
    }

    private static double[] Tone(double frequency, double seconds)
        => Generate(seconds, t => Math.Min(1, t / 0.004) * Math.Exp(-t / (seconds * 0.45)) * Math.Sin(2 * Math.PI * frequency * t) * 0.45);

    private static double[] Generate(double seconds, Func<double, double> sample)
    {
        var count = (int)(seconds * SampleRate);
        var result = new double[count];
        for (var i = 0; i < count; i++)
            result[i] = sample((double)i / SampleRate);

        // Short fade-out so the buffer never ends on a click.
        var fade = Math.Min(count, SampleRate / 200);
        for (var i = 0; i < fade; i++)
            result[count - 1 - i] *= (double)i / fade;
        return result;
    }

    private static double[] Concat(params double[][] parts) => parts.SelectMany(p => p).ToArray();

    /// <summary>16-bit mono PCM WAV.</summary>
    private static byte[] ToWav(double[] samples, double gain)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataLength = samples.Length * 2;

        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);          // PCM
        writer.Write((short)1);          // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);    // byte rate
        writer.Write((short)2);          // block align
        writer.Write((short)16);         // bits per sample
        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var s in samples)
            writer.Write((short)Math.Round(Math.Clamp(s * gain, -1, 1) * short.MaxValue));

        writer.Flush();
        return stream.ToArray();
    }
}
