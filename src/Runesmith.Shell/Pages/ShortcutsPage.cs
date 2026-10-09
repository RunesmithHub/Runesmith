using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Services;
using Runesmith.Shell.Views;
using Bindings = Runesmith.Shell.Commands.KeyBindings;

namespace Runesmith.Shell.Pages;

/// <summary>Every command with its key binding, searchable, where each binding can be changed or reset.</summary>
internal sealed class ShortcutsPage : DockPanel
{
    private readonly CommandService commands;
    private readonly NotificationService notifications;
    private readonly SearchBox search = new() { PlaceholderText = "Search commands or keys", Width = 320 };
    private readonly StackPanel rows = new() { Spacing = 2, Margin = new Thickness(28, 8, 28, 40), MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Left };

    public ShortcutsPage(CommandService commands, NotificationService notifications)
    {
        this.commands = commands;
        this.notifications = notifications;
        this[!BackgroundProperty] = new DynamicResourceExtension("SurfaceBrush");

        var hint = new TextBlock
        {
            Text = $"Click a binding to change it. Your bindings are saved in {CommandService.UserBindingsPath}.",
            Classes = { "caption", "muted" },
            TextWrapping = TextWrapping.Wrap,
        };
        var header = new StackPanel { Spacing = 6, Margin = new Thickness(28, 18, 28, 8) };
        var top = new DockPanel();
        DockPanel.SetDock(search, Dock.Right);
        top.Children.Add(search);
        top.Children.Add(new TextBlock { Text = "Keyboard Shortcuts", FontSize = 22, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        header.Children.AddRange([top, hint]);
        DockPanel.SetDock(header, Dock.Top);
        Children.Add(header);
        Children.Add(new ScrollViewer { Content = rows });

        search.PropertyChanged += (_, e) =>
        {
            if (e.Property == SearchBox.TextProperty)
                Fill();
        };
        commands.Changed += (_, _) => UiThread.Run(Fill);
        Fill();
    }

    private void Fill()
    {
        rows.Children.Clear();
        var query = search.Text?.Trim() ?? "";
        foreach (var group in commands.Commands
            .Where(c => query.Length == 0 || c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                || commands.GetKeyBinding(c.Id)?.Contains(query, StringComparison.OrdinalIgnoreCase) == true)
            .GroupBy(c => c.Category)
            .OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            rows.Children.Add(new TextBlock { Text = group.Key, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 4) });
            foreach (var command in group.OrderBy(c => c.Title, StringComparer.CurrentCulture))
                rows.Children.Add(Row(command));
        }
    }

    private DockPanel Row(Sdk.Commands.CommandDefinition command)
    {
        var binding = commands.GetKeyBinding(command.Id);
        var change = new Button { Classes = { "subtle", "small" }, MinWidth = 140, HorizontalContentAlignment = HorizontalAlignment.Right };
        change.Content = binding is null
            ? new TextBlock { Text = "Add a binding", Classes = { "muted" } }
            : new ShortcutBadge { Text = binding };
        ToolTip.SetTip(change, "Change the binding");
        change.Click += (_, _) => _ = ChangeAsync(command, binding);

        var title = new TextBlock { Text = command.Title, VerticalAlignment = VerticalAlignment.Center };
        var id = new TextBlock { Text = command.Id, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var row = new DockPanel { Margin = new Thickness(4, 0) };
        DockPanel.SetDock(change, Dock.Right);
        row.Children.Add(change);
        row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { title, id } });
        return row;
    }

    private async Task ChangeAsync(Sdk.Commands.CommandDefinition command, string? current)
    {
        var text = await Prompt.AskAsync(
            notifications.Dialogs,
            $"Key binding of {command.Title}",
            "Type keys such as Ctrl+Shift+K; separate several with | . Leave it empty to remove the binding.",
            current ?? "",
            value => value.Length == 0 || Bindings.Parse(value).Count == value.Split('|', StringSplitOptions.RemoveEmptyEntries).Length
                ? null
                : "Some keys are not known. Write modifiers first, then one key, such as Ctrl+Alt+Down or F5.");
        if (text is null)
            return;

        commands.SetUserBinding(command.Id, text.Length == 0 ? "" : Bindings.Format(Bindings.Parse(text)));
    }
}
