using System.Windows.Threading;
using Claxe.Core;

namespace Claxe.ViewModels;

public enum MascotMood
{
    Neutral,
    Happy,
    Sleepy,
}

/// <summary>
/// Picks the mascot's expression: neutral by default, happy for a moment after a page
/// finishes loading, sleepy after a while without any activity.
/// </summary>
public sealed class MascotViewModel : ObservableObject
{
    private static readonly TimeSpan HappyDuration = TimeSpan.FromSeconds(1.8);
    private static readonly TimeSpan SleepAfter = TimeSpan.FromSeconds(45);

    private readonly DispatcherTimer _happyTimer = new() { Interval = HappyDuration };
    private readonly DispatcherTimer _sleepTimer = new() { Interval = SleepAfter };
    private MascotMood _mood;
    private bool _isEnabled;

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
            if (value)
                _sleepTimer.Start();
        }
    }

    /// <summary>Any user activity wakes the mascot up and restarts the idle countdown.</summary>
    public void NoteActivity()
    {
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
        if (!success)
            return;

        Mood = MascotMood.Happy;
        _happyTimer.Stop();
        _happyTimer.Start();
    }
}
