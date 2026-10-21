using System.Windows.Markup;
using Claxe.Services;

namespace Claxe.Controls;

/// <summary>XAML: <c>ToolTip="{controls:T 'Yeni sekme'}"</c> shows the text in the interface language.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension(string text) : MarkupExtension
{
    public string Text { get; } = text;

    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}
