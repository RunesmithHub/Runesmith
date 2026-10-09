using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI.Controls;
using Runesmith.Hub.Reports;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Hub;

/// <summary>The Report button of a plugin page: it opens the browser at the right form with the details filled in. Runesmith never sends a
/// report itself.</summary>
internal static class ReportMenu
{
    public static Button Create(ReportSubject subject, string name, NotificationService notifications, OutputService output)
    {
        var button = new Button
        {
            Classes = { "subtle" },
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new SymbolIcon { Data = HammerUI.Icons.Flag, Size = 15 }, new TextBlock { Text = "Report" } } },
        };
        ToolTip.SetTip(button, $"Report {name}");
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        menu.Items.Add(Item("It's malicious or a security risk", "Private, to Runesmith's administrators", "Shield", () => _ = ReportSecurityAsync(button, subject, notifications)));
        menu.Items.Add(Item("It's broken or doesn't work", "An issue in the plugin's repository", "AlertTriangle", () => _ = ReportBrokenAsync(subject, name, notifications, output)));
        menu.Items.Add(Item("It breaks the rules", "Impersonation, spam or license: a policy issue", "Flag", () => Launcher.OpenUrl(ReportLinks.Policy(subject).AbsoluteUri)));
        button.Flyout = menu;
        return button;
    }

    /// <summary>Gets what Runesmith fills in about a plugin.</summary>
    public static ReportSubject Subject(string id, string version, string repository, string runesmithVersion) =>
        new(id, version, repository, runesmithVersion, $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");

    private static MenuItem Item(string title, string hint, string icon, Action open)
    {
        var item = new MenuItem
        {
            Header = new StackPanel { Spacing = 1, Children = { new TextBlock { Text = title }, new TextBlock { Text = hint, Classes = { "caption" } } } },
            Icon = new SymbolIcon { Data = HubVisuals.Icon(icon), Size = 15 },
        };
        item.Click += (_, _) => open();
        return item;
    }

    private static async Task ReportSecurityAsync(Visual anchor, ReportSubject subject, NotificationService notifications)
    {
        if (TopLevel.GetTopLevel(anchor)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(ReportLinks.Details(subject));
        Launcher.OpenUrl(ReportLinks.Security().AbsoluteUri);
        notifications.Notify(NotificationKind.Info, "The plugin's details are on the clipboard",
            $"Paste them into GitHub's private report form. Without a GitHub account, write to {ReportLinks.SecurityEmail} instead.");
    }

    // The user sees the log lines, can edit or leave them out, and only then does Runesmith open the browser with them.
    private static async Task ReportBrokenAsync(ReportSubject subject, string name, NotificationService notifications, OutputService output)
    {
        var lines = output.Channels
            .SelectMany(channel => channel.Lines)
            .Where(line => line.Contains(subject.PluginId, StringComparison.OrdinalIgnoreCase) || line.Contains(name, StringComparison.OrdinalIgnoreCase))
            .TakeLast(30)
            .ToList();
        var include = new CheckBox { Content = "Include these recent log lines", IsChecked = false, IsEnabled = lines.Count > 0 };
        var text = new TextBox
        {
            Text = string.Join("\n", lines),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 160,
            IsEnabled = false,
            Classes = { "mono" },
            PlaceholderText = "No log lines mention this plugin.",
        };
        include.IsCheckedChanged += (_, _) => text.IsEnabled = include.IsChecked == true;

        var open = new Button { Content = "Open in browser", Classes = { "accent" }, MinWidth = 84 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        var dialog = new Dialog
        {
            Header = $"Report that {name} is broken",
            Description = "Runesmith opens a new issue in the plugin's repository with these details. You can edit everything before you submit it.",
            Width = 600,
            ShowCloseButton = false,
            Content = new StackPanel
            {
                Spacing = 10,
                Children = { new SelectableTextBlock { Text = ReportLinks.Details(subject).TrimEnd(), Classes = { "secondary" } }, include, text },
            },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, open } },
        };
        cancel.Click += (_, _) => dialog.Close();
        open.Click += (_, _) =>
        {
            dialog.Close();
            if (ReportLinks.Broken(subject, include.IsChecked == true ? text.Text : null) is { } url)
                Launcher.OpenUrl(url.AbsoluteUri);
            else
                notifications.Notify(NotificationKind.Warning, "The plugin's repository is not on GitHub", "Report the problem where its developer asks for reports.");
        };
        await notifications.Dialogs.ShowAsync(dialog);
    }
}
