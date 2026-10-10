using System.Composition;
using System.Reflection;
using Avalonia.Threading;
using Runesmith.Composition;
using Runesmith.Hub;
using Runesmith.Hub.Enforcement;
using Runesmith.Hub.Installing;
using Runesmith.Hub.Links;
using Runesmith.Hub.Network;
using Runesmith.Hub.Updates;
using Runesmith.Sdk;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Services;
using Runesmith.Workspace.Settings;
using RunesmithHub.Protocol.Catalog;
using RunesmithHub.Protocol.Resolution;
using RunesmithHub.Protocol.Updating;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Shell.Hub;

/// <summary>Runs the plugin hub inside the window: checks the hub after the window shows and at the interval the settings ask for, applies
/// updates that may happen by themselves, turns blocked plugins off, opens <c>runesmith://</c> links and walks the user through installs.</summary>
[Export]
[Shared]
public sealed class HubService
{
    /// <summary>The id of the Plugins tool window.</summary>
    public const string ToolWindowId = "plugins";

    /// <summary>The output channel the hub logs to.</summary>
    public const string Channel = "Plugin hub";

    private readonly ISettingsService settings;
    private readonly NotificationService notifications;
    private readonly OutputService output;
    private readonly RuntimeInfo runtime;
    private readonly AppRestart restart;
    private readonly LinkThrottle throttle = new(TimeProvider.System);
    private readonly HashSet<string> announced = new(StringComparer.Ordinal);
    private readonly DispatcherTimer timer = new();
    private Task? refreshing;
    private DateTimeOffset lastRefresh;
    private bool started;

    [ImportingConstructor]
    public HubService(ISettingsService settings, NotificationService notifications, OutputService output, RuntimeInfo runtime, AppRestart restart)
    {
        this.settings = settings;
        this.notifications = notifications;
        this.output = output;
        this.runtime = runtime;
        this.restart = restart;
        Version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.1.0";
        var http = HubHttp.Create(Version);
        Client = new HubClient(
            HubConfiguration.Load(AppContext.BaseDirectory),
            new HubPaths(RunesmithPaths.Hub),
            new RunesmithHost(PluginDiscovery.ApiVersion, PluginDiscovery.OldestSupportedApiVersion, Platform()),
            Version,
            new HttpFileDownloader(http),
            url => new HttpIndexFetcher(http, url),
            TimeProvider.System);
        Client.SetEnabled(settings.Get<bool>(HubSettings.Enabled));
        Model = new PluginManagerModel(Client, () => HubSettings.Preferences(settings), () => runtime.Plugins, Disabled, () => runtime.HubStartup, runtime.IsSafeMode);
        Client.Changed += (_, _) => UiThread.Run(Model.Refresh);
        timer.Tick += (_, _) => _ = RefreshAsync();
        settings.Changed += (_, e) => UiThread.Run(() => OnSettingChanged(e.Key));
    }

    /// <summary>Raised on the UI thread when something asks to see the plugin manager, such as a link.</summary>
    public event EventHandler? ShowRequested;

    public HubClient Client { get; }

    public PluginManagerModel Model { get; }

    /// <summary>Gets Runesmith's version, for the hub's requests and reports.</summary>
    public string Version { get; }

    public bool CanRestart => restart.CanRestart;

    /// <summary>Starts checking the hub; call it once the window shows.</summary>
    public void Start()
    {
        if (started)
            return;

        started = true;
        timer.Interval = TimeSpan.FromHours(settings.Get<int>(HubSettings.CheckInterval));
        timer.Start();
        ReportStartup();
        _ = RefreshAsync();
    }

    /// <summary>Checks the hub, unless a check is running already.</summary>
    public Task RefreshAsync()
    {
        if (refreshing is { IsCompleted: false })
            return refreshing;
        refreshing = RefreshCoreAsync();
        return refreshing;
    }

    /// <summary>Checks the hub when the last check is more than a minute old, as when the plugin manager opens.</summary>
    public void RefreshIfStale()
    {
        if (started && DateTimeOffset.UtcNow - lastRefresh > TimeSpan.FromMinutes(1))
            _ = RefreshAsync();
    }

    /// <summary>Opens what a <c>runesmith://</c> link names: a plugin's page or the updates. A link never installs anything.</summary>
    public void OpenLink(string text)
    {
        if (!HubLink.TryParse(text, out var link, out var error))
        {
            output.GetChannel(Channel).AppendLine($"Refused a link: {error}");
            notifications.Notify(NotificationKind.Warning, "Runesmith refused a link", error);
            return;
        }

        var ignored = throttle.Arrive();
        if (link is PluginLink plugin)
            Model.ShowPage(new PageTarget(plugin.PluginId, plugin.Version, plugin.ToString(), ignored));
        else
            Model.ShowTab(PluginManagerTab.Updates);
        ShowRequested?.Invoke(this, EventArgs.Empty);
        RefreshIfStale();
    }

    /// <summary>Shows the plugin manager.</summary>
    public void Show(PluginManagerTab? tab = null)
    {
        if (tab is { } section)
            Model.ShowTab(section);
        ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets the ids of the plugins the user turned off.</summary>
    public IReadOnlySet<string> Disabled() =>
        settings.Get<string>(ShellSettings.DisabledPlugins).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Turns a plugin on or off from the next start.</summary>
    public void SetEnabled(string id, string name, bool enabled)
    {
        var disabled = Disabled().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (enabled)
            disabled.Remove(id);
        else
            disabled.Add(id);
        settings.Set(ShellSettings.DisabledPlugins, string.Join(';', disabled.Order(StringComparer.Ordinal)));
        PromptRestart($"{name} is turned {(enabled ? "on" : "off")} from the next start.");
        Model.Refresh();
    }

    /// <summary>Resolves installing a plugin and walks the user through the plan.</summary>
    public Task InstallAsync(string id, SemanticVersion? version) => ResolveAndReviewAsync(ResolutionRequest.Install(id, version), $"{Name(id)} can't be installed");

    public Task UpdateAsync(UpdateCandidate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        return update.Result.Succeeded ? ReviewAsync(update.Result.Plan) : ShowFailureAsync($"{update.Name} can't be updated", update.Result.Failure!);
    }

    public Task UpdateAllAsync() => ResolveAndReviewAsync(ResolutionRequest.UpdateAll(), "The plugins can't be updated");

    public Task UninstallAsync(string id) => ResolveAndReviewAsync(ResolutionRequest.Remove(id), $"{Name(id)} can't be removed");

    /// <summary>Removes a blocked plugin's dependents and forgets the blocked plugin, whose files are in quarantine already.</summary>
    public async Task RemoveBlockedAsync(PluginNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (notice.DependentIds.Count > 0)
        {
            var result = Client.Resolve(ResolutionRequest.Remove(notice.DependentIds), Model.Preferences);
            if (!result.Succeeded || !await ReviewAsync(result.Plan))
                return;
        }

        Client.Dismiss(notice.PluginId);
    }

    /// <summary>Installs the same version of a plugin again, after its files changed.</summary>
    public async Task ReinstallAsync(string id)
    {
        if (Client.ReinstallPlan(id) is { } plan)
            await ReviewAsync(plan);
        else
            await ShowMessageAsync($"{Name(id)} can't be reinstalled", Client.Status.CanInstall ? "The hub no longer offers its version." : Client.Status.Describe());
    }

    /// <summary>Shows a plan and, when the user confirms it, applies it with progress and offers to restart.</summary>
    /// <returns>Whether the plan was applied.</returns>
    public async Task<bool> ReviewAsync(InstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var model = Model.Plan(plan);
        if (!model.HasChanges)
            return false;
        if (!await InstallDialogs.ConfirmAsync(notifications.Dialogs, model, Client))
            return false;
        return await ApplyAsync(model);
    }

    /// <summary>Restarts Runesmith, or tells the user to when the application cannot.</summary>
    public void Restart(bool safeMode = false)
    {
        if (restart.CanRestart)
            restart.Restart(safeMode);
        else
            notifications.Notify(NotificationKind.Info, "Restart Runesmith", "Close Runesmith and start it again to apply the changes.");
    }

    /// <summary>Offers to restart, for changes that take effect at the next start.</summary>
    public void PromptRestart(string message) =>
        notifications.Notify(NotificationKind.Info, "Restart to apply", message, restart.CanRestart ? "Restart now" : null, restart.CanRestart ? () => Restart() : null);

    private async Task ResolveAndReviewAsync(ResolutionRequest request, string failureTitle)
    {
        if (!Client.Status.CanInstall)
            await RefreshAsync();

        var result = Client.Resolve(request, Model.Preferences);
        if (result.Succeeded)
            await ReviewAsync(result.Plan);
        else
            await ShowFailureAsync(failureTitle, result.Failure!);
    }

    private async Task<bool> ApplyAsync(InstallPlanModel model)
    {
        var progress = new InstallDialogs.Progress(model);
        _ = notifications.Dialogs.ShowAsync(progress.Dialog);
        try
        {
            await Client.ApplyAsync(model.Plan, new Progress<InstallProgress>(progress.Report));
        }
        catch (InstallException exception)
        {
            output.GetChannel(Channel).AppendLine($"{model.Title} failed: {exception.Message} {exception.Detail ?? exception.InnerException?.Message}");
            progress.Close();
            await ShowMessageAsync($"{model.Title} failed", exception.Message);
            return false;
        }

        progress.Close();
        output.GetChannel(Channel).AppendLine($"{model.Title}: {string.Join(", ", model.Rows.Select(r => $"{r.Name} {r.Change}"))}. Takes effect at the next start.");
        var names = Names(model.Rows.Select(r => r.Name).ToList());
        await InstallDialogs.RestartAsync(notifications.Dialogs, model.Rows.All(r => r.Kind == OperationKind.Remove) ? $"{names} {(model.Rows.Count == 1 ? "is" : "are")} removed when Runesmith restarts." : $"{names} {(model.Rows.Count == 1 ? "takes" : "take")} effect after a restart.", restart.CanRestart, () => Restart());
        return true;
    }

    private Task ShowFailureAsync(string title, ResolutionFailure failure) => InstallDialogs.FailureAsync(notifications.Dialogs, title, failure);

    private Task<object?> ShowMessageAsync(string title, string message) => notifications.Dialogs.ShowMessageAsync(title, message, [new HammerUI.Services.MessageDialogButton("OK", null, IsDefault: true, IsCancel: true)]);

    private async Task RefreshCoreAsync()
    {
        HubStatus status;
        try
        {
            status = await Client.RefreshAsync();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            output.GetChannel(Channel).AppendLine($"The hub could not be checked: {exception.Message}");
            return;
        }

        lastRefresh = DateTimeOffset.UtcNow;
        if (status.Detail is { } detail)
            output.GetChannel(Channel).AppendLine($"{status.Describe()} {detail}");

        await Dispatcher.UIThread.InvokeAsync(AfterRefreshAsync);
    }

    private async Task AfterRefreshAsync()
    {
        var status = Client.Status;
        if (status.Kind is HubStatusKind.Unverifiable or HubStatusKind.RunesmithTooOld && announced.Add("status:" + status.Kind))
            notifications.Notify(NotificationKind.Warning, "Plugin safety can't be confirmed", status.Describe(), "Show plugins", () => Show(PluginManagerTab.Installed));

        var loaded = runtime.Plugins.Where(p => p.State == PluginState.Loaded).Select(p => p.Manifest.Id).ToHashSet(StringComparer.Ordinal);
        var blocked = Client.Blocked().Where(p => loaded.Contains(p.Id) && announced.Add("blocked:" + p.Id + "@" + p.Version)).ToList();
        if (blocked.Count > 0)
            await InstallDialogs.BlockedAsync(notifications.Dialogs, Client.Notices(runtime.HubStartup.ChangedFiles, Model.Preferences).Where(n => n.Kind == NoticeKind.Blocked && blocked.Any(b => b.Id == n.PluginId)).ToList(), restart.CanRestart, () => Restart());

        if (!status.CanInstall || runtime.IsSafeMode)
            return;

        var updates = Model.Updates();
        var automatic = updates.Where(u => u.Kind == UpdateKind.Automatic && u.Result.Succeeded && !Client.State.IsPending(u.PluginId)).ToList();
        var applied = new List<string>();
        foreach (var update in automatic)
        {
            try
            {
                await Client.ApplyAsync(update.Result.Plan!);
                applied.Add($"{update.Name} {update.To.Version}");
            }
            catch (InstallException exception)
            {
                output.GetChannel(Channel).AppendLine($"The automatic update of {update.Name} failed: {exception.Message} {exception.Detail}");
            }
        }

        if (applied.Count > 0)
        {
            output.GetChannel(Channel).AppendLine($"Updated by themselves: {string.Join(", ", applied)}.");
            notifications.Notify(NotificationKind.Success, "Plugins updated", $"{Names(applied)} {(applied.Count == 1 ? "takes" : "take")} effect after a restart.",
                restart.CanRestart ? "Restart now" : null, restart.CanRestart ? () => Restart() : null);
        }

        var waiting = updates.Where(u => u.Kind != UpdateKind.Automatic && announced.Add($"update:{u.PluginId}@{u.To.Version}")).ToList();
        if (waiting.Count > 0)
        {
            notifications.Notify(NotificationKind.Info, waiting.Count == 1 ? $"An update for {waiting[0].Name} is available" : $"{waiting.Count} plugin updates are available",
                "Unverified plugins and updates that change what a plugin may do wait for you.", "Show updates", () => Show(PluginManagerTab.Updates));
        }
    }

    private void ReportStartup()
    {
        var report = runtime.HubStartup;
        var channel = output.GetChannel(Channel);
        foreach (var problem in report.Problems)
            channel.AppendLine(problem);
        foreach (var (id, files) in report.ChangedFiles)
            channel.AppendLine($"{id} is off because its files changed: {string.Join(" ", files)}");
        if (report.ChangedFiles.Count > 0 || report.Quarantined.Count > 0)
        {
            notifications.Notify(NotificationKind.Error, report.Quarantined.Count > 0 ? "A plugin was disabled because Runesmith found it harmful" : "A plugin's files changed",
                "The plugin manager says which, and what to do.", "Show plugins", () => Show(PluginManagerTab.Installed));
        }

        foreach (var (kind, title, message) in PluginManagerModel.LocalCopyNotices(runtime.Plugins))
            notifications.Notify(kind, title, message, "Show plugins", () => Show(PluginManagerTab.Installed));
    }

    private void OnSettingChanged(string key)
    {
        switch (key)
        {
            case HubSettings.Enabled:
                Client.SetEnabled(settings.Get<bool>(HubSettings.Enabled));
                PromptRestart(settings.Get<bool>(HubSettings.Enabled) ? "Plugins from the hub load from the next start." : "Plugins from the hub stop loading from the next start.");
                if (settings.Get<bool>(HubSettings.Enabled))
                    _ = RefreshAsync();
                break;
            case HubSettings.CheckInterval:
                timer.Interval = TimeSpan.FromHours(settings.Get<int>(HubSettings.CheckInterval));
                break;
            case CoreSettings.AllowLocalOverrides:
                PromptRestart(settings.Get<bool>(CoreSettings.AllowLocalOverrides)
                    ? "Local copies of plugins run in place of the installed ones from the next start."
                    : "Local copies of installed plugins stop loading from the next start.");
                break;
            case HubSettings.AllowedTiers or HubSettings.PreReleases or ShellSettings.DisabledPlugins:
                Model.Refresh();
                break;
        }
    }

    private string Name(string id) => Client.Catalog?.FindPlugin(id)?.Name ?? id;

    private static string Names(List<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    private static string? Platform() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : OperatingSystem.IsLinux() ? "linux" : null;
}
