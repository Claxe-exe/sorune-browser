namespace Claxe.ViewModels;

/// <summary>One row of the command palette (Ctrl+K): a command, an open tab, a bookmark or a history entry.</summary>
public sealed record PaletteItem(string Glyph, string Title, string Subtitle, Action Run, string Keywords = "");
