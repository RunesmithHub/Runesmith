using System.Diagnostics;
using Runesmith.Sdk.Debugging;
using Runesmith.Sdk.Running;
using Runesmith.Shell.Debugging;
using Runesmith.Shell.Running;
using Runesmith.Shell.Tests.Running;
using Runesmith.Tests.Dap;
using static Runesmith.Shell.Tests.Debugging.DebugTesting;

namespace Runesmith.Shell.Tests.Debugging;

/// <summary>Debugs a real console app with netcoredbg, when it and the .NET SDK are on the PATH.</summary>
public sealed class NetcoredbgTests : IAsyncDisposable
{
    private const string Program = """
        var count = 3;
        var total = Add(count, 4);
        System.Console.WriteLine($"total is {total}");
        return total - 7;

        static int Add(int a, int b)
        {
            var sum = a + b;
            return sum;
        }
        """;

    private readonly string root = Directory.CreateTempSubdirectory("runesmith-netcoredbg-").FullName;
    private readonly string state = Directory.CreateTempSubdirectory("runesmith-netcoredbg-state-").FullName;

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        Directory.Delete(root, recursive: true);
        Directory.Delete(state, recursive: true);
    }

    [Fact]
    public async Task StopsAtABreakpointShowsVariablesStepsAndRunsToTheEnd()
    {
        if (NetcoredbgEngine.Find() is null)
            Assert.Skip("netcoredbg is not on the PATH.");

        var program = await BuildAsync();
        var source = Path.Combine(root, "Program.cs");
        var breakpoints = Breakpoints(root, state);
        breakpoints.Toggle(source, 1);
        var debug = new DebugService([new Lazy<IDebugAdapterProvider>(() => new NetcoredbgEngine())], breakpoints, new FakeWorkspace(root), _ => null, _ => { }, _ => { },
            DebugService.StartAdapterAsync);
        var console = new ConsoleBuffer();
        var plan = new LaunchPlan("dotnet", [program], root)
        {
            Debug = new DebugLaunch("dotnet", new Dictionary<string, string> { ["program"] = program, ["args"] = "", ["cwd"] = root, ["env"] = "" }),
        };

        var session = await debug.StartAsync("App", plan, console, null, Token);
        Assert.True(session is not null, console.GetText());
        await WaitAsync(() => session.State == DebugState.Paused, "The program did not stop at the breakpoint. " + session.Console.GetText());

        Assert.Equal("breakpoint", session.Stop!.Reason);
        Assert.Equal(2, session.CurrentFrame!.Line);
        Assert.Equal(source, session.CurrentFrame.Source!.Path);
        var locals = await session.GetVariablesAsync((await session.GetScopesAsync(session.CurrentFrame, Token))[0].VariablesReference, Token);
        Assert.Contains(locals, v => v.Name == "count" && v.Value == "3");
        Assert.True(breakpoints.StatusOf(breakpoints.Find(source, 1)!)!.Verified);

        await session.StepIntoAsync();
        await WaitAsync(() => session is { State: DebugState.Paused, CurrentFrame.Name: var name } && name.Contains("Add", StringComparison.Ordinal), "Step into did not reach Add.");
        Assert.InRange(session.CurrentFrame!.Line, 7, 8);
        Assert.Equal("7", (await session.EvaluateAsync("a + b", "watch", Token)).Result);

        await session.StepOutAsync();
        await WaitAsync(() => session is { State: DebugState.Paused, CurrentFrame.Line: 2 }, "Step out did not return to the caller.");
        await session.StepOverAsync();
        await WaitAsync(() => session is { State: DebugState.Paused, CurrentFrame.Line: 3 }, "Step over did not reach the next line.");

        await session.ContinueAsync();
        Assert.Equal(0, await session.Completion.WaitAsync(TimeSpan.FromSeconds(30), Token));
        Assert.Contains("total is 7", session.Console.GetText(), StringComparison.Ordinal);
    }

    private async Task<string> BuildAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(root, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """, Token);
        await File.WriteAllTextAsync(Path.Combine(root, "Program.cs"), Program, Token);
        var start = new ProcessStartInfo("dotnet", ["build", "-nologo", "-v", "q", Path.Combine(root, "App.csproj")]) { RedirectStandardOutput = true, RedirectStandardError = true };
        using var build = Process.Start(start)!;
        var output = await build.StandardOutput.ReadToEndAsync(Token);
        await build.WaitForExitAsync(Token);
        Assert.True(build.ExitCode == 0, output);
        return Path.Combine(root, "bin", "Debug", "net10.0", "App.dll");
    }
}
