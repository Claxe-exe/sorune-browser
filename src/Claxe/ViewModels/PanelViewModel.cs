using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Claxe.Core;
using Claxe.Services;

namespace Claxe.ViewModels;

/// <summary>
/// A sidebar web panel. Its page is a regular <see cref="TabViewModel"/> that never appears
/// in the tab strip; it is created on first open and kept alive while closed (so chats and
/// music keep running) until the user unloads it.
/// </summary>
public sealed class PanelViewModel : ObservableObject, IDisposable
{
    private TabViewModel? _tab;
    private ImageSource? _icon;
    private bool _isOpen;

    public PanelViewModel(WebPanel panel)
    {
        Id = panel.Id;
        Name = panel.Name;
        Url = panel.Url;
        Letter = Name.Length > 0 ? Name[..1].ToUpper(new System.Globalization.CultureInfo("tr-TR")) : "?";
        _icon = LoadCachedIcon();
    }

    public string Id { get; }
    public string Name { get; }
    public string Url { get; }

    /// <summary>Shown until the site's own icon is known.</summary>
    public string Letter { get; }

    public ImageSource? Icon
    {
        get => _icon;
        private set => SetProperty(ref _icon, value);
    }

    public TabViewModel? Tab
    {
        get => _tab;
        private set
        {
            if (SetProperty(ref _tab, value))
                OnPropertyChanged(nameof(IsLoaded));
        }
    }

    public bool IsLoaded => _tab is not null;

    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (!SetProperty(ref _isOpen, value))
                return;

            if (_tab is not null)
            {
                _tab.IsActive = value;
                if (value)
                    _tab.EnsureStarted(); // rebuilds the page if the browser crashed meanwhile
            }
        }
    }

    private string IconPath => Path.Combine(PanelStore.IconFolder, Id + ".png");

    /// <summary>Creates the page on first use; <paramref name="create"/> wires it into the app.</summary>
    public TabViewModel EnsureTab(Func<string, TabViewModel> create)
    {
        if (_tab is not null)
            return _tab;

        var tab = create(Url);
        tab.PropertyChanged += OnTabPropertyChanged;
        tab.IsActive = _isOpen;
        tab.EnsureStarted();
        Tab = tab;
        return tab;
    }

    /// <summary>Frees the page (chats/music stop); it is recreated the next time the panel opens.</summary>
    public TabViewModel? Unload()
    {
        var tab = _tab;
        if (tab is null)
            return null;

        tab.PropertyChanged -= OnTabPropertyChanged;
        Tab = null;
        IsOpen = false;
        return tab;
    }

    public void Dispose() => Unload()?.Dispose();

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabViewModel.Favicon) && _tab?.Favicon is BitmapSource favicon)
        {
            Icon = favicon;
            SaveIcon(favicon);
        }
    }

    private ImageSource? LoadCachedIcon()
    {
        if (!File.Exists(IconPath))
            return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(IconPath);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or COMException)
        {
            Log.Info($"Panel simgesi okunamadı ({Id}): {ex.Message}");
            return null;
        }
    }

    private void SaveIcon(BitmapSource icon)
    {
        try
        {
            Directory.CreateDirectory(PanelStore.IconFolder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(icon));
            using var file = File.Create(IconPath);
            encoder.Save(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Log.Info($"Panel simgesi kaydedilemedi ({Id}): {ex.Message}");
        }
    }
}
