using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Claxe.Controls;
using Claxe.Interop;
using Claxe.Services;
using Claxe.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace Claxe.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    private TabViewModel? _dragTab;
    private Point _dragStart;
    private bool _isDragging;

    /// <summary>Below this window width the mascot steps aside to leave room for tabs.</summary>
    private const double MascotMinWindowWidth = 760;

    // Tabs that already played their intro, so reordering (which re-creates containers) doesn't replay it.
    private readonly ConditionalWeakTable<TabViewModel, object> _introPlayed = new();

    private bool _isBrowserFullScreen;
    private WindowState _stateBeforeFullScreen;

    // Closing waits for the view model to save session cookies, then closes for real.
    private bool _shutdownStarted;
    private bool _readyToClose;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        viewModel.FocusAddressBarRequested += (_, _) => FocusAddressBar();
        viewModel.FocusContentRequested += (_, _) => FocusActiveWebView();
        viewModel.CloseWindowRequested += (_, _) => Close();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        viewModel.Mascot.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MascotViewModel.IsEnabled))
                UpdateMascotVisibility();
        };
        viewModel.Tabs.CollectionChanged += (_, _) => EnsureActiveTabVisible();
        viewModel.AppearanceChanged += (_, _) => ApplyAppearance();
        viewModel.WallpaperPickRequested += (_, _) => PickWallpaper();
        viewModel.ConfirmOpenRiskyFile = ConfirmOpenRiskyFile;
        viewModel.PageContextMenuRequested += (_, e) => ShowPageContextMenu(e);
        viewModel.GroupEditRequested += (_, group) => OnGroupEditRequested(group);
        viewModel.PaletteRequested += (_, _) => OpenPalette();
        viewModel.VolumeEditRequested += (_, tab) => OpenVolumeEditor(tab, TabElement(tab) ?? AddressPill);
        viewModel.FindRequested += (_, _) => OpenFind();
        viewModel.FullScreenRequested += (_, _) => ToggleBrowserFullScreen();
        viewModel.PickFolder = PickFolder;

        AddressBox.TextChanged += (_, _) =>
        {
            if (AddressBox.IsKeyboardFocusWithin)
                _viewModel.UpdateSuggestions(AddressBox.Text);
        };
        AddressBox.PreviewKeyDown += AddressBox_PreviewKeyDown;
        // Deferred, so a click on a suggestion is handled before the list closes.
        AddressBox.LostKeyboardFocus += (_, _) =>
            Dispatcher.BeginInvoke(() => _viewModel.ClearSuggestions(), DispatcherPriority.Input);
        SiteTintRect.Fill = new LinearGradientBrush(new GradientStopCollection { _tintTop, _tintBottom }, 90);
        TitleTintRect.Fill = new LinearGradientBrush(new GradientStopCollection { _titleTop, _titleBottom }, 90);
        SidebarTintRect.Fill = _sidebarTint;
        ApplyAppearance();
        UpdatePanelColumn();

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewMouseDown += (_, _) => _viewModel.Mascot.NoteActivity();
        StateChanged += (_, _) => UpdateWindowStateVisuals();
        SizeChanged += (_, _) => UpdateMascotVisibility();
        Closing += OnClosing;
        UpdateWindowStateVisuals();
        UpdateMascotVisibility();
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
        _viewModel.Mascot.NoteActivity();
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
            case Key.OemComma when ctrl: _viewModel.OpenSettings(); break;
            case Key.F when ctrl: OpenFind(); break;
            case Key.K when ctrl: OpenPalette(); break;
            case Key.N when ctrlShift: _viewModel.NewPrivateTab(); break;
            case Key.H when ctrl: _viewModel.OpenHistory(); break;
            case Key.J when ctrl: _viewModel.OpenDownloads(); break;
            case Key.D when ctrl: _viewModel.ToggleBookmark(); break;
            case Key.O when ctrlShift: _viewModel.OpenBookmarks(); break;
            case Key.Delete when ctrlShift: _viewModel.OpenSettings("gizlilik"); break;
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

    /// <summary>New tabs fade and rise in once.</summary>
    private void Tab_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TabViewModel tab } element)
            return;

        if (_introPlayed.TryGetValue(tab, out _))
            return;

        _introPlayed.Add(tab, new object());
        if (!Motion.Enabled)
            return;

        // Created here: transforms declared inside a DataTemplate are frozen and can't be animated.
        var offset = new TranslateTransform();
        element.RenderTransform = offset;
        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(6, 0, duration) { EasingFunction = ease });
    }

    private void TabStrip_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (TabStripPanelOrNull() is not { CanScroll: true } panel)
            return;

        panel.ScrollBy(-e.Delta / 120.0 * Math.Max(panel.TabWidth, 60));
        e.Handled = true;
    }

    private void EnsureActiveTabVisible()
    {
        // After layout, so the panel knows the new tab width.
        Dispatcher.BeginInvoke(() =>
        {
            if (_viewModel.ActiveTab is { } tab)
                TabStripPanelOrNull()?.EnsureVisible(_viewModel.Tabs.IndexOf(tab));
        }, DispatcherPriority.Loaded);
    }

    private TabStripPanel? TabStripPanelOrNull() => FindDescendant<TabStripPanel>(TabStrip);

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
        if (TabStripPanelOrNull() is { } panel)
            _viewModel.MoveTab(_dragTab, panel.IndexAt(position.X));
    }

    private void TabStrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => TabStrip.ReleaseMouseCapture();

    // ------------------------------------------------------------- split screen

    private bool _draggingDivider;

    private void SplitButton_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsSplit)
            _viewModel.CloseSplit();
        else
            _viewModel.SplitWithNewTab();
    }

    private void PaneHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            _viewModel.ActiveTab = tab;
            tab.WebView?.Focus();
        }
    }

    private void ContentHost_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSplitDivider();

    /// <summary>The divider sits exactly in the 6px gap the content grid leaves between the panes.</summary>
    private void UpdateSplitDivider()
    {
        var usable = Math.Max(0, ContentHost.ActualWidth - SplitDivider.Width);
        var inset = ContentHost.Margin;
        SplitDivider.Margin = new Thickness(inset.Left + usable * _viewModel.SplitRatio, inset.Top, 0, inset.Bottom);
    }

    private void SplitDivider_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _draggingDivider = SplitDivider.CaptureMouse();
        e.Handled = true;
    }

    private void SplitDivider_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_draggingDivider)
            return;

        var usable = ContentHost.ActualWidth - SplitDivider.Width;
        if (usable > 0)
            _viewModel.SplitRatio = (e.GetPosition(ContentHost).X - SplitDivider.Width / 2) / usable;
    }

    private void SplitDivider_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _draggingDivider = false;
        SplitDivider.ReleaseMouseCapture();
    }

    // ------------------------------------------------------------- per-tab volume

    private Popup? _volumeEditor;

    private void AudioButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab } button)
            OpenVolumeEditor(tab, button);
    }

    private void AudioButton_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel tab })
        {
            tab.ToggleMute();
            e.Handled = true; // not the tab's context menu
        }
    }

    // Right click on the speaker mutes; the tab's menu stays closed.
    private void AudioButton_ContextMenuOpening(object sender, ContextMenuEventArgs e) => e.Handled = true;

    private void TabVolume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: TabViewModel tab } target } })
            OpenVolumeEditor(tab, target);
    }

    private FrameworkElement? TabElement(TabViewModel tab)
        => TabStrip.ItemContainerGenerator.ContainerFromItem(tab) as FrameworkElement;

    /// <summary>A slider for this tab only (0–100 %) plus mute, under the tab.</summary>
    private void OpenVolumeEditor(TabViewModel tab, FrameworkElement anchor)
    {
        if (_volumeEditor is not null)
            _volumeEditor.IsOpen = false;

        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 2,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
        };

        var value = new TextBlock
        {
            Width = 44,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Text = $"%{tab.Volume}",
        };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = tab.Volume,
            SmallChange = 5,
            LargeChange = 10,
            Width = 170,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)FindResource("ClaxeSliderStyle"),
        };
        slider.ValueChanged += (_, e) =>
        {
            tab.Volume = (int)Math.Round(e.NewValue);
            value.Text = $"%{tab.Volume}";
        };

        var mute = new Button
        {
            Style = (Style)FindResource("IconButtonStyle"),
            Width = 30,
            Height = 30,
            FontSize = 13,
            Content = tab.IsMuted ? "" : "",
            ToolTip = tab.IsMuted ? L.T("Sesi aç") : L.T("Sesi kapat"),
        };
        mute.Click += (_, _) =>
        {
            tab.ToggleMute();
            // IsMuted changes asynchronously; show the new state right away.
            var muted = !tab.IsMuted;
            mute.Content = muted ? "" : "";
            mute.ToolTip = muted ? L.T("Sesi aç") : L.T("Sesi kapat");
        };

        var reset = new Button
        {
            Content = L.T("%100"),
            Style = (Style)FindResource("GhostButtonStyle"),
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            FontSize = 11.5,
            Margin = new Thickness(6, 0, 0, 0),
        };
        reset.Click += (_, _) => slider.Value = 100;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(mute);
        row.Children.Add(new Border { Width = 8 });
        row.Children.Add(slider);
        row.Children.Add(value);
        row.Children.Add(reset);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = L.T("Bu sekmenin ses düzeyi"),
            FontSize = 12,
            Margin = new Thickness(2, 0, 0, 8),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });
        panel.Children.Add(row);

        popup.Child = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(12, 10, 12, 12),
            CornerRadius = new CornerRadius(14),
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = (Brush)FindResource("GlassBorderBrush"),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 4, BlurRadius = 20, Opacity = 0.45, Color = Colors.Black },
            Child = panel,
        };
        popup.Opened += (_, _) => slider.Focus();
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_volumeEditor, popup))
                _volumeEditor = null;
        };
        _volumeEditor = popup;
        popup.IsOpen = true;
    }

    // ------------------------------------------------------------- command palette

    private void OpenPalette()
    {
        _viewModel.UpdatePalette("");
        PaletteBox.Text = "";
        // Centred near the top of the page area.
        PalettePopup.HorizontalOffset = Math.Max(0, (ContentGrid.ActualWidth - 652) / 2);
        PalettePopup.VerticalOffset = 24;
        PalettePopup.IsOpen = true;
        Dispatcher.BeginInvoke(() =>
        {
            PaletteBox.Focus();
            Keyboard.Focus(PaletteBox);
        }, DispatcherPriority.Input);
    }

    private void ClosePalette()
    {
        PalettePopup.IsOpen = false;
    }

    private void PalettePopup_Closed(object? sender, EventArgs e) => _viewModel.PaletteItems.Clear();

    private void PaletteBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.UpdatePalette(PaletteBox.Text);
        ScrollPaletteToSelection();
    }

    private void PaletteBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _viewModel.PaletteIndex++;
                break;
            case Key.Up:
                _viewModel.PaletteIndex--;
                break;
            case Key.PageDown:
                _viewModel.PaletteIndex += 8;
                break;
            case Key.PageUp:
                _viewModel.PaletteIndex -= 8;
                break;
            case Key.Enter:
                RunPalette(_viewModel.PaletteIndex);
                break;
            case Key.Escape:
                ClosePalette();
                FocusActiveWebView();
                break;
            default:
                return;
        }

        ScrollPaletteToSelection();
        e.Handled = true;
    }

    private void PaletteRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PaletteItem item })
        {
            RunPalette(_viewModel.PaletteItems.IndexOf(item));
            e.Handled = true;
        }
    }

    private void RunPalette(int index)
    {
        ClosePalette();
        _viewModel.RunPaletteItem(index);
    }

    private void ScrollPaletteToSelection()
    {
        if (PaletteList.SelectedItem is { } item)
            PaletteList.ScrollIntoView(item);
    }

    private string? PickFolder(string? start)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = L.T("İndirme klasörünü seç") };
        if (start is not null && System.IO.Directory.Exists(start))
            dialog.InitialDirectory = start;
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    // ------------------------------------------------------------- tab groups

    private Popup? _groupEditor;

    private void GroupLabel_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel { Group: { } group } })
        {
            _viewModel.ToggleGroupCollapsed(group);
            e.Handled = true;
        }
    }

    private void GroupLabel_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabViewModel { Group: { } group } } label)
        {
            OpenGroupEditor(group, label);
            e.Handled = true;
        }
    }

    /// <summary>"Add to group" lists the other groups each time the submenu opens.</summary>
    private void GroupSubmenu_Opened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, sender) || sender is not MenuItem menu
            || (menu.Parent as ContextMenu)?.PlacementTarget is not FrameworkElement { DataContext: TabViewModel tab })
            return;

        menu.Items.Clear();
        foreach (var group in _viewModel.Groups.Where(g => g != tab.Group))
        {
            var item = new MenuItem
            {
                Header = group.HasName ? group.Name : L.T("Adsız grup"),
                Icon = new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = group.Brush },
            };
            item.Click += (_, _) => _viewModel.AddToGroup(tab, group);
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0)
            menu.Items.Add(new MenuItem { Header = L.T("Başka grup yok"), IsEnabled = false });
    }

    /// <summary>A new group opens its editor right away, under its label (once the strip has laid it out).</summary>
    private void OnGroupEditRequested(TabGroupViewModel group)
        => Dispatcher.BeginInvoke(() =>
        {
            if (_viewModel.Tabs.FirstOrDefault(t => t.Group == group) is { } first
                && TabStrip.ItemContainerGenerator.ContainerFromItem(first) is ContentPresenter container
                && container.ContentTemplate?.FindName("GroupLabel", container) is FrameworkElement label)
            {
                OpenGroupEditor(group, label);
            }
        }, DispatcherPriority.Loaded);

    /// <summary>Name, colour and actions of a group, in a themed bubble under its label.</summary>
    private void OpenGroupEditor(TabGroupViewModel group, FrameworkElement anchor)
    {
        if (_groupEditor is not null)
            _groupEditor.IsOpen = false;

        var popup = new Popup
        {
            PlacementTarget = anchor,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            VerticalOffset = 2,
            AllowsTransparency = true,
            StaysOpen = false,
            PopupAnimation = PopupAnimation.Fade,
        };

        var name = new TextBox
        {
            Text = group.Name,
            Style = (Style)FindResource("AddressTextBoxStyle"),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };
        name.TextChanged += (_, _) =>
        {
            group.Name = name.Text;
            _viewModel.GroupChanged();
        };
        name.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                popup.IsOpen = false;
                e.Handled = true;
            }
        };
        var placeholder = new TextBlock
        {
            Text = L.T("Grup adı"),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
            FontSize = 13,
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = group.HasName ? Visibility.Collapsed : Visibility.Visible,
        };
        name.TextChanged += (_, _) => placeholder.Visibility = name.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var nameBox = new Border
        {
            Height = 34,
            Padding = new Thickness(10, 0, 10, 0),
            CornerRadius = new CornerRadius(10),
            Background = (Brush)FindResource("InsetBrush"),
            BorderBrush = (Brush)FindResource("AccentBrush"),
            BorderThickness = new Thickness(1),
            Child = new Grid { Children = { placeholder, name } },
        };

        // Colour dots; the current one gets a ring.
        var colors = new WrapPanel { Margin = new Thickness(0, 12, 0, 6) };
        var rings = new List<(Border Ring, string Key)>();
        foreach (var (key, label, color) in TabGroupViewModel.Palette)
        {
            var ring = new Border
            {
                Width = 24,
                Height = 24,
                Margin = new Thickness(0, 0, 2, 2),
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = L.T(label),
                Background = Brushes.Transparent,
                Child = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = TabGroupViewModel.BrushFor(key) },
            };
            ring.MouseLeftButtonUp += (_, _) =>
            {
                group.ColorKey = key;
                _viewModel.GroupChanged();
                foreach (var (r, k) in rings)
                    r.BorderBrush = k == group.ColorKey ? (Brush)FindResource("TextPrimaryBrush") : Brushes.Transparent;
            };
            ring.BorderBrush = key == group.ColorKey ? (Brush)FindResource("TextPrimaryBrush") : Brushes.Transparent;
            rings.Add((ring, key));
            colors.Children.Add(ring);
        }

        Button Action(string text, Action act)
        {
            var button = new Button
            {
                Content = text,
                Style = (Style)FindResource("GhostButtonStyle"),
                BorderBrush = Brushes.Transparent,
                Height = 32,
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            button.Click += (_, _) =>
            {
                popup.IsOpen = false;
                act();
            };
            return button;
        }

        var panel = new StackPanel { Width = 236 };
        panel.Children.Add(nameBox);
        panel.Children.Add(colors);
        panel.Children.Add(new Rectangle { Height = 1, Margin = new Thickness(0, 2, 0, 4), Fill = (Brush)FindResource("GlassBorderBrush") });
        panel.Children.Add(Action(L.T("Gruba yeni sekme ekle"), () => _viewModel.NewTabInGroup(group)));
        panel.Children.Add(Action(group.IsCollapsed ? L.T("Grubu genişlet") : L.T("Grubu daralt"), () => _viewModel.ToggleGroupCollapsed(group)));
        panel.Children.Add(Action(L.T("Grubu çöz"), () => _viewModel.Ungroup(group)));
        panel.Children.Add(Action(L.T("Grubu kapat"), () => _viewModel.CloseGroup(group)));

        popup.Child = new Border
        {
            Margin = new Thickness(12),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(14),
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = (Brush)FindResource("GlassBorderBrush"),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 4, BlurRadius = 20, Opacity = 0.45, Color = Colors.Black },
            Child = panel,
        };
        popup.Opened += (_, _) =>
        {
            name.Focus();
            name.SelectAll();
        };
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_groupEditor, popup))
                _groupEditor = null;
        };
        _groupEditor = popup;
        popup.IsOpen = true;
    }

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
        MaximizeButton.ToolTip = L.T(maximized ? "Önceki boyutuna getir" : "Ekranı kapla");

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
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsContentFullScreen):
                SetBrowserFullScreen(_viewModel.IsContentFullScreen);
                break;
            case nameof(MainViewModel.IsActiveTabLoading):
                UpdateLoadingBar(_viewModel.IsActiveTabLoading);
                break;
            case nameof(MainViewModel.ActiveTab):
                EnsureActiveTabVisible();
                if (FindPopup.IsOpen)
                    CloseFind();
                break;
            case nameof(MainViewModel.ActivePanel):
            case nameof(MainViewModel.IsSidebarVisible):
            case nameof(MainViewModel.IsSidebarOnRight):
                UpdatePanelColumn();
                break;
            case nameof(MainViewModel.SplitLeftWidth):
            case nameof(MainViewModel.IsSplitVisible):
                UpdateSplitDivider();
                break;
            case nameof(MainViewModel.ToolbarSiteColor):
                ApplySiteTint();
                break;
            case nameof(MainViewModel.IsPageFramed):
                ApplyPageFrame();
                break;
        }
    }

    // ------------------------------------------------------- toolbar effects

    /// <summary>A highlight sweeps along the toolbar's neon edge while the active page loads.</summary>
    private void UpdateLoadingBar(bool loading)
    {
        if (loading)
        {
            LoadingBar.BeginAnimation(OpacityProperty, null);
            LoadingBar.Opacity = 1;
            if (!Motion.Enabled)
            {
                LoadingBar.Width = ActualWidth;
                LoadingBarOffset.BeginAnimation(TranslateTransform.XProperty, null);
                LoadingBarOffset.X = 0;
                return;
            }

            LoadingBar.Width = 220;
            var sweep = new DoubleAnimation(-220, ActualWidth, TimeSpan.FromSeconds(1.1))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            LoadingBarOffset.BeginAnimation(TranslateTransform.XProperty, sweep);
            return;
        }

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(Motion.Enabled ? 250 : 0));
        fade.Completed += (_, _) =>
        {
            if (!_viewModel.IsActiveTabLoading)
                LoadingBarOffset.BeginAnimation(TranslateTransform.XProperty, null);
        };
        LoadingBar.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Sizes that can't be bound to resources: the caption hit area and the mascot fit.</summary>
    private void ApplyAppearance()
    {
        if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome)
            chrome.CaptionHeight = ThemeService.TitleBarHeight;

        Mascot.FitToTitleBar(ThemeService.TitleBarHeight);
        Mascot.RefreshMotion();
        ApplyPageFrame();
        ApplySiteTint();
    }

    // ------------------------------------------------------- site colour + page frame

    private readonly GradientStop _titleTop = new(Colors.Transparent, 0);
    private readonly GradientStop _titleBottom = new(Colors.Transparent, 1);
    private readonly GradientStop _tintTop = new(Colors.Transparent, 0);
    private readonly GradientStop _tintBottom = new(Colors.Transparent, 1);
    private readonly SolidColorBrush _sidebarTint = new(Colors.Transparent);

    /// <summary>
    /// The chrome around the page takes on the site's colour: the tab strip lightly (the wallpaper
    /// still shows a little), the toolbar growing stronger towards the page, and the sidebar.
    /// Light sites get only a hint of it, so the light interface text stays readable.
    /// </summary>
    private void ApplySiteTint()
    {
        var duration = TimeSpan.FromMilliseconds(Motion.Enabled ? 350 : 0);
        void Animate(Animatable target, DependencyProperty property, Color current, Color? site, byte alpha)
        {
            // Without a site colour, fade out in the current hue instead of through grey.
            var to = site is { } c ? Color.FromArgb(alpha, c.R, c.G, c.B) : Color.FromArgb(0, current.R, current.G, current.B);
            target.BeginAnimation(property, new ColorAnimation(to, duration));
        }

        Color? site = _viewModel.ToolbarSiteColor is { } hex && ColorConverter.ConvertFromString(hex) is Color parsed ? parsed : null;
        var light = site is { } s && (0.2126 * s.R + 0.7152 * s.G + 0.0722 * s.B) / 255 > 0.55;
        //                                                              light  dark
        Animate(_titleTop, GradientStop.ColorProperty, _titleTop.Color, site, (byte)(light ? 15 : 150));
        Animate(_titleBottom, GradientStop.ColorProperty, _titleBottom.Color, site, (byte)(light ? 30 : 190));
        Animate(_tintTop, GradientStop.ColorProperty, _tintTop.Color, site, (byte)(light ? 35 : 195));
        Animate(_tintBottom, GradientStop.ColorProperty, _tintBottom.Color, site, (byte)(light ? 80 : 240));
        Animate(_sidebarTint, SolidColorBrush.ColorProperty, _sidebarTint.Color, site, (byte)(light ? 45 : 225));
    }

    /// <summary>Insets the page as a framed card (wallpaper around it); never in fullscreen.</summary>
    private void ApplyPageFrame()
    {
        const double inset = 10;
        var framed = _viewModel.IsPageFramed && !_isBrowserFullScreen;
        ContentHost.Margin = framed ? new Thickness(inset) : default;
        PageFrame.Margin = new Thickness(inset - 1);
        PageFrame.Visibility = framed ? Visibility.Visible : Visibility.Collapsed;
        UpdateSplitDivider();
    }

    private void PickWallpaper()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = L.T("Duvar kâğıdı seç"),
            Filter = L.T("Görseller") + " (*.jpg;*.jpeg;*.png;*.webp;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.bmp",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(this) == true)
            _viewModel.ImportWallpaper(dialog.FileName);
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_readyToClose)
            return;

        e.Cancel = true;
        if (_shutdownStarted)
            return;

        _shutdownStarted = true;
        Hide(); // feels instant while cookies and history are flushed
        try
        {
            await _viewModel.ShutdownAsync();
        }
        catch (Exception ex)
        {
            // Whatever went wrong while saving, the app must still exit instead of lingering hidden.
            Log.Error("Kapanış sırasında hata", ex);
        }
        finally
        {
            _readyToClose = true;
            Close();
        }
    }

    // ------------------------------------------------------ address suggestions

    private void AddressBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.HasSuggestions || e.Key is not (Key.Down or Key.Up))
            return;

        _viewModel.MoveSuggestion(e.Key == Key.Down ? 1 : -1);
        HighlightSuggestion();
        e.Handled = true;
    }

    private void HighlightSuggestion()
    {
        for (var i = 0; i < _viewModel.Suggestions.Count; i++)
        {
            if (SuggestionList.ItemContainerGenerator.ContainerFromIndex(i) is ContentPresenter container
                && FindDescendant<Border>(container) is { } row)
            {
                row.Background = i == _viewModel.SelectedSuggestionIndex
                    ? (Brush)FindResource("ControlHoverBrush")
                    : Brushes.Transparent;
            }
        }
    }

    private void Suggestion_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SuggestionViewModel suggestion })
        {
            _viewModel.OpenSuggestion(suggestion);
            e.Handled = true;
        }
    }

    // ---------------------------------------------------------- zoom and find

    private void ResetZoom_Click(object sender, RoutedEventArgs e) => _viewModel.ActiveTab?.ResetZoom();

    private TabViewModel? _findTab;

    private void OpenFind()
    {
        if (_viewModel.ActiveTab is not { Core: not null } tab)
            return;

        _findTab = tab;
        tab.PropertyChanged -= OnFindTabChanged;
        tab.PropertyChanged += OnFindTabChanged;
        FindPopup.IsOpen = true;
        FindBox.Focus();
        FindBox.SelectAll();
        UpdateFindCount();
    }

    private void CloseFind()
    {
        if (_findTab is { } tab)
        {
            tab.PropertyChanged -= OnFindTabChanged;
            tab.StopFind();
        }

        _findTab = null;
        FindPopup.IsOpen = false;
        FocusActiveWebView();
    }

    private void OnFindTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TabViewModel.FindMatchCount) or nameof(TabViewModel.FindActiveIndex))
            UpdateFindCount();
    }

    private void UpdateFindCount()
    {
        var tab = _findTab;
        FindCount.Text = tab is null || FindBox.Text.Length == 0
            ? ""
            : tab.FindMatchCount == 0 ? L.T("Bulunamadı") : $"{tab.FindActiveIndex + 1}/{tab.FindMatchCount}";
    }

    private async void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_findTab is { } tab)
            await tab.FindAsync(FindBox.Text);
        UpdateFindCount();
    }

    private void FindBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when Keyboard.Modifiers == ModifierKeys.Shift:
                _findTab?.FindPrevious();
                e.Handled = true;
                break;
            case Key.Enter:
                _findTab?.FindNext();
                e.Handled = true;
                break;
            case Key.Escape:
                CloseFind();
                e.Handled = true;
                break;
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => _findTab?.FindNext();

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => _findTab?.FindPrevious();

    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFind();

    // ------------------------------------------------------- page context menu

    /// <summary>
    /// Shows WebView2's own menu items (already in Turkish) in a Claxe-styled menu. The page
    /// waits on a deferral until an item is picked or the menu closes.
    /// </summary>
    private void ShowPageContextMenu(CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var deferral = e.GetDeferral();
        var completed = false;
        void Complete(int? commandId)
        {
            if (completed)
                return;
            completed = true;
            if (commandId is { } id)
                e.SelectedCommandId = id;
            deferral.Complete();
        }

        e.Handled = true;
        var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        AddMenuItems(menu.Items, e.MenuItems, depth: 0, Complete);
        menu.Closed += (_, _) => Dispatcher.BeginInvoke(() => Complete(null), DispatcherPriority.Background);
        menu.IsOpen = true;
    }

    private static void AddMenuItems(ItemCollection target, IList<CoreWebView2ContextMenuItem> items, int depth, Action<int?> pick)
    {
        foreach (var item in items)
        {
            if (item.Kind == CoreWebView2ContextMenuItemKind.Separator)
            {
                if (target.Count > 0 && target[^1] is not Separator)
                    target.Add(new Separator());
                continue;
            }

            if (item.Kind == CoreWebView2ContextMenuItemKind.Submenu)
            {
                // Nested menus are flattened (indented), so one simple menu style covers everything.
                AddMenuItems(target, item.Children, depth + 1, pick);
                continue;
            }

            var label = item.Label.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&");
            var menuItem = new MenuItem
            {
                Header = new string(' ', depth * 4) + label,
                InputGestureText = item.ShortcutKeyDescription,
                IsEnabled = item.IsEnabled,
                IsCheckable = item.Kind is CoreWebView2ContextMenuItemKind.CheckBox or CoreWebView2ContextMenuItemKind.Radio,
                IsChecked = item.IsChecked,
            };
            var id = item.CommandId;
            menuItem.Click += (_, _) => pick(id);
            target.Add(menuItem);
        }

        if (target.Count > 0 && target[^1] is Separator)
            target.RemoveAt(target.Count - 1);
    }

    /// <summary>A link arrived from another app or launch: show the window above everything once.</summary>
    public void BringToFront()
    {
        if (!IsVisible)
            Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private bool ConfirmOpenRiskyFile(string path)
        => MessageBox.Show(this,
               L.T("\"{0}\" bir program ya da betik ve bilgisayarında çalışacak.\n\nYalnızca güvendiğin bir kaynaktan indirdiysen aç. Açılsın mı?",
                   System.IO.Path.GetFileName(path)),
               "Sorune", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    // ------------------------------------------------------------- web panels

    /// <summary>The panel column follows the open panel (width from settings, 0 when closed).</summary>
    private void UpdatePanelColumn()
    {
        var sidebar = _viewModel.IsSidebarVisible && !_isBrowserFullScreen;
        SidebarHost.Visibility = sidebar ? Visibility.Visible : Visibility.Collapsed;

        // Left:  [sidebar][panel][splitter][content]   Right: [content][splitter][panel][sidebar]
        var right = _viewModel.IsSidebarOnRight;
        int[] order = right ? [3, 2, 1, 0] : [0, 1, 2, 3];
        Grid.SetColumn(SidebarHost, order[0]);
        Grid.SetColumn(PanelHost, order[1]);
        Grid.SetColumn(PanelSplitter, order[2]);
        Grid.SetColumn(ContentHost, order[3]);
        Grid.SetColumn(SplitDivider, order[3]);
        SidebarHost.BorderThickness = right ? new Thickness(1, 0, 0, 0) : new Thickness(0, 0, 1, 0);
        PanelHost.BorderThickness = SidebarHost.BorderThickness;

        var open = sidebar && _viewModel.ActivePanel is not null;
        var columns = ContentGrid.ColumnDefinitions;
        columns[order[0]].Width = GridLength.Auto;
        columns[order[2]].Width = GridLength.Auto;
        columns[order[3]].Width = new GridLength(1, GridUnitType.Star);
        columns[order[3]].MinWidth = 0;
        columns[order[3]].MaxWidth = double.PositiveInfinity;
        columns[order[0]].MinWidth = columns[order[2]].MinWidth = 0;
        columns[order[0]].MaxWidth = columns[order[2]].MaxWidth = double.PositiveInfinity;

        var panelColumn = columns[order[1]];
        panelColumn.Width = open ? new GridLength(_viewModel.PanelWidth) : new GridLength(0);
        panelColumn.MinWidth = open ? 280 : 0;
        panelColumn.MaxWidth = 900;
        PanelSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PanelSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        => _viewModel.SetPanelWidth(PanelHost.ActualWidth);

    private void UpdateMascotVisibility()
    {
        var visible = _viewModel.Mascot.IsEnabled && ActualWidth >= MascotMinWindowWidth && !_isBrowserFullScreen;
        Mascot.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        MascotSlot.Visibility = Mascot.Visibility;
    }

    private void ToggleBrowserFullScreen() => SetBrowserFullScreen(!_isBrowserFullScreen);

    /// <summary>Hides the browser chrome and covers the whole monitor (F11, or a page video going fullscreen).</summary>
    private void SetBrowserFullScreen(bool fullScreen)
    {
        if (fullScreen == _isBrowserFullScreen)
            return;

        _isBrowserFullScreen = fullScreen;
        UpdateMascotVisibility();
        UpdatePanelColumn();
        ApplyPageFrame();
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
            // Back to the resource references (not fixed values), so density changes keep working.
            TitleRow.SetResourceReference(RowDefinition.HeightProperty, "TitleBarRowHeight");
            ToolbarRow.SetResourceReference(RowDefinition.HeightProperty, "ToolbarRowHeight");
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
