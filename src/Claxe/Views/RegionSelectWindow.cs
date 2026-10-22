using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Claxe.Services;

namespace Claxe.Views;

/// <summary>
/// Region screenshot: shows a frozen capture of the page exactly over it, dimmed; the user
/// drags a rectangle (Enter or releasing the mouse confirms, Esc cancels).
/// </summary>
public sealed class RegionSelectWindow : Window
{
    private readonly BitmapSource _capture;
    private readonly Canvas _canvas = new() { Background = Brushes.Transparent, Cursor = Cursors.Cross };
    private readonly Rectangle _selection;
    private readonly Path _dim;
    private readonly TextBlock _hint;
    private Point? _start;
    private Rect _rect;

    /// <summary>The selected part of the capture (device pixels), or null when cancelled.</summary>
    public BitmapSource? Result { get; private set; }

    public RegionSelectWindow(BitmapSource capture, Rect screenBounds)
    {
        _capture = capture;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Left = screenBounds.Left;
        Top = screenBounds.Top;
        Width = screenBounds.Width;
        Height = screenBounds.Height;

        var accent = TryFindBrush("AccentBrush") ?? Brushes.MediumPurple;
        _selection = new Rectangle { Stroke = accent, StrokeThickness = 2, StrokeDashArray = [4, 3], Visibility = Visibility.Collapsed };
        _dim = new Path { Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), IsHitTestVisible = false };
        _hint = new TextBlock
        {
            Text = L.T("Kaydetmek istediğin alanı sürükle · Esc: vazgeç"),
            Foreground = Brushes.White,
            FontSize = 13,
            Padding = new Thickness(12, 6, 12, 6),
            Background = new SolidColorBrush(Color.FromArgb(200, 20, 16, 30)),
            IsHitTestVisible = false,
        };

        var root = new Grid();
        root.Children.Add(new Image { Source = capture, Stretch = Stretch.Fill });
        root.Children.Add(_dim);
        root.Children.Add(_canvas);
        _canvas.Children.Add(_selection);
        root.Children.Add(new Border
        {
            Child = _hint,
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 14, 0, 0),
            IsHitTestVisible = false,
        });
        Content = root;

        Loaded += (_, _) =>
        {
            UpdateDim();
            Activate();
            Focus();
        };
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            _start = e.GetPosition(_canvas);
            _canvas.CaptureMouse();
            _selection.Visibility = Visibility.Visible;
            Update(e.GetPosition(_canvas));
        };
        _canvas.MouseMove += (_, e) =>
        {
            if (_start is not null)
                Update(e.GetPosition(_canvas));
        };
        _canvas.MouseLeftButtonUp += (_, _) =>
        {
            _canvas.ReleaseMouseCapture();
            if (_rect.Width >= 4 && _rect.Height >= 4)
                Confirm();
            else
                _start = null;
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
            else if (e.Key == Key.Enter && _rect.Width >= 4)
                Confirm();
        };
        Deactivated += (_, _) =>
        {
            if (IsVisible && Result is null)
                Close();
        };
    }

    private Brush? TryFindBrush(string key) => Application.Current.TryFindResource(key) as Brush;

    private void Update(Point current)
    {
        var start = _start!.Value;
        _rect = new Rect(start, current);
        Canvas.SetLeft(_selection, _rect.X);
        Canvas.SetTop(_selection, _rect.Y);
        _selection.Width = _rect.Width;
        _selection.Height = _rect.Height;
        UpdateDim();
    }

    /// <summary>Everything outside the selection is darkened.</summary>
    private void UpdateDim()
    {
        var all = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        _dim.Data = _rect.IsEmpty || _rect.Width < 1
            ? all
            : new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(_rect));
    }

    private void Confirm()
    {
        // The capture is in device pixels; the window measures in DIPs.
        var scaleX = _capture.PixelWidth / ActualWidth;
        var scaleY = _capture.PixelHeight / ActualHeight;
        var x = (int)Math.Clamp(Math.Round(_rect.X * scaleX), 0, _capture.PixelWidth - 1);
        var y = (int)Math.Clamp(Math.Round(_rect.Y * scaleY), 0, _capture.PixelHeight - 1);
        var width = (int)Math.Clamp(Math.Round(_rect.Width * scaleX), 1, _capture.PixelWidth - x);
        var height = (int)Math.Clamp(Math.Round(_rect.Height * scaleY), 1, _capture.PixelHeight - y);

        var cropped = new CroppedBitmap(_capture, new Int32Rect(x, y, width, height));
        cropped.Freeze();
        Result = cropped;
        Close();
    }
}
