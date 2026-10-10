using Runesmith.Dap.Protocol;
using Runesmith.Sdk.Debugging;
using Runesmith.Shell.Debugging;
using Runesmith.Shell.Running;
using Runesmith.Shell.Tests.Running;
using static Runesmith.Shell.Tests.Debugging.DebugTesting;

namespace Runesmith.Shell.Tests.Debugging;

public sealed class DebugModelsTests : IAsyncDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("runesmith-debug-models-").FullName;
    private readonly string state = Directory.CreateTempSubdirectory("runesmith-debug-models-state-").FullName;
    private readonly ScriptedProgram program;
    private readonly BreakpointService breakpoints;
    private readonly DebugService debug;
    private readonly List<StackFrame> navigated = [];

    public DebugModelsTests()
    {
        program = new ScriptedProgram(Path.Combine(root, "Program.cs"));
        breakpoints = Breakpoints(root, state);
        debug = new DebugService([new Lazy<IDebugAdapterProvider>(() => new FakeEngine())], breakpoints, new FakeWorkspace(root), _ => null, _ => { }, _ => { },
            (_, _, _, _) => Task.FromResult((program.Client, (Func<ValueTask>)program.ReleaseAsync)));
    }

    public async ValueTask DisposeAsync()
    {
        if (debug.Current is { } session)
            await session.StopAsync();
        await program.DisposeAsync();
        Directory.Delete(root, recursive: true);
        Directory.Delete(state, recursive: true);
    }

    private async Task<DebugSession> PausedAsync()
    {
        var session = (await debug.StartAsync("App", Plan(Path.Combine(root, "App.dll")), new ConsoleBuffer(), null, Token))!;
        await program.StopAtBreakpointAsync();
        await WaitAsync(() => session.State == DebugState.Paused);
        return session;
    }

    [Fact]
    public async Task TheCallStackShowsThreadsAndFramesAndPickingAFrameShowsIt()
    {
        var model = new CallStackModel(debug, navigated.Add);
        model.Refresh();
        Assert.Empty(model.Frames);
        Assert.NotNull(model.Message);

        var session = await PausedAsync();
        model.Refresh();

        Assert.Null(model.Message);
        Assert.Equal(["Main Thread (paused)", "Worker"], model.Threads.Select(t => t.Text));
        Assert.Equal(["System.Console.WriteLine()", "Program.Main()", "Program.<Main>()"], model.Frames.Select(f => f.Name));
        Assert.Equal([false, true, true], model.Frames.Select(f => f.HasSource));
        Assert.Equal("Program.cs:5", model.Frames[1].Location);
        Assert.Same(model.Frames[1], model.SelectedFrame);
        Assert.Empty(navigated);

        model.SelectedFrame = model.Frames[2];
        Assert.Equal(12, session.CurrentFrame!.Id);
        Assert.Equal(12, Assert.Single(navigated).Id);

        model.SelectedThread = model.Threads[1];
        await WaitAsync(() => session.ThreadId == 2);
        model.Refresh();
        Assert.Equal("Worker.Run()", model.Frames[1].Name);
        Assert.Equal(["Main Thread", "Worker (paused)"], model.Threads.Select(t => t.Text));
    }

    [Fact]
    public async Task VariablesLoadLazilyAndStayOpenFromOnePauseToTheNext()
    {
        var model = new VariablesModel(debug);
        await model.RefreshAsync();
        Assert.Empty(model.Roots);

        var session = await PausedAsync();
        await model.RefreshAsync();

        Assert.Null(model.Message);
        Assert.Equal(["Locals", "Globals"], model.Roots.Select(r => r.Name));
        var locals = model.Roots[0];
        Assert.True(locals.IsExpanded);
        Assert.False(model.Roots[1].IsExpanded);
        Assert.Equal(["count", "person"], locals.Children.Select(c => c.Name));
        var person = locals.Children[1];
        Assert.Equal(("{Person}", "Person", true), (person.Value, person.Type, person.HasChildren));
        Assert.True(Assert.Single(person.Children).IsPlaceholder);
        Assert.Single(program.Adapter.AllArguments("variables"));

        person.IsExpanded = true;
        await WaitAsync(() => person.Children.Count == 1 && !person.Children[0].IsPlaceholder);
        Assert.Equal("\"Ada\"", person.Children[0].Value);

        await session.StepOverAsync();
        await WaitAsync(() => session is { State: DebugState.Paused, CurrentFrame.Line: 6 });
        await model.RefreshAsync();
        var again = model.Roots[0].Children.Single(c => c.Name == "person");
        Assert.True(again.IsExpanded);
        Assert.Equal("Name", Assert.Single(again.Children).Name);

        await session.ContinueAsync();
        await model.RefreshAsync();
        Assert.Empty(model.Roots);
        Assert.Equal("The program is running.", model.Message);
    }

    [Fact]
    public async Task WatchesAreEvaluatedInThePausedFrameAndShowTheirErrors()
    {
        breakpoints.AddWatch("count * 2");
        breakpoints.AddWatch("person");
        breakpoints.AddWatch("missing");
        var model = new WatchesModel(debug, breakpoints);
        await model.RefreshAsync();
        Assert.All(model.Items, item => Assert.Equal("Available while paused", item.Value));

        await PausedAsync();
        await model.RefreshAsync();

        Assert.Equal(["count * 2", "person", "missing"], model.Items.Select(i => i.Name));
        Assert.Equal(["6", "{Person}", "The name is not in scope"], model.Items.Select(i => i.Value));
        Assert.Equal([false, false, true], model.Items.Select(i => i.IsError));
        Assert.True(model.Items[1].HasChildren);
        Assert.All(program.Adapter.AllArguments("evaluate"), a => Assert.Equal("watch", a!["context"]!.GetValue<string>()));
        Assert.All(program.Adapter.AllArguments("evaluate"), a => Assert.Equal(11, a!["frameId"]!.GetValue<int>()));

        model.Items[1].IsExpanded = true;
        await WaitAsync(() => model.Items[1].Children is [{ IsPlaceholder: false }]);

        model.Remove(2);
        model.Edit(0, "count");
        await model.RefreshAsync(force: true);
        Assert.Equal(["count", "person"], breakpoints.Watches);
        Assert.Equal(["count", "person"], model.Items.Select(i => i.Name));
    }

    [Fact]
    public async Task TheBreakpointsListTurnsBreakpointsAndExceptionFiltersOnAndOff()
    {
        var path = Path.Combine(root, "src", "Program.cs");
        breakpoints.Toggle(path, 4);
        breakpoints.Set(new LineBreakpoint(path, 9) { Condition = "x > 1", LogMessage = "x is {x}" });
        var model = new BreakpointsModel(breakpoints, debug, () => root);
        model.Refresh();

        Assert.Equal(["Program.cs:5", "Program.cs:10"], model.Items.Select(i => $"{i.FileName}:{i.Line}"));
        Assert.Equal("src", model.Items[0].Folder);
        Assert.Equal("when x > 1  logs x is {x}", model.Items[1].Detail);
        Assert.Empty(model.ExceptionFilters);

        model.Items[0].IsEnabled = false;
        Assert.False(breakpoints.Find(path, 4)!.IsEnabled);
        model.Remove(model.Items[1]);
        Assert.Single(breakpoints.All);

        await PausedAsync();
        model.Refresh();
        Assert.Equal([("all", false), ("unhandled", true)], model.ExceptionFilters.Select(f => (f.Filter, f.IsEnabled)));
        model.ExceptionFilters[0].IsEnabled = true;
        Assert.Equal(["all", "unhandled"], breakpoints.ExceptionFilters!.Order());
    }

    [Fact]
    public async Task TheConsoleEvaluatesInThePausedFrameAndRemembersWhatWasTyped()
    {
        var model = new DebugConsoleModel(debug);
        Assert.Null(model.Console);
        Assert.False(model.CanEvaluate);

        var session = await PausedAsync();
        await model.EvaluateAsync("count * 2");
        await model.EvaluateAsync("missing");

        Assert.Same(session.Console, model.Console);
        Assert.True(model.CanEvaluate);
        var lines = session.Console.Drain(all: true).Lines;
        Assert.Contains(lines, l => l.Text == "> count * 2" && l.Source == ConsoleSource.Input);
        Assert.Contains(lines, l => l.Text == "6" && l.Source == ConsoleSource.Output);
        Assert.Contains(lines, l => l.Text == "The name is not in scope" && l.Source == ConsoleSource.Error);
        Assert.Equal("repl", program.Adapter.Arguments("evaluate")!["context"]!.GetValue<string>());
        Assert.Equal("missing", model.Previous());
        Assert.Equal("count * 2", model.Previous());
        Assert.Null(model.Previous());
        Assert.Equal("missing", model.Next());
        Assert.Equal("", model.Next());
    }
}
