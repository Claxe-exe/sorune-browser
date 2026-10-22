using System.Windows.Threading;
using Claxe.Core;
using Claxe.Services;

namespace Claxe.ViewModels;

public enum MascotMood
{
    Neutral,
    Happy,
    Sleepy,
}

/// <summary>
/// Picks the mascot's expression (neutral; happy for a moment after something good; sleepy
/// after a while without activity) and what it says in its speech bubble: reactions the app
/// asks for, plus gentle reminders (a break after a long stretch, bedtime late at night).
/// </summary>
public sealed class MascotViewModel : ObservableObject
{
    private static readonly TimeSpan HappyDuration = TimeSpan.FromSeconds(1.8);
    private static readonly TimeSpan SleepAfter = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan BubbleDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan BreakAfter = TimeSpan.FromMinutes(90);
    private static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(15);

    private readonly DispatcherTimer _happyTimer = new() { Interval = HappyDuration };
    private readonly DispatcherTimer _sleepTimer = new() { Interval = SleepAfter };
    private readonly DispatcherTimer _bubbleTimer = new() { Interval = BubbleDuration };
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private MascotMood _mood;
    private bool _isEnabled;
    private string? _bubble;
    private string? _customImage;
    private double _peek = 0.5;
    private DateTime _activeSince = DateTime.Now;
    private DateTime _lastActivity = DateTime.Now;
    private DateTime _lastBreakReminder = DateTime.MinValue;
    private bool _saidGoodNight;

    public MascotViewModel(bool isEnabled)
    {
        _isEnabled = isEnabled;
        _happyTimer.Tick += (_, _) =>
        {
            _happyTimer.Stop();
            Mood = MascotMood.Neutral;
        };
        _sleepTimer.Tick += (_, _) =>
        {
            _sleepTimer.Stop();
            Mood = MascotMood.Sleepy;
        };
        _bubbleTimer.Tick += (_, _) =>
        {
            _bubbleTimer.Stop();
            Bubble = null;
        };
        _reminderTimer.Tick += (_, _) => CheckReminders();
        _reminderTimer.Start();

        if (isEnabled)
            _sleepTimer.Start();
    }

    public MascotMood Mood
    {
        get => _mood;
        private set => SetProperty(ref _mood, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetProperty(ref _isEnabled, value))
                return;

            _happyTimer.Stop();
            _sleepTimer.Stop();
            Mood = MascotMood.Neutral;
            Bubble = null;
            if (value)
                _sleepTimer.Start();
        }
    }

    /// <summary>Speech bubbles and reminders on/off (the mascot itself can stay).</summary>
    public bool TalksEnabled { get; set; } = true;

    /// <summary>Text in the speech bubble; null when it is hidden.</summary>
    public string? Bubble
    {
        get => _bubble;
        private set => SetProperty(ref _bubble, value);
    }

    /// <summary>The user's own mascot image (one picture for every mood); null = the built-in one.</summary>
    public string? CustomImage
    {
        get => _customImage;
        set => SetProperty(ref _customImage, value);
    }

    /// <summary>How much of a custom mascot shows above the toolbar (0.2..0.9 of its height).</summary>
    public double Peek
    {
        get => _peek;
        set => SetProperty(ref _peek, Math.Clamp(value, 0.2, 0.9));
    }

    /// <summary>Shows a short message in the speech bubble; <paramref name="happy"/> also cheers.</summary>
    public void Say(string text, bool happy = false)
    {
        if (!_isEnabled)
            return;

        NoteActivity();
        if (happy)
            Cheer();
        if (!TalksEnabled)
            return;

        Bubble = text;
        _bubbleTimer.Stop();
        _bubbleTimer.Start();
    }

    /// <summary>Any user activity wakes the mascot up and restarts the idle countdown.</summary>
    public void NoteActivity()
    {
        var now = DateTime.Now;
        if (now - _lastActivity > SessionGap)
            _activeSince = now; // back after a real pause: the break timer starts over
        _lastActivity = now;

        if (!_isEnabled)
            return;

        if (Mood == MascotMood.Sleepy)
            Mood = MascotMood.Neutral;

        _sleepTimer.Stop();
        _sleepTimer.Start();
    }

    public void OnPageLoaded(bool success)
    {
        if (!_isEnabled)
            return;

        NoteActivity();
        if (success)
            Cheer();
    }

    /// <summary>A greeting for the time of day, once at start.</summary>
    public void Greet(bool recovered)
    {
        var hour = DateTime.Now.Hour;
        var text = recovered
            ? L.T("Kaldığın yerden devam edelim!")
            : hour is >= 5 and < 12 ? L.T("Günaydın! ☀️")
            : hour is >= 12 and < 18 ? L.T("Tekrar hoş geldin!")
            : hour is >= 18 and < 23 ? L.T("İyi akşamlar! 🌆")
            : L.T("Gece kuşu musun? 🌙");
        Say(text);
    }

    private void Cheer()
    {
        Mood = MascotMood.Happy;
        _happyTimer.Stop();
        _happyTimer.Start();
    }

    private void CheckReminders()
    {
        var now = DateTime.Now;
        // Only while the user is actually here.
        if (!_isEnabled || !TalksEnabled || now - _lastActivity > TimeSpan.FromMinutes(2))
            return;

        if (now - _activeSince > BreakAfter && now - _lastBreakReminder > BreakAfter)
        {
            _lastBreakReminder = now;
            Say(L.T("Uzun zamandır buradasın, biraz mola ver ☕"));
        }
        else if (now.Hour is >= 1 and < 5 && !_saidGoodNight && now - _activeSince > TimeSpan.FromMinutes(10))
        {
            _saidGoodNight = true;
            Say(L.T("Saat geç oldu, uyumak lazım 😴"));
        }
    }
}
