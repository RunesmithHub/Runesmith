using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using HammerUI.Controls;
using HammerUI.Services;

namespace Runesmith.Shell.Views;

/// <summary>Asks the user for a line of text, such as the name of a new file.</summary>
internal static class Prompt
{
    /// <param name="validate">Returns why a text cannot be used, or null when it can.</param>
    public static async Task<string?> AskAsync(DialogService dialogs, string title, string label, string initial = "", Func<string, string?>? validate = null, (int Start, int Length)? select = null)
    {
        var input = new TextBox { Text = initial, MinWidth = 360 };
        var error = new TextBlock { Classes = { "caption" }, IsVisible = false, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        error.Bind(TextBlock.ForegroundProperty, error.GetResourceObservable("DangerBrush"));
        var ok = new Button { Content = "OK", Classes = { "accent" }, IsDefault = true, MinWidth = 84 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        var dialog = new Dialog
        {
            Header = title,
            Width = 440,
            ShowCloseButton = false,
            Content = new StackPanel { Spacing = 6, Children = { new TextBlock { Text = label }, input, error } },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, ok } },
        };

        void Accept()
        {
            var text = input.Text?.Trim() ?? "";
            if (validate?.Invoke(text) is { } problem)
            {
                error.Text = problem;
                error.IsVisible = true;
                return;
            }

            dialog.Close(text);
        }

        ok.Click += (_, _) => Accept();
        cancel.Click += (_, _) => dialog.Close(null);
        input.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Accept();
                e.Handled = true;
            }
        };
        input.TextChanged += (_, _) => error.IsVisible = false;
        input.AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            input.Focus();
            if (select is { } range)
            {
                input.SelectionStart = range.Start;
                input.SelectionEnd = range.Start + range.Length;
            }
            else
            {
                input.SelectAll();
            }
        }, DispatcherPriority.Background);

        return await dialogs.ShowAsync(dialog) as string;
    }

    /// <summary>Says why a file name cannot be used, or returns null when it can.</summary>
    public static string? ValidateFileName(string name, string folder, string? except = null)
    {
        if (name.Length == 0)
            return "Type a name.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return "A name cannot contain " + string.Join(" ", Path.GetInvalidFileNameChars().Where(c => c >= ' ').Select(c => c.ToString())) + ".";
        var path = Path.Combine(folder, name);
        if (!string.Equals(name, except, StringComparison.Ordinal) && (File.Exists(path) || Directory.Exists(path)))
            return $"{name} exists already.";
        return null;
    }
}
