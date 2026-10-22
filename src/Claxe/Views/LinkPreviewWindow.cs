using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Claxe.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Claxe.Views;

/// <summary>
/// Link preview: the page opens in a small window over Sorune without becoming a tab. "Open in
/// tab" keeps it; clicking elsewhere, Esc or the close button throws it away. Links from an
/// InPrivate tab are previewed InPrivate too. The preview never gets permissions.
/// </summary>
public sealed class LinkPreviewWindow : Window
{
    private readonly WebView2 _webView;
    private readonly TextBlock _title;
    private readonly TextBlock _address;
    private readonly Action<string> _openInTab;
    private readonly bool _isPrivate;
    private string _url;

    public LinkPreviewWindow(string url, bool isPrivate, Action<string> openInTab)
    {
        _url = url;
        _isPrivate = isPrivate;
        _openInTab = openInTab;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        Background = Brush("SurfaceBrush", Brushes.Black);
        BorderBrush = Brush("AccentBrush", Brushes.MediumPurple);
        BorderThickness = new Thickness(1);
        Title = L.T("Bağlantı önizlemesi");

        _title = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brush("TextPrimaryBrush", Brushes.White),
            Text = L.T("Yükleniyor…"),
        };
        _address = new TextBlock
        {
            FontSize = 11.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Brush("TextSecondaryBrush", Brushes.Gray),
            Text = url,
        };

        var open = new Button { Content = L.T("Sekmede aç"), Height = 30, Padding = new Thickness(12, 0, 12, 0) };
        open.SetResourceReference(StyleProperty, "AccentButtonStyle");
        open.Click += (_, _) =>
        {
            _openInTab(_url);
            Close();
        };
        var close = new Button { Content = "", Width = 30, Height = 30, FontSize = 11, Margin = new Thickness(6, 0, 0, 0), ToolTip = L.T("Kapat (Esc)") };
        close.SetResourceReference(StyleProperty, "IconButtonStyle");
        close.Click += (_, _) => Close();

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(_title);
        text.Children.Add(_address);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(open);
        buttons.Children.Add(close);

        var header = new DockPanel { Height = 52, Margin = new Thickness(14, 0, 8, 0), Background = Brushes.Transparent };
        DockPanel.SetDock(buttons, Dock.Right);
        header.Children.Add(buttons);
        header.Children.Add(text);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        };

        _webView = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, ThemeService.BaseColor.R, ThemeService.BaseColor.G, ThemeService.BaseColor.B) };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new Border { BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Brush("GlassBorderBrush", Brushes.Gray), Child = _webView });
        Content = root;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
        // Clicking back into Sorune (or anywhere else) dismisses the preview.
        Deactivated += (_, _) =>
        {
            if (IsLoaded)
                Close();
        };
        Loaded += async (_, _) => await StartAsync();
        Closed += (_, _) => _webView.Dispose();
    }

    private Brush Brush(string key, Brush fallback) => Application.Current.TryFindResource(key) as Brush ?? fallback;

    private async Task StartAsync()
    {
        try
        {
            var environment = await BrowserEnvironment.GetAsync();
            if (_isPrivate)
            {
                var options = environment.CreateCoreWebView2ControllerOptions();
                options.IsInPrivateModeEnabled = true;
                options.ProfileName = "inprivate";
                await _webView.EnsureCoreWebView2Async(environment, options);
            }
            else
            {
                await _webView.EnsureCoreWebView2Async(environment);
            }

            var core = _webView.CoreWebView2;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DocumentTitleChanged += (_, _) => _title.Text = string.IsNullOrWhiteSpace(core.DocumentTitle) ? _url : core.DocumentTitle;
            core.SourceChanged += (_, _) =>
            {
                _url = core.Source;
                _address.Text = core.Source;
            };
            // Pop-ups from the preview become real tabs.
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (AddressResolver.IsNavigable(e.Uri))
                    _openInTab(e.Uri);
            };
            core.Navigate(_url);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            Log.Error($"Önizleme açılamadı: {_url}", ex);
            _title.Text = L.T("Önizleme açılamadı");
        }
    }
}
