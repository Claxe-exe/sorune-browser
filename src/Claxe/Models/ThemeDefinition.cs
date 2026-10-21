namespace Claxe.Models;

/// <summary>A theme as stored in Assets/Themes/{id}.json.</summary>
public sealed class ThemeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>File name next to the JSON file.</summary>
    public string? Wallpaper { get; set; }

    public ThemeColors Colors { get; set; } = new();

    /// <summary>How strongly the chrome tint covers the blurred wallpaper (0 = clear glass, 1 = solid).</summary>
    public double ChromeTintOpacity { get; set; } = 0.8;

    public NewTabStyle NewTab { get; set; } = new();
}

public sealed class ThemeColors
{
    public string Base { get; set; } = "#0B0910";
    public string ChromeTint { get; set; } = "#100C17";
    public string Surface { get; set; } = "#1C1626";
    public string SurfaceHover { get; set; } = "#261E33";
    public string SurfacePressed { get; set; } = "#30263F";
    public string Border { get; set; } = "#2A2236";
    public string TextPrimary { get; set; } = "#EEE9F7";
    public string TextSecondary { get; set; } = "#A59CBD";
    public string Accent { get; set; } = "#B583FF";
    public string AccentSecondary { get; set; } = "#DD7FD0";
    public string Danger { get; set; } = "#E5484D";
    public string Warning { get; set; } = "#F2B24C";
}

public sealed class NewTabStyle
{
    /// <summary>Colour of the readability gradient laid over the wallpaper.</summary>
    public string Overlay { get; set; } = "#0B0910";

    /// <summary>"petals", "sparks" or "none".</summary>
    public string Particles { get; set; } = "none";
}
