using Runesmith.Composition;
using Runesmith.Dap;
using Runesmith.Sdk.Debugging;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;
using Runesmith.Shell.Debugging;
using Runesmith.Shell.Running;
using Runesmith.Shell.Tests.Plugins;
using Runesmith.Shell.Tests.Running;
using static Runesmith.Shell.Tests.Debugging.DebugTesting;

namespace Runesmith.Shell.Tests.Debugging;

public sealed class DebugServiceTests : IAsyncDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-debugger-").FullName;
    private readonly string state = Directory.CreateTempSubdirectory("runesmith-debugger-state-").FullName;
    private readonly List<string> refusals = [];
    private readonly List<DebugAdapterDescriptor> started = [];
    private readonly ScriptedProgram program;
    private readonly BreakpointService breakpoints;

    public DebugServiceTests()
    {
        program = new ScriptedProgram(Path.Combine(root, "Program.cs"));
        breakpoints = Breakpoints(root, state);
    }

    public async ValueTask DisposeAsync()
    {
        await program.DisposeAsync();
        Directory.Delete(root, recursive: true);
        Directory.Delete(state, recursive: true);
    }

    private DebugService Service(IDebugAdapterProvider? engine, PluginInfo? owner = null) =>
        new(engine is null ? [] : [new Lazy<IDebugAdapterProvider>(() => engine)], breakpoints, new FakeWorkspace(root), _ => owner, refusals.Add, _ => { },
            (adapter, _, _, _) =>
            {
                started.Add(adapter);
                return Task.FromResult((program.Client, (Func<ValueTask>)program.ReleaseAsync));
            });

    [Fact]
    public void FindsTheEngineThatServesALaunchsDebugger()
    {
        var engine = new FakeEngine();
        var service = Service(engine);

        Assert.Same(engine, service.FindProvider("dotnet"));
        Assert.Same(engine, service.FindProvider("DotNet"));
        Assert.Null(service.FindProvider("jdwp"));
        Assert.Null(service.WhyCannotDebug);
        Assert.Equal("No installed plugin provides a debugger.", Service(null).WhyCannotDebug);
    }

    [Fact]
    public void AnEngineThatFailsToLoadIsLeftOut()
    {
        var service = new DebugService([new Lazy<IDebugAdapterProvider>(() => throw new InvalidOperationException("broken"))], breakpoints, new FakeWorkspace(root),
            _ => null, refusals.Add, _ => { }, (_, _, _, _) => throw new InvalidOperationException());

        Assert.Empty(service.Providers);
    }

    [Fact]
    public async Task APlanForADebuggerNoPluginProvidesSaysSo()
    {
        var console = new ConsoleBuffer();
        var plan = Plan(Path.Combine(root, "App.dll")) with { Debug = new DebugLaunch("jdwp", new Dictionary<string, string>()) };

        Assert.Null(await Service(new FakeEngine()).StartAsync("App", plan, console, null, Token));
        Assert.Contains("No installed plugin provides the jdwp debugger", console.GetText(), StringComparison.Ordinal);
        Assert.Empty(started);
    }

    [Fact]
    public async Task AnEngineWithoutTheProcessCapabilityIsRefusedBeforeItsAdapterStarts()
    {
        var console = new ConsoleBuffer();
        var service = Service(new FakeEngine(), TestCallers.Plugin("acme.debugger", "network"));

        Assert.Null(await service.StartAsync("App", Plan(Path.Combine(root, "App.dll")), console, null, Token));

        Assert.Empty(started);
        var refusal = Assert.Single(refusals);
        Assert.Contains("acme.debugger", refusal, StringComparison.Ordinal);
        Assert.Contains("process", refusal, StringComparison.Ordinal);
        Assert.Contains("Fake debugger could not start", console.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReachingAnAdapterOverASocketNeedsTheNetworkCapability()
    {
        var console = new ConsoleBuffer();
        var service = Service(new FakeEngine(new DebugAdapterServer("127.0.0.1", 4711)), TestCallers.Plugin("acme.debugger", "process"));

        Assert.Null(await service.StartAsync("App", Plan(Path.Combine(root, "App.dll")), console, null, Token));

        Assert.Empty(started);
        Assert.Contains("network", Assert.Single(refusals), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEngineWithTheCapabilityStartsASessionAttributedToItsLaunch()
    {
        var engine = new FakeEngine();
        var service = Service(engine, TestCallers.Plugin("acme.debugger", "process"));
        var console = new ConsoleBuffer();
        DebugSession? announced = null;
        service.SessionStarted += (_, session) => announced = session;

        var session = await service.StartAsync("App", Plan(Path.Combine(root, "App.dll")), console, null, Token);

        Assert.NotNull(session);
        Assert.Same(session, announced);
        Assert.Same(session, service.Current);
        Assert.Same(session, service.Latest);
        Assert.Empty(refusals);
        Assert.Equal("fake-adapter", Assert.IsType<DebugAdapterExecutable>(Assert.Single(started)).Command);
        Assert.Equal(root, Assert.Single(engine.Contexts).RootPath);
        Assert.Equal(Path.Combine(root, "App.dll"), program.Adapter.Arguments("launch")!["program"]!.GetValue<string>());
        Assert.Equal(["unhandled"], service.ExceptionFilters.Where(f => f.Default).Select(f => f.Filter));
        Assert.Contains("Debugging App with Fake debugger.", console.GetText(), StringComparison.Ordinal);

        breakpoints.Toggle(program.ProgramPath, 9);
        await program.Adapter.WaitForAsync("setBreakpoints");
        breakpoints.SetExceptionFilter("all", true, ["unhandled"]);
        await program.Adapter.WaitForAsync("setExceptionBreakpoints", 2);

        await session.StopAsync();
        Assert.Null(service.Current);
    }

    [Fact]
    public async Task DebuggingARunConfigurationRunsItUnderTheDebuggerAndReportsTheExitCode()
    {
        var type = new DebuggableType(Plan(Path.Combine(root, "App.dll")));
        var service = Service(new FakeEngine());
        var configurations = new RunConfigurationService([new Lazy<IRunConfigurationType>(() => type)], new FakeWorkspace(null), null, state);
        var runs = new RunService(configurations, new FakeBuildService(), new FakeWorkspace(root), () => Task.CompletedTask, new FakeOutput(), new FakeDiagnostics(),
            new FakeNotifications(), null, () => service);
        breakpoints.Toggle(program.ProgramPath, 4);
        var configuration = new RunConfiguration(type.Id, "App", new OptionValues()) { BeforeLaunch = [] };

        var run = await runs.RunAsync(configuration, RunMode.Debug) ?? throw new InvalidOperationException("The run did not start.");
        await WaitAsync(() => service.Current is { State: DebugState.Running } && run.State == RunState.Running);
        Assert.Equal(RunMode.Debug, type.Modes.Single());

        await program.StopAtBreakpointAsync();
        await WaitAsync(() => service.Current is { State: DebugState.Paused });
        await program.Adapter.SendEventAsync("output", """{ "category": "stdout", "output": "Hello from the program\n" }""");
        await program.ExitAsync(7);

        Assert.Equal(7, await run.Completion.WaitAsync(WaitLimit, Token));
        Assert.Contains("Hello from the program", run.Console.GetText(), StringComparison.Ordinal);
        Assert.Contains("exit code 7", run.Console.GetText(), StringComparison.Ordinal);
        Assert.Null(service.Current);
    }

    [Fact]
    public async Task StoppingTheRunStopsTheDebugSession()
    {
        var type = new DebuggableType(Plan(Path.Combine(root, "App.dll")));
        var service = Service(new FakeEngine());
        var configurations = new RunConfigurationService([new Lazy<IRunConfigurationType>(() => type)], new FakeWorkspace(null), null, state);
        var runs = new RunService(configurations, new FakeBuildService(), new FakeWorkspace(root), () => Task.CompletedTask, new FakeOutput(), new FakeDiagnostics(),
            new FakeNotifications(), null, () => service);

        var run = await runs.RunAsync(new RunConfiguration(type.Id, "App", new OptionValues()) { BeforeLaunch = [] }, RunMode.Debug);
        await WaitAsync(() => service.Current is { State: DebugState.Running });
        run!.Stop();

        await run.Completion.WaitAsync(WaitLimit, Token);
        Assert.True(run.WasStopped);
        Assert.Contains("terminate", program.Adapter.Commands);
        Assert.Contains("Stopped after", run.Console.GetText(), StringComparison.Ordinal);
    }

    /// <summary>A configuration type that can be debugged, preparing the plan the test gives.</summary>
    private sealed class DebuggableType(LaunchPlan plan) : IRunConfigurationType
    {
        public string Id => "debuggable";

        public string Name => "Debuggable";

        public string Icon => "play";

        public string Description => "A type the tests debug.";

        public bool CanDebug => true;

        public List<RunMode> Modes { get; } = [];

        public Task<OptionSet> GetOptionsAsync(string rootPath, CancellationToken cancellationToken) => Task.FromResult(OptionSet.Empty);

        public Task<IReadOnlyList<RunConfiguration>> DetectAsync(string rootPath, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RunConfiguration>>([]);

        public Task<LaunchPlan> PrepareAsync(RunConfiguration configuration, RunContext context, CancellationToken cancellationToken)
        {
            Modes.Add(context.Mode);
            return Task.FromResult(plan);
        }

        public Task<Sdk.Build.BuildResult?> BuildAsync(RunConfiguration configuration, Sdk.Build.BuildContext context, CancellationToken cancellationToken) =>
            Task.FromResult<Sdk.Build.BuildResult?>(null);
    }
}
