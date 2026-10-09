using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using HammerUI;
using HammerUI.Controls;
using static Runesmith.Plugins.Views.ViewHelpers;

namespace Runesmith.GitHub.Views.Account;

/// <summary>The Add an organization dialog: what happens on GitHub, then Continue on GitHub. It closes with true when the user continues.</summary>
internal sealed class AddOrganizationDialog : Dialog
{
    public AddOrganizationDialog(string? appName)
    {
        GitHubIcons.EnsureRegistered();
        Header = "Add an organization";
        Width = 460;
        ShowCloseButton = true;

        var app = appName is { Length: > 0 } ? appName : "Runesmith's GitHub App";
        var tile = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top, Child = Icon(GitHubIcons.Get(GitHubIcons.Building), 20, "AccentBrush") };
        ((Control)tile.Child).HorizontalAlignment = HorizontalAlignment.Center;
        Brush(tile, Border.BackgroundProperty, "AccentSubtleBrush");
        var text = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Label($"GitHub asks which organization to add {app} to, and which of its repositories Runesmith may use.", wrap: true),
                Label("Organization owners install it right away; members send a request that an owner approves.", "TextSecondaryBrush", wrap: true),
            },
        };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,14,*"), Margin = new Thickness(0, 4, 0, 4), Children = { tile, text } };
        Grid.SetColumn(text, 2);

        var note = new DockPanel { Children = { Icon(Icons.Info, 12, "TextMutedBrush"), Label("Come back to Runesmith when you are done; the new repositories show up on their own.", "TextMutedBrush", 12, wrap: true) } };
        note.Children[0].Margin = new Thickness(0, 2, 6, 0);
        note.Children[0].VerticalAlignment = VerticalAlignment.Top;
        DockPanel.SetDock(note.Children[0], Dock.Left);
        Content = new StackPanel { Spacing = 14, Children = { body, note } };

        var cancel = new Button { Content = "Cancel", MinWidth = 84, IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        var proceed = IconButton(Icons.ExternalLink, "Continue on GitHub", "accent");
        proceed.IsDefault = true;
        proceed.MinWidth = 96;
        proceed.Click += (_, _) => Close(true);
        Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, proceed } };
    }

    protected override Type StyleKeyOverride => typeof(Dialog);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(false);
        }
    }
}
