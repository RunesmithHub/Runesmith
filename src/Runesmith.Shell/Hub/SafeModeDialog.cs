using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI.Controls;
using HammerUI.Services;

namespace Runesmith.Shell.Hub;

/// <summary>The offer of safe mode after Runesmith failed to start twice in a row, naming the plugins that were loading.</summary>
public static class SafeModeDialog
{
    /// <summary>Shows the offer. Runesmith already started in safe mode, so the choice is to stay in it or start normally.</summary>
    /// <param name="plugins">The names and versions of the plugins that were loading.</param>
    /// <param name="startNormally">Restarts Runesmith with every plugin.</param>
    public static Task ShowAsync(IDialogService dialogs, string reason, IReadOnlyList<(string Name, string Version)> plugins, Action startNormally)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(plugins);
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = plugins.Count == 0 ? reason : $"{reason} {(plugins.Count == 1 ? "This plugin was" : "These plugins were")} loading when it happened:",
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var (name, version) in plugins)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2) };
            line.Children.Add(HubVisuals.Symbol("Puzzle", 16, "TextSecondaryBrush"));
            line.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold });
            line.Children.Add(new TextBlock { Text = version, Classes = { "caption", "mono" }, VerticalAlignment = VerticalAlignment.Center });
            body.Children.Add(line);
        }

        body.Children.Add(new TextBlock
        {
            Text = "Runesmith started in safe mode, which loads no plugins. Turn off the suspect plugin in the plugin manager, then restart normally.",
            TextWrapping = TextWrapping.Wrap,
            Classes = { "secondary" },
            Margin = new Thickness(0, 6, 0, 0),
        });

        var normal = new Button { Content = "Start normally", MinWidth = 84 };
        var stay = new Button { Content = "Stay in safe mode", Classes = { "accent" }, IsDefault = true, IsCancel = true, MinWidth = 84 };
        var dialog = new Dialog
        {
            Header = "Runesmith didn't start properly",
            Width = 500,
            ShowCloseButton = false,
            Content = body,
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { normal, stay } },
        };
        stay.Click += (_, _) => dialog.Close();
        normal.Click += (_, _) =>
        {
            dialog.Close();
            startNormally();
        };
        return dialogs.ShowAsync(dialog);
    }
}
