using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI.Controls;
using Runesmith.Hub;
using Runesmith.Hub.Catalog;
using Runesmith.Hub.Enforcement;
using Runesmith.Hub.Updates;
using Runesmith.Sdk;
using Runesmith.Sdk.Localization;
using Runesmith.Shell.Localization;
using Runesmith.Shell.Pages;
using Runesmith.Shell.Services;
using Runesmith.Shell.Views;
using RunesmithHub.Protocol.Index;

namespace Runesmith.Shell.Hub;

/// <summary>The plugin manager: the installed plugins with their notices, the updates, the catalog and plugin pages. The welcome screen's
/// Plugins page and the Plugins tool window each show one, over the same model.</summary>
internal sealed class PluginManagerView : DockPanel
{
    private readonly HubService hub;
    private readonly PluginManagerModel model;
    private readonly bool compact;
    private readonly Action? openSettings;
    private readonly SegmentedControl tabs = new();
    private readonly StackPanel body = new() { Spacing = 10 };
    private readonly TextBlock status = new() { Classes = { "caption" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly SymbolIcon statusIcon = new() { Size = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly SearchBox search = new SearchBox().Localize(SearchBox.PlaceholderTextProperty, () => Strings.PluginsSearch);
    private readonly ComboBox tierFilter = new() { MinWidth = 150 };
    private readonly ComboBox categoryFilter = new() { MinWidth = 170 };
    private readonly CheckBox compatibleFilter = new CheckBox { VerticalAlignment = VerticalAlignment.Center }.Localize(ContentControl.ContentProperty, () => Strings.PluginsFilterCompatible);
    private readonly WrapPanel filters = new() { ItemSpacing = 8, LineSpacing = 8 };
    private readonly StackPanel results = new() { Spacing = 10 };
    private readonly DispatcherTimer rebuild = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private PluginPageView? page;
    private bool filling;

    /// <param name="compact">Whether the view sits in a narrow tool window rather than a page.</param>
    /// <param name="openSettings">Shows the plugin hub's settings.</param>
    /// <param name="localization">Says how plugins are translated into the language that shows; without it, the view says nothing about that.</param>
    public PluginManagerView(HubService hub, NotificationService notifications, OutputService output, bool compact, Action? openSettings, ShellLocalization? localization = null)
    {
        this.hub = hub;
        model = hub.Model;
        Translations = localization is null ? null : new PluginTranslations(localization, model);
        this.compact = compact;
        this.openSettings = openSettings;
        Notifications = notifications;
        Output = output;

        foreach (var name in (ReadOnlySpan<Func<string>>)[() => Strings.PluginsTabInstalled, () => Strings.PluginsTabUpdates, () => Strings.PluginsTabBrowse])
            tabs.Items.Add(new SegmentedControlItem { Padding = compact ? new Thickness(7, 0) : new Thickness(12, 0) }.Localize(ContentControl.ContentProperty, name));
        tabs.SelectedIndex = (int)model.Tab;
        tabs.SelectionChanged += (_, _) =>
        {
            if (!filling && tabs.SelectedIndex >= 0 && (tabs.SelectedIndex != (int)model.Tab || model.Page is not null))
                model.ShowTab((PluginManagerTab)tabs.SelectedIndex);
        };

        var refresh = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = HammerUI.Icons.Refresh, Size = 14 } };
        refresh.Localize(ToolTip.TipProperty, () => Strings.PluginsRefresh);
        refresh.Click += (_, _) => _ = hub.RefreshAsync();
        var statusLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { statusIcon, status, refresh } };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var title = new TextBlock { Classes = { "title" }, VerticalAlignment = VerticalAlignment.Center }.Localize(TextBlock.TextProperty, () => Strings.PluginsTitle);
        if (compact)
        {
            header.Children.Add(new StackPanel { Spacing = 8, Children = { statusLine, tabs } });
        }
        else
        {
            DockPanel.SetDock(statusLine, Dock.Right);
            header.Children.AddRange([statusLine, title]);
        }

        var top = new StackPanel { Children = { header } };
        if (!compact)
            top.Children.Add(tabs);
        DockPanel.SetDock(top, Dock.Top);

        var content = new StackPanel
        {
            MaxWidth = 880,
            Margin = compact ? new Thickness(10, 8) : new Thickness(40, 32, 40, 32),
            Children = { top, body },
        };
        body.Margin = new Thickness(0, 14, 0, 0);
        Children.Add(compact ? new ScrollViewer { Content = content } : content);

        search.PropertyChanged += (_, e) =>
        {
            if (e.Property == SearchBox.TextProperty)
                FillResults();
        };
        tierFilter.SelectionChanged += (_, _) => FillResults();
        categoryFilter.SelectionChanged += (_, _) => FillResults();
        compatibleFilter.IsCheckedChanged += (_, _) => FillResults();
        filters.Children.AddRange([tierFilter, categoryFilter, compatibleFilter]);

        rebuild.Tick += (_, _) =>
        {
            rebuild.Stop();
            Fill();
        };
        model.Changed += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            rebuild.Stop();
            rebuild.Start();
        });
        AttachedToVisualTree += (_, _) => hub.RefreshIfStale();
        Fill();
    }

    public NotificationService Notifications { get; }

    public OutputService Output { get; }

    /// <summary>Gets what to say about translations, or null when the view says nothing about them.</summary>
    public PluginTranslations? Translations { get; }

    /// <summary>The badge that says how much of a plugin the language that shows translates, or null when there is nothing to say.</summary>
    internal static Control? CoverageBadge(Func<string?> coverage) =>
        coverage() is null ? null : HubVisuals.LanguageBadge(() => coverage() ?? "", () => Strings.LocalizationCoverageTip);

    private void Fill()
    {
        filling = true;
        tabs.SelectedIndex = (int)model.Tab;
        var updates = model.Client.Status.HasCatalog ? model.Updates() : [];
        if (tabs.Items[1] is SegmentedControlItem item)
            item.Content = updates.Count == 0 ? Strings.PluginsTabUpdates : compact ? Strings.PluginsTabUpdatesCompact(updates.Count) : Strings.PluginsTabUpdatesCount(updates.Count);
        filling = false;
        FillStatus();

        body.Children.Clear();
        if (model.Page is { } target)
        {
            page = new PluginPageView(hub, this, model.Describe(target), target, compact);
            body.Children.Add(page);
            return;
        }

        page = null;
        switch (model.Tab)
        {
            case PluginManagerTab.Installed:
                FillInstalled();
                break;
            case PluginManagerTab.Updates:
                FillUpdates(updates);
                break;
            default:
                FillBrowse();
                break;
        }
    }

    private void FillStatus()
    {
        var current = model.Client.Status;
        var (icon, brush, text) = current.Kind switch
        {
            HubStatusKind.Verified => ("shield-check", "SuccessBrush", Strings.PluginsStatusVerified(WelcomePage.Ago(current.CheckedAt ?? DateTimeOffset.Now, DateTimeOffset.Now).ToLower(CultureInfo.CurrentCulture))),
            HubStatusKind.Checking => ("Refresh", "TextMutedBrush", Strings.PluginsStatusChecking),
            HubStatusKind.Offline => ("Info", "TextMutedBrush", Strings.PluginsStatusOffline),
            HubStatusKind.Unverifiable or HubStatusKind.VerificationFailed or HubStatusKind.RunesmithTooOld => ("AlertTriangle", "WarningBrush", Strings.PluginsStatusUnverifiable),
            HubStatusKind.TurnedOff => ("Info", "TextMutedBrush", Strings.PluginsStatusOff),
            _ => ("Info", "TextMutedBrush", Strings.PluginsStatusNotSetUp),
        };
        statusIcon.Data = HubVisuals.Icon(icon);
        HubVisuals.Themed(statusIcon, SymbolIcon.ForegroundProperty, brush);
        status.Text = text;
        ToolTip.SetTip(status, current.Describe());
    }

    private Control? StatusNotice()
    {
        var current = model.Client.Status;
        return current.Kind switch
        {
            HubStatusKind.Unverifiable or HubStatusKind.VerificationFailed or HubStatusKind.RunesmithTooOld => HubVisuals.Notice(PageNoticeKind.Warning, current.Describe()),
            HubStatusKind.Offline or HubStatusKind.NotSetUp => HubVisuals.Notice(PageNoticeKind.Quiet, current.Describe()),
            HubStatusKind.TurnedOff => HubVisuals.Notice(PageNoticeKind.Quiet, current.Describe(), actions: openSettings is null ? null : [SmallButton(Strings.PluginsSettingsButton, openSettings)]),
            _ => null,
        };
    }

    private void FillInstalled()
    {
        if (model.IsSafeMode)
        {
            body.Children.Add(HubVisuals.Notice(PageNoticeKind.Info, Strings.PluginsSafeModeTitle,
                Strings.PluginsSafeModeDetail,
                [SmallButton(Strings.PluginsRestartNormally, () => hub.Restart(), accent: true)], icon: "shield-check"));
        }

        if (StatusNotice() is { } notice)
            body.Children.Add(notice);

        foreach (var pluginNotice in model.Notices())
            body.Children.Add(Notice(pluginNotice));

        var rows = model.Installed();
        if (rows.Count == 0)
        {
            body.Children.Add(new EmptyState
            {
                Icon = HammerUI.Icons.Puzzle,
                Title = Strings.PluginsNoneInstalled,
                Hint = Strings.PluginsNoneInstalledHint,
                ActionText = Strings.PluginsBrowse,
                ActionCommand = new RelayCommand(() => model.ShowTab(PluginManagerTab.Browse)),
                Margin = new Thickness(0, 24),
            });
        }

        var list = new StackPanel { Spacing = 8 };
        foreach (var row in rows)
            list.Children.Add(InstalledRow(row));
        body.Children.Add(list);

        var tools = new WrapPanel { ItemSpacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        tools.Children.Add(SmallButton(Strings.PluginsOpenFolder, () =>
        {
            Directory.CreateDirectory(RunesmithPaths.UserPlugins);
            Launcher.Reveal(RunesmithPaths.UserPlugins);
        }));
        if (Directory.Exists(model.Client.Paths.Quarantine) && Directory.EnumerateFileSystemEntries(model.Client.Paths.Quarantine).Any())
            tools.Children.Add(SmallButton(Strings.PluginsEmptyQuarantine, () => model.Client.EmptyQuarantine()));
        if (openSettings is not null)
            tools.Children.Add(SmallButton(Strings.PluginsSettings, openSettings));
        body.Children.Add(tools);
    }

    private Control Notice(PluginNotice notice)
    {
        var actions = new List<Control>();
        switch (notice.Kind)
        {
            case NoticeKind.Quarantined or NoticeKind.Blocked:
                if (notice.UpdateTo is { } fix)
                    actions.Add(SmallButton(Strings.PluginsUpdateTo(fix.ToString()), () => _ = hub.InstallAsync(notice.PluginId, fix), accent: true));
                if (notice.Kind == NoticeKind.Quarantined)
                    actions.Add(SmallButton(notice.Dependents.Count == 0 ? Strings.PluginsRemoveOne(notice.Name) : Strings.PluginsRemoveWithDependents(notice.Name, string.Join(Strings.PluginsListSeparator, notice.Dependents)), () => _ = hub.RemoveBlockedAsync(notice)));
                else
                    actions.Add(SmallButton(Strings.PluginsRestartNow, () => hub.Restart(), accent: notice.UpdateTo is null));
                if (notice.Advisory is { } advisory)
                    actions.Add(InstallDialogs.Link(Strings.PluginsAdvisory, advisory));
                break;
            case NoticeKind.FilesChanged:
                actions.Add(SmallButton(Strings.PluginsReinstall, () => _ = hub.ReinstallAsync(notice.PluginId), accent: true));
                break;
            case NoticeKind.Yanked when notice.UpdateTo is { } newer:
                actions.Add(SmallButton(Strings.PluginsUpdateTo(newer.ToString()), () => _ = hub.InstallAsync(notice.PluginId, newer)));
                break;
            case NoticeKind.Deprecated when notice.Replacement is { } replacement:
                actions.Add(SmallButton(Strings.PluginsSeeReplacement, () => model.ShowPage(new PageTarget(replacement))));
                break;
        }

        var message = notice.Message;
        if (notice.Dependents.Count > 0)
            message = (message + " " + Strings.PluginsDependentsTurnedOff(dependents: string.Join(Strings.PluginsListSeparator, notice.Dependents), count: notice.Dependents.Count, name: notice.Name)).Trim();
        var kind = notice.IsDanger ? PageNoticeKind.Danger : notice.Kind == NoticeKind.Frozen ? PageNoticeKind.Info : PageNoticeKind.Quiet;
        var icon = notice.Kind switch
        {
            NoticeKind.Frozen => "pause",
            NoticeKind.Yanked => "undo",
            NoticeKind.Deprecated => "archive",
            NoticeKind.Deleted => "Trash",
            NoticeKind.FilesChanged => "AlertTriangle",
            _ => null,
        };
        return HubVisuals.Notice(kind, notice.Title, message, actions, icon);
    }

    private Button InstalledRow(InstalledRow row)
    {
        var record = model.Client.Catalog?.FindPlugin(row.Id);
        var icon = HubVisuals.PluginIcon(model.Client, row.Id, row.Name, row.Tier, record?.Icon.Size64, row.Folder, compact ? 26 : 32);
        var title = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
        title.Children.Add(new TextBlock { Text = row.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new TextBlock { Text = row.Version, Classes = { "caption", "mono" }, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(HubVisuals.TierBadge(row.Tier, local: row.Source == InstalledSource.Local));
        if (HubVisuals.StateBadge(row.Badge) is { } badge)
            title.Children.Add(badge);
        if (HubVisuals.LocalCopyBadge(row.LocalCopy) is { } copy)
            title.Children.Add(copy);
        if (Translations is { } translations && CoverageBadge(() => translations.Coverage(row)) is { } coverage)
            title.Children.Add(coverage);

        var note = new TextBlock { Text = row.Note, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap };
        if (row.IsProblem)
            HubVisuals.Themed(note, TextBlock.ForegroundProperty, row.Badge is NoticeKind.Blocked or NoticeKind.Quarantined or NoticeKind.FilesChanged ? "DangerBrush" : "WarningBrush");
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { title, note } };
        if (row.LanguagePack is { } pack)
        {
            text.Children.Add(new TextBlock { Classes = { "caption" }, TextWrapping = TextWrapping.Wrap, Name = "LanguagePackSummary" }
                .Localize(TextBlock.TextProperty, () => Translations?.Summary(row) ?? PluginTranslations.Language(pack)));
        }

        var toggle = new ToggleSwitch { IsChecked = row.IsEnabled, OnContent = null, OffContent = null, IsEnabled = row.CanToggle, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(toggle, row.CanToggle ? Strings.PluginsToggle : Strings.PluginsToggleLocked);
        toggle.IsCheckedChanged += (_, _) => hub.SetEnabled(row.Id, row.Name, toggle.IsChecked == true);

        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(toggle, Dock.Right);
        icon.Margin = new Thickness(0, 0, compact ? 10 : 12, 0);
        icon.VerticalAlignment = compact ? VerticalAlignment.Top : VerticalAlignment.Center;
        toggle.Margin = new Thickness(8, 0, 0, 0);
        line.Children.AddRange([icon, toggle, text]);

        var button = Card(line);
        ToolTip.SetTip(button, record is null ? null : Strings.PluginsOpenPage(row.Name));
        if (record is not null)
            button.Click += (_, _) => model.ShowPage(new PageTarget(row.Id));
        return button;
    }

    private void FillUpdates(IReadOnlyList<UpdateCandidate> updates)
    {
        if (StatusNotice() is { } notice)
            body.Children.Add(notice);

        if (updates.Count == 0)
        {
            body.Children.Add(new EmptyState
            {
                Icon = HubVisuals.Icon("shield-check"),
                Title = model.Client.Status.HasCatalog ? Strings.PluginsUpToDate : Strings.PluginsNoUpdates,
                Hint = model.Client.Status.HasCatalog ? Strings.PluginsUpToDateHint : model.Client.Status.Describe(),
                Margin = new Thickness(0, 24),
            });
            return;
        }

        var all = SmallButton(Strings.PluginsUpdateAll, () => _ = hub.UpdateAllAsync(), accent: true);
        all.IsEnabled = model.Client.Status.CanInstall;
        var bar = new DockPanel();
        DockPanel.SetDock(all, Dock.Right);
        bar.Children.AddRange([all, new TextBlock
        {
            Text = Strings.PluginsUpdatesSummary(updates.Count),
            Classes = { "secondary" },
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        }]);
        body.Children.Add(bar);
        foreach (var update in updates)
            body.Children.Add(UpdateRow(update));
    }

    private Border UpdateRow(UpdateCandidate update)
    {
        var record = model.Client.Catalog?.FindPlugin(update.PluginId);
        var pending = model.Client.State.IsPending(update.PluginId) && model.Client.State.Find(update.PluginId)?.Version == update.To.Version.ToString();
        var icon = HubVisuals.PluginIcon(model.Client, update.PluginId, update.Name, update.Tier, record?.Icon.Size64, null, 32);
        var title = new WrapPanel { ItemSpacing = 8, LineSpacing = 4 };
        title.Children.Add(new TextBlock { Text = update.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new TextBlock { Text = Strings.PluginsVersionChange(update.From.ToString(), update.To.Version.ToString()), Classes = { "caption", "mono" }, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(HubVisuals.TierBadge(update.Tier));

        var (subtitle, brush) = update.Kind switch
        {
            UpdateKind.Automatic => (Strings.PluginsUpdateAutomatic, "TextMutedBrush"),
            UpdateKind.NeedsReview => (string.Join(" ", update.Reasons) + " " + Strings.PluginsUpdateNeedsApproval, "WarningBrush"),
            _ when update.Tier == PluginTier.Unverified => (Strings.PluginsUpdateUnverified, "TextMutedBrush"),
            _ => (Strings.PluginsUpdateOff, "TextMutedBrush"),
        };
        var note = HubVisuals.Themed(new TextBlock { Text = subtitle, Classes = { "caption" }, TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, brush);

        Control action = pending
            ? HubVisuals.StateBadge(Strings.PluginsBadgeRestart, "Check", "SuccessBrush")
            : SmallButton(update.Kind == UpdateKind.NeedsReview ? Strings.PluginsReviewUpdate : Strings.PluginsUpdate, () => _ = hub.UpdateAsync(update), accent: update.Kind == UpdateKind.NeedsReview);
        action.IsEnabled = pending || model.Client.Status.CanInstall;
        action.VerticalAlignment = VerticalAlignment.Center;

        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(action, Dock.Right);
        icon.Margin = new Thickness(0, 0, 12, 0);
        action.Margin = new Thickness(12, 0, 0, 0);
        var text = new StackPanel { Spacing = 2, Children = { title, note } };
        if (update.NewCapabilities.Count > 0)
        {
            foreach (var capability in update.To.Capabilities.Declared.Where(c => update.NewCapabilities.Contains(c.Id)))
                text.Children.Add(HubVisuals.Capability(new CapabilityRow(capability.Id, model.Client.CapabilityLabel(capability.Id), capability.Reason, update.Name, true, false), showFrom: false));
        }

        if (!string.IsNullOrWhiteSpace(update.To.ReleaseNotes))
        {
            text.Children.Add(new Expander
            {
                Header = Strings.PluginsReleaseNotes,
                Content = new SelectableTextBlock { Text = update.To.ReleaseNotes, TextWrapping = TextWrapping.Wrap },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 6, 0, 0),
            });
        }

        line.Children.AddRange([icon, action, text]);
        return Surface(line);
    }

    private void FillBrowse()
    {
        if (!model.Client.Status.HasCatalog || model.Client.Catalog is null)
        {
            body.Children.Add(new EmptyState { Icon = HammerUI.Icons.Puzzle, Title = Strings.PluginsCatalogUnavailable, Hint = model.Client.Status.Describe(), Margin = new Thickness(0, 24) });
            return;
        }

        FillFilters();
        body.Children.Add(search);
        body.Children.Add(filters);
        if (StatusNotice() is { } notice)
            body.Children.Add(notice);
        body.Children.Add(results);
        FillResults();
    }

    private void FillFilters()
    {
        filling = true;
        var allowed = model.Preferences.AllowedTiers;
        var tier = (tierFilter.SelectedItem as ComboBoxItem)?.Tag;
        tierFilter.Items.Clear();
        tierFilter.Items.Add(new ComboBoxItem { Content = Strings.PluginsFilterAllTiers });
        foreach (var value in Enum.GetValues<PluginTier>().Where(t => RunesmithHub.Protocol.Catalog.AllowedTiersExtensions.Allows(allowed, t)))
            tierFilter.Items.Add(new ComboBoxItem { Content = PluginPageModel.TierName(value), Tag = value });
        tierFilter.SelectedItem = tierFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, tier)) ?? tierFilter.Items[0];

        var category = (categoryFilter.SelectedItem as ComboBoxItem)?.Tag;
        categoryFilter.Items.Clear();
        categoryFilter.Items.Add(new ComboBoxItem { Content = Strings.PluginsFilterAllCategories });
        foreach (var name in model.Client.Categories)
            categoryFilter.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        categoryFilter.SelectedItem = categoryFilter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, category)) ?? categoryFilter.Items[0];
        filling = false;
    }

    private void FillResults()
    {
        if (filling || model.Tab != PluginManagerTab.Browse || model.Page is not null)
            return;

        results.Children.Clear();
        var query = new CatalogQuery(search.Text ?? "", (tierFilter.SelectedItem as ComboBoxItem)?.Tag as PluginTier?, (categoryFilter.SelectedItem as ComboBoxItem)?.Tag as string,
            compatibleFilter.IsChecked == true);
        var found = model.Browse(query);
        if (found.HiddenByTierSetting > 0)
        {
            results.Children.Add(HubVisuals.Notice(PageNoticeKind.Quiet, Strings.PluginsHiddenByTiers(found.HiddenByTierSetting),
                actions: openSettings is null ? null : [SmallButton(Strings.PluginsChange, openSettings)]));
        }

        if (found.Plugins.Count == 0)
        {
            results.Children.Add(new EmptyState { Icon = HammerUI.Icons.Search, Title = Strings.PluginsNoMatch, Hint = Strings.PluginsNoMatchHint, Margin = new Thickness(0, 24) });
            return;
        }

        var grid = new UniformGrid { Columns = compact ? 1 : 2 };
        foreach (var plugin in found.Plugins)
            grid.Children.Add(PluginCard(plugin));
        results.Children.Add(grid);
    }

    private Button PluginCard(PluginRecord plugin)
    {
        var catalog = model.Client.Catalog!;
        var installable = catalog.GetInstallableVersions(plugin.Id, model.Client.Host, model.Preferences.AllowedTiers) is [var version, ..] ? version : null;
        var newest = installable?.Version.ToString();
        var icon = HubVisuals.PluginIcon(model.Client, plugin.Id, plugin.Name, plugin.Tier, plugin.Icon.Size64, null, 40);
        var title = new WrapPanel { ItemSpacing = 6, LineSpacing = 4 };
        title.Children.Add(new TextBlock { Text = plugin.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(HubVisuals.TierBadge(plugin.Tier));
        if (plugin.Versions is [var latest, ..] && catalog.GetEffectiveState(plugin, latest).State == VersionState.Frozen)
            title.Children.Add(HubVisuals.StateBadge(NoticeKind.Frozen)!);
        if (plugin.State == RunesmithHub.Protocol.Index.PluginState.Deprecated)
            title.Children.Add(HubVisuals.StateBadge(NoticeKind.Deprecated)!);
        var shown = installable ?? (plugin.Versions is [var first, ..] ? first : null);
        if (shown?.LanguagePack is { } pack)
            title.Children.Add(HubVisuals.LanguageBadge(() => PluginTranslations.Language(pack)));
        else if (Translations is { } translations && CoverageBadge(() => translations.Coverage(plugin, shown)) is { } coverage)
            title.Children.Add(coverage);

        var publisher = catalog.FindPublisher(plugin.Publisher)?.DisplayName ?? plugin.Publisher;
        var installed = model.RunningVersion(plugin.Id) is { } running ? " · " + Strings.PluginsInstalledVersion(running) : "";
        var text = new StackPanel
        {
            Spacing = 3,
            Children =
            {
                title,
                new TextBlock { Text = $"{publisher}{(newest is null ? "" : " · " + newest)}{installed}", Classes = { "caption" }, TextTrimming = TextTrimming.CharacterEllipsis },
                new TextBlock { Text = plugin.Summary, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.WordEllipsis, Classes = { "secondary" } },
            },
        };
        var line = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        icon.Margin = new Thickness(0, 0, 12, 0);
        icon.VerticalAlignment = VerticalAlignment.Top;
        line.Children.AddRange([icon, text]);
        var card = Card(line);
        card.Margin = new Thickness(0, 0, compact ? 0 : 10, 10);
        card.VerticalAlignment = VerticalAlignment.Stretch;
        card.VerticalContentAlignment = VerticalAlignment.Top;
        card.Click += (_, _) => model.ShowPage(new PageTarget(plugin.Id));
        return card;
    }

    /// <summary>A clickable card in the style of the hub's lists.</summary>
    internal static Button Card(Control content)
    {
        var button = new Button
        {
            Classes = { "subtle" },
            Padding = new Thickness(12, 10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Content = content,
        };
        HubVisuals.Themed(button, Button.BorderBrushProperty, "BorderSubtleBrush");
        HubVisuals.Themed(button, Button.BackgroundProperty, "Surface0Brush");
        return button;
    }

    /// <summary>A card that is not clickable.</summary>
    internal static Border Surface(Control content)
    {
        var border = new Border { Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Child = content };
        HubVisuals.Themed(border, Border.BorderBrushProperty, "BorderSubtleBrush");
        return HubVisuals.Themed(border, Border.BackgroundProperty, "Surface0Brush");
    }

    internal static Button SmallButton(string text, Action click, bool accent = false) => SmallButton(new TextBlock { Text = text }, click, accent);

    /// <summary>A small button whose text follows the language.</summary>
    internal static Button SmallButton(Func<string> text, Action click, bool accent = false) =>
        SmallButton(new TextBlock().Localize(TextBlock.TextProperty, text), click, accent);

    private static Button SmallButton(TextBlock text, Action click, bool accent)
    {
        text.TextWrapping = TextWrapping.Wrap;
        var button = new Button { Content = text, Classes = { "small" } };
        if (accent)
            button.Classes.Add("accent");
        button.Click += (_, _) => click();
        return button;
    }
}
