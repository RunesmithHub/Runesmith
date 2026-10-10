using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HammerUI.Controls;
using Runesmith.Shell.Views;
using RunesmithHub.Protocol.Index;

namespace Runesmith.Shell.Hub;

/// <summary>A plugin's page, built from the hub's signed records: who made it, how it was reviewed, what it may do, what it needs and its
/// versions, with Install, or Update, Disable and Uninstall, and Report.</summary>
/// <remarks>Install is never the default button, and keys do nothing to it while the page opens, so a key press meant for something else
/// cannot install.</remarks>
internal sealed class PluginPageView : StackPanel
{
    /// <summary>How long after the page opens the Install button ignores keys.</summary>
    public static readonly TimeSpan KeyGrace = TimeSpan.FromSeconds(1.5);

    private readonly HubService hub;
    private readonly PluginManagerView owner;
    private readonly PluginPageModel page;
    private readonly PageTarget target;
    private readonly bool compact;
    private readonly DateTimeOffset opened = DateTimeOffset.UtcNow;

    public PluginPageView(HubService hub, PluginManagerView owner, PluginPageModel page, PageTarget target, bool compact)
    {
        this.hub = hub;
        this.owner = owner;
        this.page = page;
        this.target = target;
        this.compact = compact;
        Spacing = 14;

        var back = new Button
        {
            Classes = { "subtle", "small" },
            HorizontalAlignment = HorizontalAlignment.Left,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new SymbolIcon { Data = HammerUI.Icons.ChevronLeft, Size = 14 }, new TextBlock { Text = $"Back to {hub.Model.Tab}" } } },
        };
        back.Click += (_, _) => hub.Model.Back();
        Children.Add(back);

        if (target.FromLink is { } link)
        {
            var note = "Runesmith shows what the hub's signed records say, not what the website said.";
            if (target.IgnoredLinks > 0)
                note += $" {target.IgnoredLinks} other {(target.IgnoredLinks == 1 ? "link" : "links")} that came within a minute {(target.IgnoredLinks == 1 ? "was" : "were")} ignored.";
            Children.Add(HubVisuals.Notice(PageNoticeKind.Info, $"Opened from a link: {link}", note, icon: "Link"));
        }

        if (page.Record is not { } record)
        {
            foreach (var notice in page.Notices)
                Children.Add(HubVisuals.Notice(notice.Kind, notice.Title, notice.Message));
            return;
        }

        Children.Add(Header(record));
        foreach (var notice in page.Notices)
        {
            Children.Add(HubVisuals.Notice(notice.Kind, notice.Title, notice.Message, notice.Advisory is { } advisory ? [InstallDialogs.Link("Advisory", advisory)] : null,
                icon: notice.Title.StartsWith("Unverified", StringComparison.Ordinal) ? "AlertTriangle" : null));
        }

        if (page.IsInstalled)
            Children.Add(Installed(record));

        var left = new StackPanel { Spacing = 12, Children = { Review(record), Versions(record) } };
        var right = new StackPanel { Spacing = 12, Children = { Capabilities(), Dependencies() } };
        if (compact)
        {
            Children.Add(right);
            Children.Add(left);
        }
        else
        {
            var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*") };
            Grid.SetColumn(right, 2);
            columns.Children.AddRange([left, right]);
            Children.Add(columns);
        }
    }

    private Control Header(PluginRecord record)
    {
        var icon = HubVisuals.PluginIcon(hub.Client, record.Id, record.Name, record.Tier, record.Icon.Size128, null, 56);
        var meta = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
        meta.Children.Add(new TextBlock { Text = page.Publisher, VerticalAlignment = VerticalAlignment.Center });
        meta.Children.Add(HubVisuals.TierBadge(record.Tier));
        if (page.Version is { } version)
            meta.Children.Add(new TextBlock { Text = $"Version {version.Version}", Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center });

        var text = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = record.Name, FontSize = 20, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                meta,
                new TextBlock { Text = page.Version?.Summary ?? record.Summary, TextWrapping = TextWrapping.Wrap, Classes = { "secondary" }, Margin = new Thickness(0, 2, 0, 0) },
            },
        };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
        if (page.IsPending)
            actions.Children.Add(HubVisuals.StateBadge("Installed, restart to enable", "Check", "SuccessBrush"));
        else if (page.CanInstall && !page.IsSelectedInstalled)
            actions.Children.Add(InstallButton(page.IsUpdate ? "Update" : page.IsInstalled ? $"Install {page.Version!.Version}" : "Install", opened, () => _ = hub.InstallAsync(page.PluginId, page.Version?.Version)));
        actions.Children.Add(ReportMenu.Create(ReportMenu.Subject(record.Id, page.InstalledVersion ?? page.Version?.Version.ToString() ?? "", record.Repository.Url, hub.Version),
            record.Name, owner.Notifications, owner.Output));

        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        icon.Margin = new Thickness(0, 0, 16, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        if (compact)
        {
            line.Children.AddRange([icon, text]);
            return new StackPanel { Spacing = 10, Children = { line, actions } };
        }

        line.Children.AddRange([icon, new SideBySidePanel { MinMainWidth = 220, Spacing = 16, Children = { text, actions } }]);
        return line;
    }

    /// <summary>Creates the Install button, which ignores Enter always and every key during <see cref="KeyGrace"/> after the page opened.</summary>
    internal static Button InstallButton(string text, DateTimeOffset opened, Action install)
    {
        var button = new Button
        {
            Classes = { "accent" },
            IsDefault = false,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new SymbolIcon { Data = HubVisuals.Icon("download"), Size = 15 }, new TextBlock { Text = text } } },
        };
        button.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Enter || DateTimeOffset.UtcNow - opened < KeyGrace)
                e.Handled = true;
        }, RoutingStrategies.Tunnel);
        button.Click += (_, _) => install();
        return button;
    }

    private Border Installed(PluginRecord record)
    {
        var state = hub.Client.State.Find(record.Id);
        var where = page.IsFromHub ? "is installed from the hub" : "is in your plugins folder";
        var text = new TextBlock { Text = $"Version {page.InstalledVersion} {where}.", VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        if (page.Problem is { } problem && !page.IsPending)
            text.Text += $" {problem}";
        var enabled = !hub.Disabled().Contains(record.Id);
        var toggle = PluginManagerView.SmallButton(enabled ? "Disable" : "Enable", () => hub.SetEnabled(record.Id, record.Name, !enabled));
        var row = new WrapPanel { ItemSpacing = 8, LineSpacing = 8, Children = { toggle } };
        if (page.IsFromHub && !page.IsPending)
            row.Children.Add(PluginManagerView.SmallButton("Uninstall", () => _ = hub.UninstallAsync(record.Id)));
        if (state is not null && record.Tier != PluginTier.Unverified)
        {
            var automatic = new CheckBox { Content = "Update automatically", IsChecked = state.AutoUpdate };
            ToolTip.SetTip(automatic, "Official and verified plugins update by themselves within their line.");
            automatic.IsCheckedChanged += (_, _) => hub.Client.SetAutoUpdate(record.Id, automatic.IsChecked == true);
            row.Children.Add(automatic);
        }

        row.VerticalAlignment = VerticalAlignment.Top;
        var ok = page.Problem is null || page.IsPending;
        var mark = HubVisuals.Symbol(ok ? "Check" : "AlertTriangle", 16, ok ? "SuccessBrush" : "WarningBrush");
        mark.VerticalAlignment = VerticalAlignment.Top;
        mark.Margin = new Thickness(0, 1, 0, 0);
        var status = new DockPanel();
        DockPanel.SetDock(mark, Dock.Left);
        text.Margin = new Thickness(8, 0, 0, 0);
        status.Children.AddRange([mark, text]);
        return PluginManagerView.Surface(new SideBySidePanel { MinMainWidth = 200, Spacing = 12, Children = { status, row } });
    }

    private Border Review(PluginRecord record)
    {
        var card = Card("Review");
        var reviewMark = HubVisuals.Symbol(page.IsReviewed ? "shield-check" : "AlertTriangle", 15, page.IsReviewed ? "SuccessBrush" : "WarningBrush");
        reviewMark.VerticalAlignment = VerticalAlignment.Top;
        reviewMark.Margin = new Thickness(0, 2, 8, 0);
        DockPanel.SetDock(reviewMark, Dock.Left);
        card.Children.Add(new DockPanel { Children = { reviewMark, new TextBlock { Text = page.Review, TextWrapping = TextWrapping.Wrap } } });

        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,14,*"), Margin = new Thickness(0, 6, 0, 0) };
        var rows = new List<(string, Control)> { ("Source", InstallDialogs.Link(record.Repository.Url.Replace("https://", "", StringComparison.Ordinal), record.Repository.Url)) };
        if (page.Version is { } version)
        {
            rows.Add(("Built from", new SelectableTextBlock { Text = version.Commit.Length > 7 ? version.Commit[..7] : version.Commit, Classes = { "mono" }, VerticalAlignment = VerticalAlignment.Center }));
            if (version.PublishedAt is { } published)
                rows.Add(("Published", new TextBlock { Text = published.ToLocalTime().ToString("d MMMM yyyy", CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center }));
            rows.Add(("Runesmith API", new TextBlock { Text = version.RunesmithApi.ToString(), Classes = { "mono" }, VerticalAlignment = VerticalAlignment.Center }));
        }

        rows.Add(("Categories", new TextBlock { Text = string.Join(", ", record.Categories), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }));
        for (var i = 0; i < rows.Count; i++)
        {
            facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var name = new TextBlock { Text = rows[i].Item1, Classes = { "muted" }, Margin = new Thickness(0, 4), VerticalAlignment = VerticalAlignment.Center };
            var value = rows[i].Item2;
            value.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(name, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 2);
            facts.Children.AddRange([name, value]);
        }

        card.Children.Add(facts);
        return Wrap(card);
    }

    private Border Capabilities()
    {
        var card = Card("Capabilities");
        var declared = page.Version?.Capabilities.Declared ?? [];
        if (declared.Count == 0)
            card.Children.Add(new TextBlock { Text = "Declares no capabilities: it works only inside the editor, through what Runesmith offers plugins.", TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } });
        foreach (var capability in declared)
        {
            var label = hub.Client.CapabilityLabel(capability.Id);
            card.Children.Add(HubVisuals.Capability(new CapabilityRow(capability.Id, label, capability.Reason, "", false, capability.Id is "native" or "dynamic-code" or "credentials" or "process"), showFrom: false));
        }

        if (page.Version?.NetworkHosts is { Count: > 0 } hosts)
        {
            var text = string.Join(", ", hosts.Select(h => h == "*" ? "hosts you choose" : h));
            card.Children.Add(new TextBlock { Text = $"Connects to {text}.", Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(25, 2, 0, 0) });
        }

        return Wrap(card);
    }

    private Border Dependencies()
    {
        var card = Card("Dependencies");
        var dependencies = page.Version?.Dependencies ?? [];
        if (dependencies.Count == 0)
            card.Children.Add(new TextBlock { Text = "Needs no other plugin.", Classes = { "secondary" } });
        foreach (var dependency in dependencies)
        {
            var record = hub.Client.Catalog?.FindPlugin(dependency.Id);
            var icon = HubVisuals.PluginIcon(hub.Client, dependency.Id, record?.Name ?? dependency.Id, record?.Tier, record?.Icon.Size64, null, 22);
            var range = new TextBlock { Text = dependency.Range.ToString(), Classes = { "caption", "mono" }, VerticalAlignment = VerticalAlignment.Center };
            var line = new DockPanel { Margin = new Thickness(0, 3) };
            DockPanel.SetDock(icon, Dock.Left);
            DockPanel.SetDock(range, Dock.Right);
            icon.Margin = new Thickness(0, 0, 8, 0);
            line.Children.AddRange([icon, range, new WrapPanel
            {
                ItemSpacing = 8,
                LineSpacing = 4,
                Children = { new TextBlock { Text = record?.Name ?? dependency.Id, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }, HubVisuals.TierBadge(record?.Tier) },
            }]);
            card.Children.Add(line);
        }

        return Wrap(card);
    }

    private Border Versions(PluginRecord record)
    {
        var card = Card("Versions");
        foreach (var (version, state) in page.Versions)
        {
            var selected = version == page.Version;
            var title = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
            title.Children.Add(new TextBlock { Text = version.Version.ToString(), FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal, Classes = { "mono" }, VerticalAlignment = VerticalAlignment.Center });
            if (HubVisuals.VersionBadge(state.State) is { } badge)
                title.Children.Add(badge);
            if (version.Version.ToString() == page.InstalledVersion)
                title.Children.Add(HubVisuals.StateBadge("Installed", "Check", "SuccessBrush"));
            if (version.PublishedAt is { } published)
                title.Children.Add(new TextBlock { Text = published.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture), Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center });

            var entry = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4) };
            entry.Children.Add(title);
            if (selected && !string.IsNullOrWhiteSpace(version.ReleaseNotes))
                entry.Children.Add(new SelectableTextBlock { Text = version.ReleaseNotes, TextWrapping = TextWrapping.Wrap, Classes = { "secondary" } });

            if (!selected && state.IsInstallable)
            {
                var choose = new Button { Classes = { "subtle" }, Padding = new Thickness(6, 2), HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, Content = entry };
                ToolTip.SetTip(choose, $"Select {version.Version}");
                var picked = version.Version;
                choose.Click += (_, _) => hub.Model.ShowPage(target with { Version = picked });
                card.Children.Add(choose);
            }
            else
            {
                entry.Margin = new Thickness(6, 4);
                card.Children.Add(entry);
            }
        }

        if (record.Versions.Count == 0)
            card.Children.Add(new TextBlock { Text = "No version is published yet.", Classes = { "secondary" } });
        return Wrap(card);
    }

    private static StackPanel Card(string title) => new() { Spacing = 6, Children = { new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 2) } } };

    private static Border Wrap(StackPanel card)
    {
        var border = new Border { Padding = new Thickness(14, 12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = card };
        HubVisuals.Themed(border, Border.BorderBrushProperty, "BorderSubtleBrush");
        return HubVisuals.Themed(border, Border.BackgroundProperty, "SurfaceSunkenBrush");
    }
}
