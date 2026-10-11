using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Localization;

namespace Runesmith.Shell.Hub;

/// <summary>What a suggestion line says, each text in the language that shows when it is read.</summary>
/// <param name="Close">The tooltip of the button that closes the line.</param>
internal sealed record SuggestionText(Func<string> Message, Func<string> Install, Func<string> Decline, Func<string> Close);

/// <summary>The line over an editor or the editor area that suggests installing a plugin from the hub.</summary>
internal static class PluginSuggestionBar
{
    /// <param name="close">Removes the line.</param>
    public static Control Create(SuggestedPlugin plugin, PluginSuggestions suggestions, Action close) =>
        Create(new SuggestionText(() => plugin.Message, () => "Install", () => "Don't suggest again", () => "Close"), () => suggestions.Install(plugin),
            () => suggestions.StopSuggesting(plugin), close);

    /// <summary>Creates a line with Install, which closes it and runs <paramref name="install"/>, a button that closes it and runs
    /// <paramref name="decline"/>, and a button that only closes it.</summary>
    /// <param name="icon">The name of the line's icon.</param>
    public static Control Create(SuggestionText text, Action install, Action decline, Action close, string icon = "Puzzle")
    {
        var installButton = PluginManagerView.SmallButton(text.Install, () =>
        {
            close();
            install();
        }, accent: true);
        var declineButton = PluginManagerView.SmallButton(text.Decline, () =>
        {
            close();
            decline();
        });
        var dismiss = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = Icons.X, Size = 14 } }.Localize(ToolTip.TipProperty, text.Close);
        dismiss.Click += (_, _) => close();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { installButton, declineButton, dismiss } };
        var glyph = HubVisuals.Symbol(icon, 15, "AccentBrush");
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var message = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 12, 0) }
            .Localize(TextBlock.TextProperty, text.Message);
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(actions, Dock.Right);
        var line = new DockPanel { Margin = new Thickness(12, 5, 8, 5), Children = { glyph, actions, message } };
        var bar = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = line, Name = "PluginSuggestion" };
        HubVisuals.Themed(bar, Border.BackgroundProperty, "SurfaceSunkenBrush");
        return HubVisuals.Themed(bar, Border.BorderBrushProperty, "BorderSubtleBrush");
    }
}
