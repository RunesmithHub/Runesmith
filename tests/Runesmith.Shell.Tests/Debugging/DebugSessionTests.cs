using System.Text.Json.Nodes;
using Runesmith.Dap;
using Runesmith.Sdk.Debugging;
using Runesmith.Shell.Debugging;
using Runesmith.Shell.Running;
using Runesmith.Tests.Dap;
using static Runesmith.Shell.Tests.Debugging.DebugTesting;

namespace Runesmith.Shell.Tests.Debugging;

public sealed class DebugSessionTests : IAsyncDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-debug-").FullName;
    private readonly string state = Directory.CreateTempSubdirectory("runesmith-debug-state-").FullName;
    private readonly ScriptedProgram program;
    private readonly BreakpointService breakpoints;
    private readonly DebugSession session;

    public DebugSessionTests()
    {
        program = new ScriptedProgram(Path.Combine(root, "Program.cs"));
        breakpoints = Breakpoints(root, state);
        session = new DebugSession("App", "Fake debugger", program.Client, breakpoints, program.ReleaseAsync);
    }

    public async ValueTask DisposeAsync()
    {
        await session.StopAsync();
        await program.DisposeAsync();
        Directory.Delete(root, recursive: true);
        Directory.Delete(state, recursive: true);
    }

    private Task StartAsync() =>
        session.StartAsync(new DebugAdapterRequest(DebugRequestKind.Launch, new JsonObject { ["program"] = "App.dll" }), "fake", Token);

    [Fact]
    public async Task StartingSendsTheBreakpointsAndExceptionFiltersBeforeConfigurationDone()
    {
        breakpoints.Toggle(program.ProgramPath, 4);
        breakpoints.Set(new LineBreakpoint(program.ProgramPath, 9) { Condition = "count > 2" });
        breakpoints.Set(new LineBreakpoint(program.ProgramPath, 12) { IsEnabled = false });

        await StartAsync();

        Assert.Equal(DebugState.Running, session.State);
        Assert.Equal(["initialize", "launch", "setBreakpoints", "setExceptionBreakpoints", "configurationDone"], program.Adapter.Commands);
        Assert.Equal("fake", program.Adapter.Arguments("initialize")!["adapterId"]!.GetValue<string>());
        Assert.Equal("App.dll", program.Adapter.Arguments("launch")!["program"]!.GetValue<string>());
        var sent = program.Adapter.Arguments("setBreakpoints")!;
        Assert.Equal(program.ProgramPath, sent["source"]!["path"]!.GetValue<string>());
        Assert.Equal([5, 10], sent["breakpoints"]!.AsArray().Select(b => b!["line"]!.GetValue<int>()));
        Assert.Equal("count > 2", sent["breakpoints"]![1]!["condition"]!.GetValue<string>());
        Assert.Equal(["unhandled"], program.Adapter.Arguments("setExceptionBreakpoints")!["filters"]!.AsArray().Select(f => f!.GetValue<string>()));
        Assert.True(breakpoints.StatusOf(breakpoints.Find(program.ProgramPath, 4)!)!.Verified);
        Assert.Null(breakpoints.StatusOf(breakpoints.Find(program.ProgramPath, 12)!));
    }

    [Fact]
    public async Task StopsAtABreakpointShowsTheFrameWithSourceStepsAndContinues()
    {
        breakpoints.Toggle(program.ProgramPath, 4);
        await StartAsync();
        var changes = 0;
        session.StateChanged += (_, _) => Interlocked.Increment(ref changes);

        await program.StopAtBreakpointAsync();
        await WaitAsync(() => session.State == DebugState.Paused);

        Assert.Equal("breakpoint", session.Stop!.Reason);
        Assert.Equal(1, session.ThreadId);
        Assert.Equal(["Main Thread", "Worker"], session.Threads.Select(t => t.Name));
        Assert.Equal(3, session.Frames.Count);
        Assert.Equal("Program.Main()", session.CurrentFrame!.Name);
        Assert.Equal(5, session.CurrentFrame.Line);
        Assert.True(changes > 0);

        await session.StepOverAsync();
        await WaitAsync(() => session.State == DebugState.Paused && session.CurrentFrame?.Line == 6);
        Assert.Equal("step", session.Stop!.Reason);
        Assert.Equal(1, program.Adapter.Arguments("next")!["threadId"]!.GetValue<int>());

        await session.ContinueAsync();
        Assert.Equal(DebugState.Running, session.State);
        Assert.Null(session.CurrentFrame);
        Assert.Empty(session.Frames);
        await program.Adapter.WaitForAsync("continue");
    }

    [Fact]
    public async Task ExitingEndsTheSessionWithTheExitCodeAndReleasesTheAdapter()
    {
        breakpoints.Toggle(program.ProgramPath, 4);
        await StartAsync();

        await program.ExitAsync(3);

        Assert.Equal(3, await session.Completion.WaitAsync(WaitLimit, Token));
        Assert.Equal(DebugState.Ended, session.State);
        Assert.True(program.Released);
        Assert.Contains("disconnect", program.Adapter.Commands);
        Assert.Null(breakpoints.StatusOf(breakpoints.Find(program.ProgramPath, 4)!));
    }

    [Fact]
    public async Task StoppingAsksTheAdapterToTerminateAndEnds()
    {
        await StartAsync();

        await session.StopAsync();

        Assert.Equal(DebugState.Ended, session.State);
        Assert.Contains("terminate", program.Adapter.Commands);
        Assert.Equal(0, await session.Completion.WaitAsync(WaitLimit, Token));
    }

    [Fact]
    public async Task ACrashedAdapterEndsTheSessionAndSaysSo()
    {
        await StartAsync();

        program.Adapter.Crash();

        Assert.Null(await session.Completion.WaitAsync(WaitLimit, Token));
        Assert.Equal(DebugState.Ended, session.State);
        Assert.Contains("Fake debugger stopped", session.Console.GetText(), StringComparison.Ordinal);
        Assert.True(program.Released);
    }

    [Fact]
    public async Task AnAdapterThatRefusesTheLaunchFailsTheStartWithItsMessage()
    {
        program.Adapter.On("launch", _ => FakeReply.Failure("The program 'App.dll' does not exist."));

        var exception = await Assert.ThrowsAsync<DebugAdapterException>(StartAsync);

        Assert.Contains("does not exist", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BreakpointsChangedDuringTheSessionAreSentAgainAndTheirStatusFollowsTheAdapter()
    {
        await StartAsync();

        breakpoints.Toggle(program.ProgramPath, ScriptedProgram.UnverifiedLine - 1);
        await session.UpdateBreakpointsAsync(program.ProgramPath);

        var breakpoint = breakpoints.Find(program.ProgramPath, ScriptedProgram.UnverifiedLine - 1)!;
        Assert.Equal(new BreakpointStatus(false, "No code on this line"), breakpoints.StatusOf(breakpoint));

        await program.Adapter.SendEventAsync("breakpoint", $$"""{ "reason": "changed", "breakpoint": { "id": {{100 + ScriptedProgram.UnverifiedLine}}, "verified": true } }""");
        await WaitAsync(() => breakpoints.StatusOf(breakpoint)?.Verified == true);
    }

    [Fact]
    public async Task ADebuggerWithoutLogPointsStopsAtThemAndTheSessionLogsAndGoesOn()
    {
        breakpoints.Set(new LineBreakpoint(program.ProgramPath, 4) { LogMessage = "double is {count * 2}, {missing}" });

        await StartAsync();
        var sent = program.Adapter.Arguments("setBreakpoints")!["breakpoints"]![0]!;
        Assert.Equal(5, sent["line"]!.GetValue<int>());
        Assert.Null(sent["logMessage"]);
        Assert.True(breakpoints.StatusOf(breakpoints.Find(program.ProgramPath, 4)!)!.Verified);

        await program.StopAtBreakpointAsync();
        await program.Adapter.WaitForAsync("continue");

        await WaitAsync(() => session.Console.GetText().Contains("double is", StringComparison.Ordinal));
        Assert.Contains("double is 6, {The name is not in scope}", session.Console.GetText(), StringComparison.Ordinal);
        Assert.Equal(DebugState.Running, session.State);
        Assert.Null(session.CurrentFrame);
    }

    [Fact]
    public async Task ADebuggerWithLogPointsGetsTheMessage()
    {
        await using var withLogPoints = new ScriptedProgram(program.ProgramPath, """{ "supportsConfigurationDoneRequest": true, "supportsLogPoints": true }""");
        var other = new DebugSession("App", "Fake debugger", withLogPoints.Client, breakpoints, withLogPoints.ReleaseAsync);
        breakpoints.Set(new LineBreakpoint(program.ProgramPath, 4) { LogMessage = "count is {count}" });

        await other.StartAsync(new DebugAdapterRequest(DebugRequestKind.Launch, new JsonObject()), "fake", Token);

        Assert.Equal("count is {count}", withLogPoints.Adapter.Arguments("setBreakpoints")!["breakpoints"]![0]!["logMessage"]!.GetValue<string>());
        await other.StopAsync();
    }

    [Fact]
    public async Task PickingAnotherThreadOrFrameChangesTheCurrentFrame()
    {
        await StartAsync();
        await program.StopAtBreakpointAsync();
        await WaitAsync(() => session.State == DebugState.Paused);

        session.SelectFrame(session.Frames[2]);
        Assert.Equal(12, session.CurrentFrame!.Id);

        await session.SelectThreadAsync(2);
        Assert.Equal(2, session.ThreadId);
        Assert.Equal("Worker.Run()", session.CurrentFrame!.Name);
    }

    [Fact]
    public async Task ProgramOutputGoesToTheConsoleAndToItsListeners()
    {
        await StartAsync();
        var seen = new List<string>();
        session.ProgramOutput += (_, e) => seen.Add(e.Output);

        await program.Adapter.SendEventAsync("output", """{ "category": "stdout", "output": "Hello\n" }""");
        await program.Adapter.SendEventAsync("output", """{ "category": "console", "output": "Loaded App.dll\n" }""");
        await program.Adapter.SendEventAsync("output", """{ "category": "telemetry", "output": "secret\n" }""");
        await WaitAsync(() => session.Console.GetText().Contains("Loaded", StringComparison.Ordinal));

        Assert.Equal(["Hello\n"], seen);
        var lines = session.Console.Drain(all: true).Lines;
        Assert.Contains(lines, l => l.Text == "Hello" && l.Source == ConsoleSource.Output);
        Assert.Contains(lines, l => l.Text == "Loaded App.dll" && l.Source == ConsoleSource.System);
        Assert.DoesNotContain(lines, l => l.Text.Contains("secret", StringComparison.Ordinal));
    }
}
