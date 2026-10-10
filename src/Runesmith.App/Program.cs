using System.Diagnostics;
using Avalonia;
using Runesmith.Composition;
using Runesmith.Hub;
using Runesmith.Hub.Enforcement;
using Runesmith.Sdk;
using Runesmith.Shell.Services;

namespace Runesmith.App;

internal static class Program
{
    /// <summary>Gets the command line Runesmith started with.</summary>
    public static CommandLine Arguments { get; private set; } = CommandLine.Parse([]);

    /// <summary>Gets the composition, which starts while Avalonia starts.</summary>
    public static Task<CompositionResult> Composition { get; private set; } = null!;

    /// <summary>Gets how long the steps before the window took.</summary>
    public static List<(string Step, TimeSpan Duration)> Timings { get; } = [];

    public static SingleInstance? Instance { get; private set; }

    /// <summary>Gets how the last starts went, and whether this one offers safe mode.</summary>
    public static StartupGuard Guard { get; private set; } = null!;

    /// <summary>Gets whether this start is in safe mode, which loads no plugins.</summary>
    public static bool IsSafeMode { get; private set; }

    /// <summary>Gets what the plugin hub decided before plugins loaded.</summary>
    public static HubStartupReport HubReport { get; private set; } = HubStartupReport.Empty;

    /// <summary>Gets or sets the arguments to start Runesmith with again once it closes, or null to not restart.</summary>
    public static IReadOnlyList<string>? RestartArguments { get; set; }

    [STAThread]
    public static int Main(string[] args)
    {
        Arguments = CommandLine.Parse(args);
        if (Arguments.Error is not null || Arguments.ShowHelp)
        {
            if (Arguments.Error is not null)
                Console.Error.WriteLine(Arguments.Error);
            Console.WriteLine(CommandLine.Usage);
            return Arguments.Error is null ? 0 : 2;
        }

        if (Arguments.ShowVersion)
        {
            Console.WriteLine($"Runesmith {typeof(Program).Assembly.GetName().Version?.ToString(3)}");
            return 0;
        }

        if (!Arguments.IsNewInstance && SingleInstance.TryHandOver(Arguments.Paths, Environment.CurrentDirectory, Arguments.Link))
            return 0;

        Guard = StartupGuard.Begin(Path.Combine(RunesmithPaths.State, "startups.json"), TimeProvider.System, Arguments.IsSafeMode);
        IsSafeMode = Arguments.IsSafeMode || Guard.Offer is not null;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                CrashLog.Write(exception, "Runesmith stopped because of an error");
                Guard.Crashed(exception, assembly => PluginCallers.Of(assembly) is { } plugin ? new SuspectPlugin(plugin.Manifest.Id, plugin.Manifest.Name, plugin.Manifest.Version) : null);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception, "A background task failed");
            e.SetObserved();
        };

        if (Environment.ProcessPath is { } executable && !IsDotnetHost(executable))
            LinkRegistration.Apply(StartupSettings.RegisterLinks(), executable);

        Instance = Arguments.IsNewInstance ? null : new SingleInstance();
        Composition = Task.Run(ComposeAsync);
        int exitCode;
        try
        {
            exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Instance?.Dispose();
            if (Composition.IsCompletedSuccessfully)
                Composition.Result.Dispose();
        }

        Guard.Exited();
        if (RestartArguments is { } restart)
            StartAgain(restart);
        return exitCode;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
#pragma warning disable AVALONIA_X11_CSD // Drawn decorations are experimental in Avalonia 12.
            // Without it, drawing in software allocates a window-sized buffer every frame, and its memory pressure forces full collections.
            .With(new X11PlatformOptions { UseRetainedFramebuffer = true, EnableDrawnDecorations = StartupSettings.TitleBar() == TitleBarMode.Custom })
#pragma warning restore AVALONIA_X11_CSD
            .WithInterFont()
            .LogToTrace();

    private static async Task<CompositionResult> ComposeAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var hub = new HubPaths(RunesmithPaths.Hub);
        HubReport = HubStartup.Run(HubConfiguration.Load(AppContext.BaseDirectory), hub, StartupSettings.HubEnabled(), TimeProvider.System);
        Timings.Add(("Prepare the plugins from the hub", stopwatch.Elapsed));

        stopwatch.Restart();
        string[] hostAssemblies = ["Runesmith.Sdk", "Runesmith.Workspace", "Runesmith.Languages", "Runesmith.Editor", "Runesmith.Shell"];
        (string Path, PluginSource Source)[] folders = Arguments.ArePluginsDisabled || IsSafeMode
            ? []
            : [(hub.Plugins, PluginSource.Hub), (RunesmithPaths.UserPlugins, PluginSource.Local)];

        var options = new CompositionOptions(
            [.. hostAssemblies.Select(name => System.Reflection.Assembly.Load(name))],
            folders,
            Path.Combine(RunesmithPaths.Cache, "composition"))
        {
            DisabledPlugins = StartupSettings.DisabledPlugins(),
            HubPlugins = HubReport.HubPlugins,
            WithheldPlugins = HubReport.Withheld,
            AllowLocalOverrides = StartupSettings.AllowLocalOverrides(),
        };
        var result = await RunesmithComposition.CreateAsync(options);
        foreach (var plugin in result.Plugins.Where(p => p.State == PluginState.Failed))
            Console.Error.WriteLine($"Plugin {plugin.Manifest.Id} could not load: {plugin.Error}");
        foreach (var error in result.Errors)
            Console.Error.WriteLine($"Composition: {error}");
        Guard.Loaded(result.Plugins.Where(p => p.State is PluginState.Loaded or PluginState.Failed)
            .Select(p => new SuspectPlugin(p.Manifest.Id, p.Manifest.Name, p.Manifest.Version)));
        Timings.Add((result.IsFromCache ? "Compose the parts (from the cache)" : "Compose the parts", stopwatch.Elapsed));
        return result;
    }

    private static void StartAgain(IReadOnlyList<string> arguments)
    {
        if (Environment.ProcessPath is not { } executable)
            return;

        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        if (IsDotnetHost(executable))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(start);
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }

    private static bool IsDotnetHost(string executable) => string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase);
}
