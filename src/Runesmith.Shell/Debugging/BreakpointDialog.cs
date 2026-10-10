using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using HammerUI.Controls;
using HammerUI.Services;

namespace Runesmith.Shell.Debugging;

/// <summary>What the breakpoint dialog returns: the changed breakpoint, or a request to remove it.</summary>
internal sealed record BreakpointEdit(LineBreakpoint? Breakpoint, bool Remove);

/// <summary>Edits a breakpoint's condition, hit count, log message and whether it is on.</summary>
internal static class BreakpointDialog
{
    /// <param name="focusLogMessage">Whether the log message gets the keyboard focus, as when adding a log point.</param>
    public static async Task<BreakpointEdit?> ShowAsync(DialogService dialogs, LineBreakpoint breakpoint, bool exists, bool focusLogMessage)
    {
        var condition = Field(breakpoint.Condition, "Such as count > 3");
        var hitCount = Field(breakpoint.HitCondition, "Such as 5");
        var logMessage = Field(breakpoint.LogMessage, "Such as count is {count}");
        var enabled = new CheckBox { Content = "Enabled", IsChecked = breakpoint.IsEnabled };
        var ok = new Button { Content = "OK", Classes = { "accent" }, IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        var remove = new Button { Content = "Remove", Classes = { "danger" }, MinWidth = 84, IsVisible = exists };
        var footer = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(remove, Dock.Left);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, ok } };
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.AddRange([remove, buttons]);

        var dialog = new Dialog
        {
            Header = $"Breakpoint at {Path.GetFileName(breakpoint.Path)}:{breakpoint.Line + 1}",
            Width = 480,
            ShowCloseButton = false,
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Label("Condition"), condition, Hint("Stops only when this expression is true."),
                    Label("Hit count"), hitCount, Hint("Stops only once the line has run this many times."),
                    Label("Log message"), logMessage, Hint("Writes this to the debug console instead of stopping; put expressions in braces."),
                    enabled,
                },
            },
            Footer = footer,
        };

        LineBreakpoint Result() => breakpoint with
        {
            Condition = Text(condition),
            HitCondition = Text(hitCount),
            LogMessage = Text(logMessage),
            IsEnabled = enabled.IsChecked == true,
        };

        ok.Click += (_, _) => dialog.Close(new BreakpointEdit(Result(), false));
        cancel.Click += (_, _) => dialog.Close(null);
        remove.Click += (_, _) => dialog.Close(new BreakpointEdit(null, true));
        foreach (var box in new[] { condition, hitCount, logMessage })
        {
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    e.Handled = true;
                    dialog.Close(new BreakpointEdit(Result(), false));
                }
            };
        }

        var first = focusLogMessage ? logMessage : condition;
        first.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            first.Focus();
            first.SelectAll();
        }, DispatcherPriority.Background);
        return await dialogs.ShowAsync(dialog) as BreakpointEdit;
    }

    private static TextBox Field(string? text, string placeholder) => new() { Text = text ?? "", PlaceholderText = placeholder, MinWidth = 400 };

    private static TextBlock Label(string text) => new() { Text = text, Margin = new Avalonia.Thickness(0, 6, 0, 0) };

    private static TextBlock Hint(string text) => new() { Text = text, Classes = { "caption", "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    private static string? Text(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
}
