using System.Windows.Media;
using Claxe.Core;

namespace Claxe.ViewModels;

/// <summary>A named, coloured group of neighbouring tabs that can be collapsed to its label.</summary>
public sealed class TabGroupViewModel : ObservableObject
{
    /// <summary>Colour keys (saved in the session) and their label colours.</summary>
    public static readonly (string Key, string Name, Color Color)[] Palette =
    [
        ("purple", "Mor", Color.FromRgb(0xB5, 0x83, 0xFF)),
        ("pink", "Pembe", Color.FromRgb(0xFF, 0x7B, 0xC8)),
        ("red", "Kırmızı", Color.FromRgb(0xFF, 0x6B, 0x7A)),
        ("orange", "Turuncu", Color.FromRgb(0xFF, 0x9F, 0x5A)),
        ("yellow", "Sarı", Color.FromRgb(0xFF, 0xD0, 0x5A)),
        ("green", "Yeşil", Color.FromRgb(0x5B, 0xD6, 0x8B)),
        ("cyan", "Turkuaz", Color.FromRgb(0x5F, 0xE0, 0xE6)),
        ("blue", "Mavi", Color.FromRgb(0x6A, 0xA6, 0xFF)),
        ("grey", "Gri", Color.FromRgb(0xA4, 0xA0, 0xB0)),
    ];

    private string _name;
    private string _colorKey;
    private bool _isCollapsed;

    public TabGroupViewModel(string? id, string? name, string? colorKey, bool isCollapsed = false)
    {
        Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString("N") : id;
        _name = name ?? "";
        _colorKey = Palette.Any(p => p.Key == colorKey) ? colorKey! : Palette[0].Key;
        _isCollapsed = isCollapsed;
    }

    public string Id { get; }

    public string Name
    {
        get => _name;
        set
        {
            value = value.Length > 40 ? value[..40] : value;
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(HasName));
        }
    }

    public bool HasName => _name.Trim().Length > 0;

    public string ColorKey
    {
        get => _colorKey;
        set
        {
            if (Palette.Any(p => p.Key == value) && SetProperty(ref _colorKey, value))
                OnPropertyChanged(nameof(Brush));
        }
    }

    public Brush Brush => BrushFor(_colorKey);

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => SetProperty(ref _isCollapsed, value);
    }

    public static Brush BrushFor(string key)
    {
        var brush = new SolidColorBrush(Palette.FirstOrDefault(p => p.Key == key, Palette[0]).Color);
        brush.Freeze();
        return brush;
    }
}
