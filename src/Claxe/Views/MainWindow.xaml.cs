using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Claxe.Controls;
using Claxe.Interop;
using Claxe.ViewModels;

namespace Claxe.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    private TabViewModel? _dragTab;
    private Point _dragStart;
    private bool _isDragging;

    private bool _isBrowserFullScreen;
    private WindowState _stateBeforeFullScreen;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.FocusAddressBarRequested += (_, _) => FocusAddressBar();
        viewModel.FocusContentRequested += (_, _) => FocusActiveWebView();
        viewModel.CloseWindowRequested += (_, _) => Close();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        PreviewKeyDown += OnPreviewKeyDown;
        StateChanged += (_, _) => UpdateWindowStateVisuals();
        Closing += (_, _) => _viewModel.Shutdown();
        UpdateWindowStateVisuals();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var round = NativeMethods.DWMWCP_ROUND;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
        var dark = 1;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    // ---------------------------------------------------------------- keyboard

    /// <summary>
    /// Window-level shortcuts. WebView2 re-raises accelerator keys (Ctrl/Alt/F-keys)
    /// as WPF key events, so these also work while a page has focus.
    /// </summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Keyboard.Modifiers;
        var ctrl = mods == ModifierKeys.Control;
        var ctrlShift = mods == (ModifierKeys.Control | ModifierKeys.Shift);
        var alt = mods == ModifierKeys.Alt;
        var none = mods == ModifierKeys.None;

        var handled = true;
        switch (key)
        {
            case Key.T when ctrl: _viewModel.NewTab(); break;
            case Key.T when ctrlShift: _viewModel.ReopenClosedTab(); break;
            case Key.W when ctrl:
            case Key.F4 when ctrl: _viewModel.CloseTab(_viewModel.ActiveTab); break;
            case Key.Tab when ctrl:
            case Key.PageDown when ctrl: _viewModel.SelectRelative(1); break;
            case Key.Tab when ctrlShift:
            case Key.PageUp when ctrl: _viewModel.SelectRelative(-1); break;
            case >= Key.D1 and <= Key.D9 when ctrl: _viewModel.SelectByNumber(key - Key.D0); break;
            case >= Key.NumPad1 and <= Key.NumPad9 when ctrl: _viewModel.SelectByNumber(key - Key.NumPad0); break;
            case Key.L when ctrl:
            case Key.D when alt:
            case Key.F6 when none: FocusAddressBar(); break;
            case Key.F5 when none:
            case Key.R when ctrl: _viewModel.Reload(); break;
            case Key.Left when alt: Execute(_viewModel.BackCommand); break;
            case Key.Right when alt: Execute(_viewModel.ForwardCommand); break;
            case Key.F11 when none: ToggleBrowserFullScreen(); break;
            default: handled = false; break;
        }

        e.Handled = handled;
    }

    private static void Execute(ICommand command)
    {
        if (command.CanExecute(null))
            command.Execute(null);
    }

    private void FocusAddressBar()
    {
        // Deferred so it wins over WebView2 grabbing focus while a new tab is created.
        Dispatcher.BeginInvoke(() =>
        {
            AddressBox.Focus();
            AddressBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void FocusActiveWebView() => _viewModel.ActiveTab?.WebView?.Focus();

    private void AddressBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => AddressBox.SelectAll();

    /// <summary>First click selects the whole address (like other browsers); later clicks place the caret.</summary>
    private void AddressBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (AddressBox.IsKeyboardFocusWithin)
            return;

        e.Handled = true;
        AddressBox.Focus();
    }

    // ------------------------------------------------------------- tab strip

    private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabViewModel tab })
            return;

        // Let the close button handle its own click.
        if (FindAncestor<Button>(e.OriginalSource as DependencyObject, stopAt: (DependencyObject)sender) is not null)
            return;

        _viewModel.ActiveTab = tab;
        _dragTab = tab;
        _dragStart = e.GetPosition(TabStrip);
        _isDragging = false;
        TabStrip.CaptureMouse();
        e.Handled = true;
    }

    private void Tab_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            _viewModel.CloseTab(tab);
            e.Handled = true;
        }
    }

    private void TabStrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var position = e.GetPosition(TabStrip);
        if (!_isDragging && Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance)
            return;

        _isDragging = true;
        var panel = FindDescendant<TabStripPanel>(TabStrip);
        if (panel is null || panel.TabWidth <= 0)
            return;

        _viewModel.MoveTab(_dragTab, (int)(position.X / panel.TabWidth));
    }

    private void TabStrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => TabStrip.ReleaseMouseCapture();

    private void TabStrip_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _dragTab = null;
        _isDragging = false;
    }

    // ------------------------------------------------------- window chrome

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    // Mouse clicks on this button arrive as WM_NCLBUTTONUP (see WndProc); Click covers keyboard/automation.
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void UpdateWindowStateVisuals()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "" : "";
        MaximizeButton.ToolTip = maximized ? "Önceki boyutuna getir" : "Ekranı kapla";

        // A maximized WindowChrome window hangs over the screen edge by its resize frame.
        RootBorder.Margin = maximized && !_isBrowserFullScreen ? GetMaximizedOverhang() : default;
    }

    private Thickness GetMaximizedOverhang()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == 0)
            return default;

        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        var pixels = NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXSIZEFRAME, dpi)
                     + NativeMethods.GetSystemMetricsForDpi(NativeMethods.SM_CXPADDEDBORDER, dpi);
        var dips = pixels * 96.0 / dpi;
        return new Thickness(dips);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        switch (msg)
        {
            case NativeMethods.WM_NCHITTEST:
                if (IsOverMaximizeButton(lParam))
                {
                    MaximizeButton.Tag = "hover";
                    handled = true;
                    return NativeMethods.HTMAXBUTTON;
                }
                MaximizeButton.Tag = null;
                break;

            case NativeMethods.WM_NCLBUTTONDOWN when wParam == NativeMethods.HTMAXBUTTON:
                handled = true;
                break;

            case NativeMethods.WM_NCLBUTTONUP when wParam == NativeMethods.HTMAXBUTTON:
                handled = true;
                ToggleMaximize();
                break;

            case NativeMethods.WM_NCMOUSELEAVE:
            case NativeMethods.WM_NCMOUSEMOVE when wParam != NativeMethods.HTMAXBUTTON:
                MaximizeButton.Tag = null;
                break;
        }

        return 0;
    }

    private bool IsOverMaximizeButton(nint lParam)
    {
        if (!MaximizeButton.IsVisible)
            return false;

        var screen = new Point(NativeMethods.GetXLParam(lParam), NativeMethods.GetYLParam(lParam));
        var local = MaximizeButton.PointFromScreen(screen);
        return local.X >= 0 && local.Y >= 0 && local.X < MaximizeButton.ActualWidth && local.Y < MaximizeButton.ActualHeight;
    }

    // ------------------------------------------------------------ fullscreen

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsContentFullScreen))
            SetBrowserFullScreen(_viewModel.IsContentFullScreen);
    }

    private void ToggleBrowserFullScreen() => SetBrowserFullScreen(!_isBrowserFullScreen);

    /// <summary>Hides the browser chrome and covers the whole monitor (F11, or a page video going fullscreen).</summary>
    private void SetBrowserFullScreen(bool fullScreen)
    {
        if (fullScreen == _isBrowserFullScreen)
            return;

        _isBrowserFullScreen = fullScreen;
        if (fullScreen)
        {
            _stateBeforeFullScreen = WindowState;
            TitleRow.Height = new GridLength(0);
            ToolbarRow.Height = new GridLength(0);
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            // Going through Normal makes Windows recompute bounds for the borderless style.
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else
        {
            TitleRow.Height = new GridLength(40);
            ToolbarRow.Height = new GridLength(46);
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBeforeFullScreen;
        }

        UpdateWindowStateVisuals();
    }

    // --------------------------------------------------------------- helpers

    private static T? FindAncestor<T>(DependencyObject? start, DependencyObject stopAt) where T : DependencyObject
    {
        for (var current = start; current is not null && current != stopAt; current = GetParent(current))
        {
            if (current is T match)
                return match;
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject child)
        => child is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(child)
            : LogicalTreeHelper.GetParent(child);

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
                return match;
            if (FindDescendant<T>(child) is { } nested)
                return nested;
        }

        return null;
    }
}
