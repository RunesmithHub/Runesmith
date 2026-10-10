using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;

namespace Runesmith.Shell.Hub;

/// <summary>The line over an editor or the editor area that suggests installing an official plugin.</summary>
internal static class PluginSuggestionBar
{
    /// <param name="close">Removes the line.</param>
    public static Control Create(SuggestedPlugin plugin, PluginSuggestions suggestions, Action close)
    {
        var install = PluginManagerView.SmallButton("Install", () =>
        {
            close();
            suggestions.Install(plugin);
        }, accent: true);
        var decline = PluginManagerView.SmallButton("Don't suggest again", () =>
        {
            close();
            suggestions.StopSuggesting(plugin);
        });
        var dismiss = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = Icons.X, Size = 14 } };
        ToolTip.SetTip(dismiss, "Close");
        dismiss.Click += (_, _) => close();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { install, decline, dismiss } };
        var glyph = HubVisuals.Symbol("Puzzle", 15, "AccentBrush");
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var text = new TextBlock { Text = plugin.Message, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 12, 0) };
        DockPanel.SetDock(glyph, Dock.Left);
        DockPanel.SetDock(actions, Dock.Right);
        var line = new DockPanel { Margin = new Thickness(12, 5, 8, 5), Children = { glyph, actions, text } };
        var bar = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Child = line, Name = "PluginSuggestion" };
        HubVisuals.Themed(bar, Border.BackgroundProperty, "SurfaceSunkenBrush");
        return HubVisuals.Themed(bar, Border.BorderBrushProperty, "BorderSubtleBrush");
    }
}
