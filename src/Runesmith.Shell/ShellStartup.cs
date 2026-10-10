using System.Diagnostics;
using Avalonia.Threading;
using Microsoft.VisualStudio.Composition;
using Runesmith.Composition;
using Runesmith.Languages.Servers;
using Runesmith.Sdk.Plugins;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Pages;
using Runesmith.Shell.Services;
using Runesmith.Shell.Session;
using Runesmith.Shell.ToolWindows;
using Runesmith.Shell.Views;

namespace Runesmith.Shell;

/// <summary>What Runesmith was started with.</summary>
/// <param name="Paths">Folders and files to open, files optionally with <c>:line:column</c>.</param>
/// <param name="IsDiagnosticsEnabled">Whether <c>--diagnostics</c> was given.</param>
public sealed record StartupOptions(IReadOnlyList<string> Paths, string WorkingDirectory, bool IsDiagnosticsEnabled)
{
    /// <summary>Gets whether Runesmith runs in safe mode, which loads no plugins.</summary>
    public bool IsSafeMode { get; init; }

    /// <summary>Gets what the plugin hub decided before plugins loaded.</summary>
    public Runesmith.Hub.Enforcement.HubStartupReport HubStartup { get; init; } = Runesmith.Hub.Enforcement.HubStartupReport.Empty;

    /// <summary>Gets a <c>runesmith://</c> link to open once the window shows.</summary>
    public string? Link { get; init; }
}

/// <summary>Creates the main window from the composed parts and does the work that follows once it shows.</summary>
public static class ShellStartup
{
    /// <summary>Creates the main window; call <see cref="OnShownAsync"/> once it shows.</summary>
    public static MainWindow CreateMainWindow(CompositionResult composition, StartupOptions options, IReadOnlyList<(string Step, TimeSpan Duration)> timings)
    {
        ArgumentNullException.ThrowIfNull(composition);
        var exports = composition.Exports;
        var runtime = exports.GetExportedValue<RuntimeInfo>();
        runtime.Plugins = composition.Plugins;
        runtime.CompositionErrors = composition.Errors;
        runtime.IsDiagnosticsEnabled = options.IsDiagnosticsEnabled;
        runtime.IsSafeMode = options.IsSafeMode;
        runtime.HubStartup = options.HubStartup;
        runtime.Timings.AddRange(timings);

        var stopwatch = Stopwatch.StartNew();
        exports.GetExportedValue<ThemeService>();
        var window = new MainWindow(exports);
        runtime.Timings.Add(("Create the main window", stopwatch.Elapsed));
        return window;
    }

    /// <summary>Starts the language servers and plugins, reports problems from startup and opens what the command line names, or the last
    /// folder.</summary>
    public static async Task OnShownAsync(ExportProvider exports, StartupOptions options)
    {
        ArgumentNullException.ThrowIfNull(exports);
        var runtime = exports.GetExportedValue<RuntimeInfo>();
        var notifications = exports.GetExportedValue<INotificationService>();
        var output = exports.GetExportedValue<IOutputService>();
        var stopwatch = Stopwatch.StartNew();

        exports.GetExportedValue<LanguageServerManager>();
        exports.GetExportedValue<Editors.KeyHookPolicy>();
        exports.GetExportedValue<Languages.Analyzers.AnalyzerBridge>();
        exports.GetExportedValue<ProblemsToolWindow>().ShowCounts();
        exports.GetExportedValue<PerformanceMonitor>();
        ReportStartupProblems(runtime, notifications, output);
        await InitializePluginsAsync(exports, notifications, output);
        runtime.Timings.Add(("Start language services and plugins", stopwatch.Elapsed));

        stopwatch.Restart();
        var workbench = exports.GetExportedValue<Workbench>();
        if (options.Paths.Count > 0)
            await workbench.OpenArgumentsAsync(options.Paths, options.WorkingDirectory);
        else if (exports.GetExportedValue<ISettingsService>().Get<bool>(SettingKeys.RestoreSession) && SessionStore.LoadWindow()?.LastFolder is { } last && Directory.Exists(last))
            await workbench.OpenFolderAsync(last);
        runtime.Timings.Add(("Open the folder and files", stopwatch.Elapsed));

        var hub = exports.GetExportedValue<Hub.HubService>();
        hub.Start();
        if (options.Link is { } link)
            hub.OpenLink(link);

        if (runtime.IsDiagnosticsEnabled)
            WriteDiagnostics(runtime, output);
    }

    private static void ReportStartupProblems(RuntimeInfo runtime, INotificationService notifications, IOutputService output)
    {
        var failed = runtime.Plugins.Where(p => p.State == PluginState.Failed).ToList();
        if (failed.Count == 0 && runtime.CompositionErrors.Count == 0)
            return;

        var channel = output.GetChannel("Plugins");
        foreach (var plugin in failed)
            channel.AppendLine($"{plugin.Manifest.Name} ({plugin.Directory}) could not load: {plugin.Error}");
        foreach (var error in runtime.CompositionErrors)
            channel.AppendLine(error);

        var title = failed.Count == 1 ? $"The plugin {failed[0].Manifest.Name} could not load" : failed.Count > 1 ? $"{failed.Count} plugins could not load" : "Some extensions could not load";
        notifications.Notify(NotificationKind.Warning, title, "The Plugins output says why.", "Show output", channel.Show);
    }

    private static async Task InitializePluginsAsync(ExportProvider exports, INotificationService notifications, IOutputService output)
    {
        IEnumerable<Lazy<IPlugin>> plugins;
        try
        {
            plugins = exports.GetExports<IPlugin>();
        }
        catch (CompositionFailedException exception)
        {
            output.GetChannel("Plugins").AppendLine(exception.Message);
            return;
        }

        foreach (var plugin in plugins)
        {
            try
            {
                await plugin.Value.InitializeAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                var channel = output.GetChannel("Plugins");
                channel.AppendLine($"A plugin failed to start: {exception}");
                notifications.Notify(NotificationKind.Error, "A plugin failed to start", exception.Message, "Show output", channel.Show);
            }
        }
    }

    private static void WriteDiagnostics(RuntimeInfo runtime, IOutputService output)
    {
        var channel = output.GetChannel("Diagnostics");
        channel.AppendLine($"Runesmith started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");
        foreach (var (step, duration) in runtime.Timings)
            channel.AppendLine($"  {duration.TotalMilliseconds,8:0.0} ms  {step}");
        channel.AppendLine($"Plugins ({runtime.Plugins.Count}):");
        foreach (var plugin in runtime.Plugins)
            channel.AppendLine($"  {plugin.Manifest.Id} {plugin.Manifest.Version} ({plugin.Source}{(plugin.Manifest.IsLegacy ? ", older manifest" : "")}): {plugin.State}{(plugin.Error is null ? "" : " - " + plugin.Error)}");
        channel.AppendLine($"Managed memory: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
        Dispatcher.UIThread.Post(channel.Show, DispatcherPriority.Background);
    }
}
