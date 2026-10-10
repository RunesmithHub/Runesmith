using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI.Controls;
using HammerUI.Services;
using Runesmith.Hub;
using Runesmith.Hub.Enforcement;
using Runesmith.Hub.Installing;
using Runesmith.Shell.Services;
using RunesmithHub.Protocol.Resolution;

namespace Runesmith.Shell.Hub;

/// <summary>The dialogs of installing: the install plan with its warnings, the progress, the offer to restart, why there is no plan, and the
/// notice that a running plugin was blocked.</summary>
internal static class InstallDialogs
{
    /// <summary>Shows the plan and returns whether the user confirmed it.</summary>
    public static async Task<bool> ConfirmAsync(IDialogService dialogs, InstallPlanModel model, HubClient client)
    {
        var dialog = PlanDialog(model, client, out var install);
        install.Click += (_, _) => dialog.Close(true);
        return await dialogs.ShowAsync(dialog) is true;
    }

    /// <summary>Builds the install plan dialog; its Install button is never the default and stays off until every warning is acknowledged.</summary>
    public static Dialog PlanDialog(InstallPlanModel model, HubClient client, out Button install)
    {
        install = new Button { Content = model.ActionText, Classes = { "accent" }, MinWidth = 84, IsDefault = false, IsEnabled = model.CanInstall };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 84 };
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(new TextBlock { Text = model.Summary, Classes = { "secondary" }, Margin = new Thickness(0, 0, 0, 4) });
        var rows = new StackPanel();
        for (var i = 0; i < model.Rows.Count; i++)
            rows.Children.Add(PlanRow(model.Rows[i], client, last: i == model.Rows.Count - 1));
        body.Children.Add(rows);

        if (model.Review is { } review)
        {
            body.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 8, 0, 0),
                Children = { HubVisuals.Symbol("shield-check", 14, "SuccessBrush"), new TextBlock { Text = review, Classes = { "secondary" } } },
            });
        }

        if (model.Capabilities.Count > 0)
        {
            body.Children.Add(new TextBlock { Text = "Capabilities", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 2) });
            foreach (var capability in model.Capabilities)
                body.Children.Add(HubVisuals.Capability(capability, showFrom: model.Rows.Count > 1));
        }
        else if (model.Rows.Count(r => r.Kind != OperationKind.Remove) is > 0 and var added)
        {
            var subject = added == 1 ? "this plugin works" : "these plugins work";
            body.Children.Add(new TextBlock { Text = "Capabilities", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 2) });
            body.Children.Add(new TextBlock { Text = $"None declared: {subject} only inside the editor, through what Runesmith offers plugins.", Classes = { "secondary" }, TextWrapping = TextWrapping.Wrap });
        }

        var checkboxes = new List<CheckBox>();
        foreach (var warning in model.Warnings)
        {
            var acknowledge = new CheckBox { Content = new TextBlock { Text = warning.Acknowledgement, TextWrapping = TextWrapping.Wrap }, IsChecked = false, Margin = new Thickness(0, 6, 0, 0) };
            var button = install;
            acknowledge.IsCheckedChanged += (_, _) =>
            {
                warning.IsAcknowledged = acknowledge.IsChecked == true;
                button.IsEnabled = model.CanInstall;
            };
            checkboxes.Add(acknowledge);
            body.Children.Add(Warning(warning, acknowledge));
        }

        var dialog = new Dialog
        {
            Header = model.Title,
            Width = 620,
            ShowCloseButton = true,
            Content = body,
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, install } },
        };
        cancel.Click += (_, _) => dialog.Close(false);
        return dialog;
    }

    /// <summary>The progress of an install, in the steps the transaction takes.</summary>
    public sealed class Progress
    {
        private static readonly (InstallStep Step, string Text)[] Steps =
        [
            (InstallStep.Downloading, "Downloading the packages, within the sizes their records state"),
            (InstallStep.Verifying, "Checking each package against the hub's signed records"),
            (InstallStep.Unpacking, "Unpacking safely into a staging folder and checking each manifest"),
            (InstallStep.Committing, "Switching the installed plugins in one step"),
        ];

        private readonly List<(SymbolIcon Icon, TextBlock Text)> lines = [];
        private readonly int packages;
        private int downloaded;
        private readonly ProgressBar bar = new() { Minimum = 0, Maximum = Steps.Length, Height = 6 };
        private readonly TextBlock current = new() { Classes = { "caption" }, Margin = new Thickness(25, 6, 0, 0) };

        public Progress(InstallPlanModel model)
        {
            packages = Math.Max(1, model.Rows.Count(r => r.Kind != OperationKind.Remove));
            var list = new StackPanel { Spacing = 6, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var (_, text) in Steps)
            {
                var icon = HubVisuals.Symbol("clock", 15, "TextMutedBrush");
                var label = new TextBlock { Text = text, Classes = { "secondary" }, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
                lines.Add((icon, label));
                list.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { icon, label } });
            }

            Dialog = new Dialog
            {
                Header = model.Title,
                Width = 520,
                ShowCloseButton = false,
                Content = new StackPanel
                {
                    Children = { bar, list, current, new TextBlock { Text = "If anything fails, nothing changes.", Classes = { "caption" }, Margin = new Thickness(0, 10, 0, 0) } },
                },
            };
        }

        public Dialog Dialog { get; }

        public void Report(InstallProgress progress)
        {
            var reached = progress.Step switch
            {
                InstallStep.Downloading => 0,
                InstallStep.Verifying => 1,
                InstallStep.Unpacking or InstallStep.Checking => 2,
                InstallStep.Committing => 3,
                _ => Steps.Length,
            };
            if (progress.Step == InstallStep.Downloading)
                downloaded++;
            bar.Value = reached == 0 ? (downloaded - 0.5) / packages : reached;
            for (var i = 0; i < lines.Count; i++)
            {
                var done = i < reached || (i == 1 && reached > 1);
                lines[i].Icon.Data = HubVisuals.Icon(done ? "Check" : "clock");
                HubVisuals.Themed(lines[i].Icon, SymbolIcon.ForegroundProperty, done ? "SuccessBrush" : i == reached ? "AccentBrush" : "TextMutedBrush");
                HubVisuals.Themed(lines[i].Text, TextBlock.ForegroundProperty, done || i == reached ? "TextPrimaryBrush" : "TextSecondaryBrush");
            }

            current.Text = progress.Plugin is { } name ? progress.Step == InstallStep.Downloading ? $"Downloading {name}" : $"Unpacking {name}" : "";
        }

        public void Close() => Dialog.Close();
    }

    /// <summary>Says that the changes take effect after a restart, and offers to restart now.</summary>
    public static Task RestartAsync(IDialogService dialogs, string message, bool canRestart, Action restart)
    {
        var later = new Button { Content = "Later", IsCancel = true, MinWidth = 84 };
        var now = new Button { Content = "Restart now", Classes = { "accent" }, IsDefault = true, MinWidth = 84, IsVisible = canRestart };
        var dialog = new Dialog
        {
            Header = "Restart to finish",
            Width = 460,
            ShowCloseButton = false,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 12,
                Children =
                {
                    HubVisuals.Symbol("CheckCircle", 22, "SuccessBrush"),
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 380, VerticalAlignment = VerticalAlignment.Center },
                },
            },
            Footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { later, now } },
        };
        later.Click += (_, _) => dialog.Close();
        now.Click += (_, _) =>
        {
            dialog.Close();
            restart();
        };
        return dialogs.ShowAsync(dialog);
    }

    /// <summary>Explains why no plan exists, with every step of the reasoning behind a toggle.</summary>
    public static Task FailureAsync(IDialogService dialogs, string title, ResolutionFailure failure)
    {
        var close = new Button { Content = "Close", Classes = { "accent" }, IsDefault = true, IsCancel = true, MinWidth = 84 };
        var body = new StackPanel { Spacing = 10, Children = { new SelectableTextBlock { Text = failure.Message, TextWrapping = TextWrapping.Wrap } } };
        if (failure.Derivation.Count > 1)
        {
            var steps = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6, 0, 0) };
            foreach (var step in failure.Derivation)
                steps.Children.Add(new SelectableTextBlock { Text = step, TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } });
            body.Children.Add(new Expander { Header = "How Runesmith worked this out", Content = steps, HorizontalAlignment = HorizontalAlignment.Stretch });
        }

        var dialog = new Dialog { Header = title, Width = 560, ShowCloseButton = false, Content = body, Footer = close };
        close.Click += (_, _) => dialog.Close();
        return dialogs.ShowAsync(dialog);
    }

    /// <summary>Says that plugins that run were blocked and stop at the restart, and offers only to restart: there is no "later".</summary>
    public static Task BlockedAsync(IDialogService dialogs, IReadOnlyList<PluginNotice> notices, bool canRestart, Action restart)
    {
        if (notices.Count == 0)
            return Task.CompletedTask;

        var body = new StackPanel { Spacing = 10 };
        foreach (var notice in notices)
        {
            var text = notice.Message ?? "";
            if (notice.Dependents.Count > 0)
                text += $" {string.Join(", ", notice.Dependents)} {(notice.Dependents.Count == 1 ? "needs" : "need")} it and {(notice.Dependents.Count == 1 ? "stops" : "stop")} with it.";
            body.Children.Add(HubVisuals.Notice(PageNoticeKind.Danger, notice.Title, text.Trim()));
        }

        var button = new Button { Content = canRestart ? "Restart now" : "OK", Classes = { "danger" }, IsDefault = true, MinWidth = 100 };
        var dialog = new Dialog
        {
            Header = notices.Count == 1 ? $"{notices[0].Name} was turned off" : "Plugins were turned off",
            Description = "Runesmith restarts to stop it. Plugins that depend on it stop with it.",
            Width = 540,
            ShowCloseButton = false,
            Content = body,
            Footer = button,
        };
        button.Click += (_, _) =>
        {
            dialog.Close();
            if (canRestart)
                restart();
        };
        return dialogs.ShowAsync(dialog);
    }

    private static Border PlanRow(PlanRow row, HubClient client, bool last)
    {
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        name.Inlines!.Add(new Avalonia.Controls.Documents.Run(row.Name) { FontWeight = FontWeight.SemiBold });
        name.Inlines.Add(new Avalonia.Controls.Documents.Run("  " + row.Change));
        var what = new TextBlock { Text = row.What, Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var line = new DockPanel { Margin = new Thickness(0, 7) };
        var icon = HubVisuals.PluginIcon(client, row.Id, row.Name, row.Tier, row.Icon, null, 24);
        var badge = HubVisuals.TierBadge(row.Tier);
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(what, Dock.Right);
        icon.Margin = new Thickness(0, 0, 10, 0);
        what.Margin = new Thickness(12, 0, 0, 0);
        line.Children.AddRange([icon, what, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { name, badge } }]);
        var border = new Border { Child = line, BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1) };
        return HubVisuals.Themed(border, Border.BorderBrushProperty, "BorderSubtleBrush");
    }

    private static Panel Warning(UnverifiedWarning warning, CheckBox acknowledge)
    {
        var content = new StackPanel { Spacing = 6, Margin = new Thickness(12) };
        content.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { HubVisuals.Symbol("AlertTriangle", 16, "WarningBrush"), new TextBlock { Text = $"{warning.Name} is unverified", FontWeight = FontWeight.SemiBold } },
        });
        content.Children.Add(new TextBlock { Text = warning.Text, TextWrapping = TextWrapping.Wrap });
        if (warning.Capabilities.Count == 0)
            content.Children.Add(new TextBlock { Text = "Capabilities: none declared.", Classes = { "secondary" } });
        foreach (var capability in warning.Capabilities)
            content.Children.Add(HubVisuals.Capability(capability, showFrom: false));

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        if (warning.Repository is { } repository)
            links.Children.Add(Link("Source repository", repository));
        if (warning.Analysis is { } analysis)
            links.Children.Add(Link("Build and analysis", analysis));
        if (links.Children.Count > 0)
            content.Children.Add(links);
        content.Children.Add(acknowledge);
        var panel = HubVisuals.Tinted("WarningBrush", content, new CornerRadius(8));
        panel.Margin = new Thickness(0, 12, 0, 0);
        return panel;
    }

    /// <summary>A button that opens an address in the browser.</summary>
    public static Button Link(string text, string url)
    {
        var button = new Button
        {
            Classes = { "subtle", "small" },
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { new TextBlock { Text = text }, new SymbolIcon { Data = HammerUI.Icons.ExternalLink, Size = 12 } } },
        };
        HubVisuals.Themed(button, Button.ForegroundProperty, "AccentBrush");
        ToolTip.SetTip(button, url);
        button.Click += (_, _) => Launcher.OpenUrl(url);
        return button;
    }
}
