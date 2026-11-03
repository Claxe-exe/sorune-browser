using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Claxe.Services;
using Claxe.ViewModels;

namespace Claxe.Views;

/// <summary>The panel for the current site's look: every change shows on the page right away.</summary>
public partial class MainWindow
{
    private Popup? _siteStylePanel;

    private void SiteStyleButton_Click(object sender, RoutedEventArgs e) => OpenSiteStylePanel();

    private void OpenSiteStylePanel()
    {
        if (_siteStylePanel is { IsOpen: true })
        {
            _siteStylePanel.IsOpen = false;
            return;
        }

        var tab = _viewModel.ActiveTab;
        if (tab is null || SiteStyleStore.HostOf(tab.Url) is not { } host)
        {
            _viewModel.OpenSettings("site-gorunum");
            return;
        }

        var store = SharedStores.SiteStyles;
        var popup = new Popup
        {
            PlacementTarget = SiteStyleButton,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 6,
            AllowsTransparency = true,
            StaysOpen = true,
            PopupAnimation = PopupAnimation.Fade,
        };

        var text = (Brush)FindResource("TextPrimaryBrush");
        var dim = (Brush)FindResource("TextSecondaryBrush");
        TextBlock Label(string content, double size = 12.5, Brush? brush = null) => new()
        {
            Text = content,
            FontSize = size,
            Foreground = brush ?? dim,
            TextWrapping = TextWrapping.Wrap,
        };
        Button Chip(string content, Action click, bool accent = false)
        {
            var button = new Button
            {
                Content = content,
                Style = (Style)FindResource(accent ? "AccentButtonStyle" : "GhostButtonStyle"),
                Height = 28,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 6),
            };
            button.Click += (_, _) => click();
            return button;
        }

        var root = new StackPanel();
        var body = new StackPanel();
        bool building = false; // the controls being filled in are not changes by the user

        void Change(Action<SiteStyle> change)
        {
            if (!building)
                store.Update(host, change);
        }

        void AddSlider(string name, int min, int max, int fallback, int value, Action<SiteStyle, int> set, string unit)
        {
            var shown = Label($"{value}{unit}", 12.5, text);
            shown.HorizontalAlignment = HorizontalAlignment.Right;
            var head = new Grid { Margin = new Thickness(0, 8, 0, 2) };
            head.Children.Add(Label(name));
            head.Children.Add(shown);
            var slider = new Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                SmallChange = 5,
                LargeChange = 10,
                Style = (Style)FindResource("ClaxeSliderStyle"),
            };
            slider.ValueChanged += (_, e) =>
            {
                var number = (int)Math.Round(e.NewValue);
                shown.Text = $"{number}{unit}";
                Change(s => set(s, number));
            };
            head.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2)
                    slider.Value = fallback;
            };
            body.Children.Add(head);
            body.Children.Add(slider);
        }

        void Build()
        {
            building = true;
            body.Children.Clear();
            var style = store.Find(host) ?? new SiteStyle { Host = host };

            var on = new CheckBox
            {
                Content = L.T("Bu sitede açık"),
                IsChecked = style.Enabled,
                Style = (Style)FindResource("ClaxeCheckBoxStyle"),
                Foreground = text,
            };
            on.Click += (_, _) => Change(s => s.Enabled = on.IsChecked == true);
            body.Children.Add(on);

            var match = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            match.Children.Add(Chip(L.T("Temama uydur"), () => _ = MatchThemeAsync(tab, host, Build), accent: true));
            match.Children.Add(Chip(L.T("Öğe gizle"), () => StartSitePick(tab, host, popup)));
            body.Children.Add(match);

            AddSlider(L.T("Renk tonu"), -180, 180, 0, style.Hue, (s, v) => s.Hue = v, "°");
            AddSlider(L.T("Doygunluk"), 0, 200, 100, style.Saturation, (s, v) => s.Saturation = v, "%");
            AddSlider(L.T("Parlaklık"), 50, 150, 100, style.Brightness, (s, v) => s.Brightness = v, "%");

            var links = new CheckBox
            {
                Content = L.T("Bağlantılar tema renginde"),
                IsChecked = style.ThemeLinks,
                Margin = new Thickness(0, 10, 0, 0),
                Style = (Style)FindResource("ClaxeCheckBoxStyle"),
                Foreground = text,
            };
            links.Click += (_, _) => Change(s => s.ThemeLinks = links.IsChecked == true);
            body.Children.Add(links);

            var fontHead = Label(L.T("Yazı tipi"));
            fontHead.Margin = new Thickness(0, 12, 0, 6);
            body.Children.Add(fontHead);
            var fonts = new WrapPanel();
            foreach (var font in SiteStyleStore.Fonts)
            {
                var chip = Chip(font.Length == 0 ? L.T("Sitenin kendi") : font, () =>
                {
                    Change(s => s.Font = font);
                    Build();
                }, accent: style.Font == font);
                if (font.Length > 0)
                    chip.FontFamily = new FontFamily(font);
                fonts.Children.Add(chip);
            }
            body.Children.Add(fonts);

            if (style.Hidden.Count > 0)
            {
                var hidden = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                var showAll = Chip(L.T("Geri getir"), () =>
                {
                    Change(s => s.Hidden.Clear());
                    Build();
                });
                DockPanel.SetDock(showAll, Dock.Right);
                hidden.Children.Add(showAll);
                var count = Label(L.T("{0} öğe gizli", style.Hidden.Count));
                count.VerticalAlignment = VerticalAlignment.Center;
                hidden.Children.Add(count);
                body.Children.Add(hidden);
            }

            var cssHead = Label(L.T("Özel CSS"));
            cssHead.Margin = new Thickness(0, 8, 0, 6);
            body.Children.Add(cssHead);
            var css = new TextBox
            {
                Text = style.Css,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 84,
                Padding = new Thickness(6),
                MaxLength = SiteStyleStore.MaxCssLength,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 12,
                Foreground = text,
                CaretBrush = text,
                Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)),
                BorderBrush = (Brush)FindResource("GlassBorderBrush"),
                BorderThickness = new Thickness(1),
            };
            css.TextChanged += (_, _) => Change(s => s.Css = css.Text);
            body.Children.Add(css);

            var foot = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            foot.Children.Add(Chip(L.T("Sıfırla"), () =>
            {
                store.Remove(host);
                Build();
            }));
            foot.Children.Add(Chip(L.T("Bütün siteler"), () =>
            {
                popup.IsOpen = false;
                _viewModel.OpenSettings("site-gorunum");
            }));
            body.Children.Add(foot);
            building = false;
        }

        var title = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var close = new Button
        {
            Content = "",
            Style = (Style)FindResource("IconButtonStyle"),
            Width = 26,
            Height = 26,
            FontSize = 11,
            ToolTip = L.T("Kapat"),
        };
        close.Click += (_, _) => popup.IsOpen = false;
        DockPanel.SetDock(close, Dock.Right);
        title.Children.Add(close);
        var heading = new StackPanel();
        heading.Children.Add(Label(L.T("Site görünümü"), 14, text));
        heading.Children.Add(Label(host));
        title.Children.Add(heading);
        root.Children.Add(title);
        root.Children.Add(new ScrollViewer
        {
            Content = body,
            MaxHeight = 470,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });

        popup.Child = new Border
        {
            Margin = new Thickness(12),
            Width = 330,
            Padding = new Thickness(16, 12, 12, 12),
            CornerRadius = new CornerRadius(16),
            Background = (Brush)FindResource("SurfaceBrush"),
            BorderBrush = (Brush)FindResource("GlassBorderBrush"),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { ShadowDepth = 4, BlurRadius = 24, Opacity = 0.5, Color = Colors.Black },
            Child = root,
        };

        // A popup is its own window: it does not follow this one, and belongs to one tab's page.
        void CloseOnChange(object? _, EventArgs __) => popup.IsOpen = false;
        void CloseOnTab(object? _, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(MainViewModel.ActiveTab))
                popup.IsOpen = false;
        }
        LocationChanged += CloseOnChange;
        Deactivated += CloseOnChange;
        _viewModel.PropertyChanged += CloseOnTab;
        popup.Closed += (_, _) =>
        {
            LocationChanged -= CloseOnChange;
            Deactivated -= CloseOnChange;
            _viewModel.PropertyChanged -= CloseOnTab;
            store.Flush();
        };

        Build();
        _siteStylePanel = popup;
        popup.IsOpen = true;
    }

    private async Task MatchThemeAsync(TabViewModel tab, string host, Action rebuild)
    {
        var hue = 0;
        if (tab.Core is { } core)
        {
            try
            {
                var color = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(SiteStyles.DominantColorScript));
                if (color is not null)
                    hue = SiteStyles.HueTurn(color, ThemeService.Accent);
            }
            catch (Exception ex) when (ex is JsonException or System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                Log.Info($"Sitenin rengi okunamadı: {ex.Message}");
            }
        }

        SharedStores.SiteStyles.Update(host, s =>
        {
            s.Hue = hue;
            s.ThemeLinks = true;
        });
        rebuild();
    }

    /// <summary>The panel steps aside while the user points at elements on the page, and comes back after.</summary>
    private void StartSitePick(TabViewModel tab, string host, Popup panel)
    {
        void OnPicked(string selector) => SharedStores.SiteStyles.Hide(host, selector);
        void OnEnded()
        {
            tab.SiteElementPicked -= OnPicked;
            tab.SitePickEnded -= OnEnded;
            if (ReferenceEquals(_viewModel.ActiveTab, tab) && SiteStyleStore.HostOf(tab.Url) == host)
                OpenSiteStylePanel();
        }

        tab.SiteElementPicked += OnPicked;
        tab.SitePickEnded += OnEnded;
        panel.IsOpen = false;
        tab.WebView?.Focus();
        _ = tab.StartSitePickAsync(L.T("Gizlemek için tıkla  ·  Tekerlek: daha büyük alan  ·  Bitirmek için Esc"));
    }
}
