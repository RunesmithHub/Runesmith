using Avalonia.Controls;
using Avalonia.VisualTree;
using Runesmith.Dap.Protocol;
using Runesmith.Editor.Tests;
using Runesmith.Sdk.Debugging;
using Runesmith.Shell.Debugging;
using Runesmith.Shell.Running;
using Runesmith.Shell.Services;
using Runesmith.Shell.Tests.Running;
using static Runesmith.Shell.Tests.Debugging.DebugTesting;

namespace Runesmith.Shell.Tests.Debugging;

public sealed class DebugViewTests
{
    [Fact]
    public Task TheDebugWindowShowsThePausedSessionsStackVariablesAndBreakpoints() => HeadlessSession.Value.Dispatch(async () =>
    {
        var root = Directory.CreateTempSubdirectory("runesmith-debug-view-").FullName;
        var state = Directory.CreateTempSubdirectory("runesmith-debug-view-state-").FullName;
        await using var program = new ScriptedProgram(Path.Combine(root, "Program.cs"));
        var breakpoints = Breakpoints(root, state);
        breakpoints.Toggle(program.ProgramPath, 4);
        var debug = new DebugService([new Lazy<IDebugAdapterProvider>(() => new FakeEngine())], breakpoints, new FakeWorkspace(root), _ => null, _ => { }, _ => { },
            (_, _, _, _) => Task.FromResult((program.Client, (Func<ValueTask>)program.ReleaseAsync)));
        var navigated = new List<StackFrame>();
        var view = new DebugView(debug, breakpoints, new FakeCommands(), new NotificationService(), () => root, navigated.Add);
        var window = new Window { Width = 1000, Height = 400, Content = view };
        window.Show();
        Assert.Null(view.HeaderTitle);
        Assert.False(view.HeaderActions.IsVisible);

        var session = (await debug.StartAsync("App", Plan(Path.Combine(root, "App.dll")), new ConsoleBuffer(), null, Token))!;
        await program.StopAtBreakpointAsync();
        await WaitAsync(() => session.State == DebugState.Paused);
        await program.Adapter.SendEventAsync("output", """{ "category": "stdout", "output": "Hello\n" }""");
        await WaitAsync(() => session.Console.Count > 0);
        view.Refresh();
        await WaitAsync(() => view.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "count"));

        Assert.Equal("App, paused at a breakpoint", view.HeaderTitle);
        Assert.True(view.HeaderActions.IsVisible);
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("Program.Main()", texts);
        Assert.Contains("Locals", texts);
        Assert.Contains("person", texts);

        await session.StopAsync();
        view.Refresh();
        Assert.Null(view.HeaderTitle);
        window.Close();
        Directory.Delete(root, recursive: true);
        Directory.Delete(state, recursive: true);
        return true;
    }, Token);
}
