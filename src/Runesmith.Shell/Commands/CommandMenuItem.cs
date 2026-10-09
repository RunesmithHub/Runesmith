using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Runesmith.Shell.Commands;

/// <summary>A menu item that shows its key binding the way Runesmith writes it, such as <c>Ctrl+/</c> rather than Avalonia's key names.</summary>
internal sealed class CommandMenuItem : MenuItem
{
    public string? GestureText { get; init; }

    protected override Type StyleKeyOverride => typeof(MenuItem);

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (GestureText is not null && e.NameScope.Find<TextBlock>("PART_InputGestureText") is { } text)
            text.Text = GestureText;
    }
}
