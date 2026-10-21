using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Claxe.Services;
using Claxe.ViewModels;

namespace Claxe.Controls;

/// <summary>
/// The mascot peeking over the toolbar. Each pose is scaled so the head is the same size
/// and positioned so the eyes sit just above <see cref="EyeLine"/>; everything below is
/// hidden behind the toolbar, which is drawn on top of this control.
/// </summary>
public sealed class MascotView : Grid
{
    public static readonly DependencyProperty MoodProperty = DependencyProperty.Register(
        nameof(Mood), typeof(MascotMood), typeof(MascotView),
        new PropertyMetadata(MascotMood.Neutral, (d, _) => ((MascotView)d).ShowMood(animate: true)));

    /// <summary>Source-pixel measurements of each pose: bottom of the eyes, top of the hair (ignoring the ahoge), centre of the head.</summary>
    private static readonly Dictionary<MascotMood, (string File, double EyeBottom, double HairTop, double HeadCenterX)> Poses = new()
    {
        [MascotMood.Neutral] = ("neutral.png", 262, 45, 232),
        [MascotMood.Happy] = ("happy.png", 238, 30, 270),
        [MascotMood.Sleepy] = ("sleepy.png", 300, 55, 262),
    };

    private double _headHeight = 34;

    private readonly Dictionary<MascotMood, Image> _images = [];
    private readonly TranslateTransform _peek = new();
    private readonly TranslateTransform _bob = new();
    private Image? _current;

    public MascotView()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        RenderTransform = new TransformGroup { Children = { _bob, _peek } };
        Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            ShadowDepth = 0,
            BlurRadius = 10,
            Opacity = 0.55,
        };
        SetResourceReference(GlowColorProperty, "AccentColor");

        Loaded += (_, _) => PeekIn();
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue)
                PeekIn();
            else
                StopMotion();
        };
        SizeChanged += (_, _) => LayoutPoses();
    }

    public MascotMood Mood
    {
        get => (MascotMood)GetValue(MoodProperty);
        set => SetValue(MoodProperty, value);
    }

    /// <summary>Y coordinate (in this control) that the eyes should sit just above.</summary>
    public double EyeLine { get; private set; } = 38;

    /// <summary>
    /// Fits the mascot to a title bar height: eyes just above the toolbar edge and the
    /// visible head (hair top to eye bottom) filling most of the bar.
    /// </summary>
    public void FitToTitleBar(double titleBarHeight)
    {
        var eyeLine = titleBarHeight - 6;
        var headHeight = titleBarHeight - 10;
        if (eyeLine == EyeLine && headHeight == _headHeight)
            return;

        EyeLine = eyeLine;
        _headHeight = headHeight;

        // Pose images are sized from the head height, so rebuild them.
        foreach (var image in _images.Values)
            Children.Remove(image);
        _images.Clear();
        _current = null;
        ShowMood(animate: false);
    }

    private static readonly DependencyProperty GlowColorProperty = DependencyProperty.Register(
        "GlowColor", typeof(Color), typeof(MascotView),
        new PropertyMetadata(Colors.Transparent, (d, e) =>
        {
            if (((MascotView)d).Effect is System.Windows.Media.Effects.DropShadowEffect glow)
                glow.Color = (Color)e.NewValue;
        }));

    private Image GetImage(MascotMood mood)
    {
        if (_images.TryGetValue(mood, out var existing))
            return existing;

        var pose = Poses[mood];
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Mascot", pose.File);
        var scale = _headHeight / (pose.EyeBottom - pose.HairTop);

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        var image = new Image
        {
            Source = bitmap,
            Stretch = Stretch.Fill,
            Width = bitmap.PixelWidth * scale,
            Height = bitmap.PixelHeight * scale,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Opacity = 0,
            Tag = scale,
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

        _images[mood] = image;
        Children.Add(image);
        LayoutPose(mood, image);
        return image;
    }

    private void LayoutPoses()
    {
        foreach (var (mood, image) in _images)
            LayoutPose(mood, image);
    }

    private void LayoutPose(MascotMood mood, Image image)
    {
        var pose = Poses[mood];
        var scale = (double)image.Tag;
        image.Margin = new Thickness(
            ActualWidth / 2 - pose.HeadCenterX * scale,
            EyeLine - pose.EyeBottom * scale,
            0, 0);
    }

    private void ShowMood(bool animate)
    {
        var next = GetImage(Mood);
        if (ReferenceEquals(next, _current))
            return;

        var previous = _current;
        _current = next;
        var duration = TimeSpan.FromMilliseconds(170);

        if (animate && Motion.Enabled && previous is not null)
        {
            previous.BeginAnimation(OpacityProperty, new DoubleAnimation(0, duration));
            next.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration));
            if (Mood == MascotMood.Happy)
                Hop();
        }
        else
        {
            if (previous is not null)
            {
                previous.BeginAnimation(OpacityProperty, null);
                previous.Opacity = 0;
            }
            next.BeginAnimation(OpacityProperty, null);
            next.Opacity = 1;
        }

        StartBob();
    }

    /// <summary>Slides up from behind the toolbar.</summary>
    private void PeekIn()
    {
        ShowMood(animate: false);
        if (!Motion.Enabled)
            return;

        _peek.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(520))
        {
            EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut },
        });
        StartBob();
    }

    private void Hop()
    {
        var hop = new DoubleAnimation(0, -4, TimeSpan.FromMilliseconds(140))
        {
            AutoReverse = true,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        _peek.BeginAnimation(TranslateTransform.YProperty, hop);
    }

    /// <summary>Slow "breathing"; slower still when sleepy. Capped at 24 fps to stay cheap.</summary>
    private void StartBob()
    {
        if (!Motion.Enabled || !IsVisible)
        {
            StopMotion();
            return;
        }

        var period = Mood == MascotMood.Sleepy ? 2.6 : 1.7;
        var bob = new DoubleAnimation(0, 1.6, TimeSpan.FromSeconds(period))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Timeline.SetDesiredFrameRate(bob, 24);
        _bob.BeginAnimation(TranslateTransform.YProperty, bob);
    }

    /// <summary>Re-evaluates idle motion after the animation setting changes.</summary>
    public void RefreshMotion()
    {
        if (Motion.Enabled)
            StartBob();
        else
            StopMotion();
    }

    private void StopMotion()
    {
        _bob.BeginAnimation(TranslateTransform.YProperty, null);
        _peek.BeginAnimation(TranslateTransform.YProperty, null);
    }
}
