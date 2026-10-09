using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;

namespace Runesmith.Git.Views.Changes;

/// <summary>How a banner looks: what kind of news it brings.</summary>
internal enum BannerTone
{
    Info,
    Warning,
    Error,
}

/// <summary>A calm inline message across a panel: an icon, a title, Git's own words, up to two actions and a close button.</summary>
internal sealed class Banner : Border
{
    private readonly SymbolIcon icon = new() { Size = 16, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
    private readonly Border strip = new() { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 10, 0) };
    private readonly TextBlock title = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly SelectableTextBlock message = new() { TextWrapping = TextWrapping.Wrap, Classes = { "caption" }, MaxHeight = 120 };
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button close = new() { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = Icons.X, Size = 12 }, VerticalAlignment = VerticalAlignment.Top };

    public Banner()
    {
        Classes.Add("card");
        Padding = new Thickness(10, 8, 6, 8);
        Margin = new Thickness(8, 6, 8, 2);
        IsVisible = false;
        message[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextSecondaryBrush");
        ToolTip.SetTip(close, "Dismiss");
        close.Click += (_, _) => Hide();
        var text = new StackPanel { Spacing = 2, Children = { title, message, actions } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        Grid.SetColumn(icon, 1);
        Grid.SetColumn(text, 2);
        Grid.SetColumn(close, 3);
        icon.Margin = new Thickness(0, 1, 8, 0);
        grid.Children.AddRange([strip, icon, text, close]);
        Child = grid;
    }

    /// <summary>Gets or sets whether the close button shows; a banner about the repository's state stays until the state changes.</summary>
    public bool IsDismissible
    {
        get => close.IsVisible;
        set => close.IsVisible = value;
    }

    /// <summary>Shows the banner with a message and its actions.</summary>
    public void Show(BannerTone tone, string heading, string? text, params (string Text, Action Run)[] buttons)
    {
        var brush = tone switch
        {
            BannerTone.Error => "DangerBrush",
            BannerTone.Warning => "WarningBrush",
            _ => "AccentBrush",
        };
        icon.Data = tone switch
        {
            BannerTone.Error => Icons.AlertCircle,
            BannerTone.Warning => Icons.AlertTriangle,
            _ => Icons.Info,
        };
        icon[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(brush);
        strip[!BackgroundProperty] = new DynamicResourceExtension(brush);
        title.Text = heading;
        message.Text = text;
        message.IsVisible = !string.IsNullOrEmpty(text);
        actions.Children.Clear();
        foreach (var (label, run) in buttons)
        {
            var button = new Button { Content = label, Classes = { "small" } };
            button.Click += (_, _) => run();
            actions.Children.Add(button);
        }

        actions.IsVisible = buttons.Length > 0;
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}
