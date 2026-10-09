using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI.Controls;
using HammerUI.Services;

namespace Runesmith.Git.Commands;

/// <summary>What the user typed in a name dialog, and the state of its check box.</summary>
internal sealed record NameAnswer(string Name, bool Option);

/// <summary>The small dialogs of the Git features, such as the name of a new branch, shown with the window's dialog service.</summary>
internal static class GitDialogs
{
    /// <summary>Asks for a name; returns null when the user cancels.</summary>
    /// <param name="validate">Says why a name cannot be used, or returns null when it can; it may run Git.</param>
    /// <param name="option">The text of a check box under the field, such as "Check out the branch", or null for none.</param>
    public static async Task<NameAnswer?> AskNameAsync(IDialogService dialogs, string title, string description, string label, string initial,
        Func<string, Task<string?>> validate, string confirmText, string? option = null, bool optionChecked = true)
    {
        var input = new TextBox { Text = initial };
        var error = new TextBlock { Classes = { "caption" }, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable("DangerBrush"));
        var check = option is null ? null : new CheckBox { Content = option, IsChecked = optionChecked, Margin = new Thickness(0, 4, 0, 0) };
        var ok = new Button { Content = confirmText, Classes = { "accent" }, IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        var body = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = label }, input, error } };
        if (check is not null)
            body.Children.Add(check);
        var dialog = new Dialog
        {
            Header = title,
            Description = description,
            Width = 460,
            ShowCloseButton = false,
            Content = body,
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, ok } },
        };

        async Task AcceptAsync()
        {
            var text = input.Text?.Trim() ?? "";
            ok.IsEnabled = false;
            var problem = await validate(text);
            ok.IsEnabled = true;
            if (problem is not null)
            {
                error.Text = problem;
                error.IsVisible = true;
                return;
            }

            dialog.Close(new NameAnswer(text, check?.IsChecked == true));
        }

        ok.Click += (_, _) => _ = AcceptAsync();
        cancel.Click += (_, _) => dialog.Close(null);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _ = AcceptAsync();
                e.Handled = true;
            }
        };
        input.TextChanged += (_, _) => error.IsVisible = false;
        input.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            input.Focus();
            input.SelectAll();
        }, DispatcherPriority.Background);

        return await dialogs.ShowAsync(dialog) as NameAnswer;
    }
}
