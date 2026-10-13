using Avalonia.Controls;
using Avalonia.Media;
using HammerUI.Controls;
using HammerUI.Services;
using Runesmith.Composition;

namespace Runesmith.Shell.Hub;

/// <summary>Says that Runesmith started in safe mode because its own assemblies come from different builds, and which ones.</summary>
public static class IncompleteBuildDialog
{
    public static Task ShowAsync(IDialogService dialogs, IReadOnlyList<HostMismatch> mismatches)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(mismatches);
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = Strings.PluginsIncompleteExplanation, TextWrapping = TextWrapping.Wrap });
        foreach (var mismatch in mismatches)
        {
            body.Children.Add(new TextBlock
            {
                Text = Describe(mismatch),
                TextWrapping = TextWrapping.Wrap,
                Classes = { "mono" },
            });
        }

        var close = new Button { Content = Strings.PluginsOk, Classes = { "accent" }, IsDefault = true, IsCancel = true, MinWidth = 84 };
        var dialog = new Dialog
        {
            Header = Strings.PluginsIncompleteHeader,
            Width = 560,
            ShowCloseButton = false,
            Content = body,
            Footer = close,
        };
        close.Click += (_, _) => dialog.Close();
        return dialogs.ShowAsync(dialog);
    }

    private static string Describe(HostMismatch mismatch) => mismatch.RequiredBy is { } user
        ? Strings.PluginsIncompleteBuiltAgainst(user: user, assembly: mismatch.Assembly, expected: mismatch.Expected.ToString(), found: mismatch.Found.ToString())
        : Strings.PluginsIncompleteApi(assembly: mismatch.Assembly, expected: mismatch.Expected.ToString(), found: mismatch.Found.ToString());
}
