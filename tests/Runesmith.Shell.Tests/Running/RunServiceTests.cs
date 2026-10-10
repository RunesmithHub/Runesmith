using System.Diagnostics;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;
using Runesmith.Shell.Running;
using Runesmith.Shell.Terminal;

namespace Runesmith.Shell.Tests.Running;

public sealed class RunServiceTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly string root = Directory.CreateTempSubdirectory("runesmith-runner-").FullName;
    private readonly string state = Directory.CreateTempSubdirectory("runesmith-runner-state-").FullName;
    private readonly FakeType type = new();
    private readonly FakeBuildService build = new();
    private readonly FakeNotifications notifications = new();
    private readonly RunConfigurationService configurations;
    private readonly RunService runs;

    public RunServiceTests()
    {
        var workspace = new FakeWorkspace(root);
        configurations = new RunConfigurationService([new Lazy<IRunConfigurationType>(() => type)], new FakeWorkspace(null), null, state);
        runs = new RunService(configurations, build, workspace, () => Task.CompletedTask, new FakeOutput(), new FakeDiagnostics(), notifications, null);
    }

    private string Log => Path.Combine(root, "log.txt");

    [Fact]
    public async Task RunsTheProgramAndShowsItsOutputAndExitCode()
    {
        var session = await RunAsync(Config("App", "echo hello from the program", []));

        Assert.True(session.Succeeded);
        Assert.Equal(0, session.ExitCode);
        var text = session.Console.GetText();
        Assert.Contains("hello from the program", text, StringComparison.Ordinal);
        Assert.Contains("exit code 0", text, StringComparison.Ordinal);
        Assert.Equal(["App"], configurations.Recent);
    }

    [Fact]
    public async Task ReportsANonZeroExitCodeAndStandardErrorApart()
    {
        var session = await RunAsync(Config("App", "echo broken 1>&2 && exit 4", []));

        Assert.False(session.Succeeded);
        Assert.Equal(4, session.ExitCode);
        var lines = session.Console.Drain(all: true).Lines;
        Assert.Contains(lines, l => l.Source == ConsoleSource.Error && l.Text.Trim() == "broken");
    }

    [Fact]
    public async Task BeforeLaunchStepsRunInOrderAndAFailingStepStopsTheLaunch()
    {
        type.Build = (_, context) =>
        {
            File.AppendAllText(Log, "build\n");
            return new BuildResult(true, 0, 0, TimeSpan.Zero);
        };
        BeforeLaunchStep[] steps =
        [
            new(BeforeLaunchKind.Command, "echo one>>log.txt"),
            new(BeforeLaunchKind.Build),
            new(BeforeLaunchKind.Command, "exit 3"),
            new(BeforeLaunchKind.Command, "echo two>>log.txt"),
        ];

        var session = await RunAsync(Config("App", "echo program>>log.txt", steps));

        Assert.Equal(["one", "build"], File.ReadAllLines(Log).Select(l => l.Trim()));
        Assert.Null(session.ExitCode);
        Assert.False(session.Succeeded);
        Assert.Contains("failed, so App did not start", session.Console.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedBuildStopsTheLaunchAndAMissingTypeBuildFallsBackToTheFolderBuild()
    {
        type.Build = (_, _) => new BuildResult(false, 2, 0, TimeSpan.Zero);
        var failed = await RunAsync(Config("App", "echo program>>log.txt", [new(BeforeLaunchKind.Build)]));
        Assert.False(File.Exists(Log));
        Assert.Null(failed.ExitCode);

        type.Build = (_, _) => null;
        build.CanBuild = true;
        var fallback = await RunAsync(Config("App", "echo program>>log.txt", [new(BeforeLaunchKind.Build)]));
        Assert.Equal(1, build.Builds);
        Assert.True(fallback.Succeeded);

        build.Result = new BuildResult(false, 1, 0, TimeSpan.Zero);
        var folderFailed = await RunAsync(Config("App", "echo again>>log.txt", [new(BeforeLaunchKind.Build)]));
        Assert.Null(folderFailed.ExitCode);
        Assert.Equal(["program"], File.ReadAllLines(Log).Select(l => l.Trim()));
    }

    [Fact]
    public async Task ARunConfigurationStepRunsTheOtherFirstAndWaitsForIt()
    {
        var prepare = Config("Prepare", "echo prepared>>log.txt", []);
        var main = Config("Main", "echo main>>log.txt", [new(BeforeLaunchKind.RunConfiguration, "Prepare")]);
        var loop = Config("Loop", "echo loop>>log.txt", [new(BeforeLaunchKind.RunConfiguration, "Loop")]);
        await configurations.LoadAsync(root);
        configurations.Save([prepare, main, loop]);

        var session = await RunAsync(main);
        var looped = await RunAsync(loop);

        Assert.True(session.Succeeded);
        Assert.Equal(["prepared", "main"], File.ReadAllLines(Log).Select(l => l.Trim()));
        Assert.Equal(2, runs.Sessions.Count(s => s.Configuration.Name is "Prepare" or "Main"));
        Assert.Null(looped.ExitCode);
        Assert.Contains("cannot run before itself", looped.Console.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANewRunReplacesTheFinishedTabOfItsConfiguration()
    {
        await RunAsync(Config("App", "echo first", []));
        var second = await RunAsync(Config("App", "echo second", []));

        Assert.Same(second, Assert.Single(runs.Sessions));
    }

    [Fact]
    public async Task RunningAConfigurationThatRunsAsksToStopItAndRunsItAgain()
    {
        var configuration = Config("Server", LongCommand, []);
        var first = await runs.RunAsync(configuration);
        Assert.NotNull(first);
        await WaitUntilAsync(() => first.State == RunState.Running);

        notifications.Confirm = false;
        Assert.Null(await runs.RunAsync(configuration));
        Assert.Equal(RunState.Running, first.State);

        notifications.Confirm = true;
        var second = await runs.RunAsync(configuration);
        Assert.NotNull(second);
        Assert.Equal(2, notifications.Confirmations);
        Assert.Equal(RunState.Finished, first.State);
        Assert.True(first.WasStopped);
        second.Stop();
        await second.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StopEndsTheWholeProcessTree()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The process tree is checked with a POSIX shell.");
        var session = await runs.RunAsync(Config("Tree", "sleep 60 & echo $! > child.pid; wait", []));
        Assert.NotNull(session);
        var pidFile = Path.Combine(root, "child.pid");
        await WaitUntilAsync(() => File.Exists(pidFile) && File.ReadAllText(pidFile).Trim().Length > 0);
        var child = int.Parse(File.ReadAllText(pidFile).Trim(), System.Globalization.CultureInfo.InvariantCulture);

        session.Stop();
        await session.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        Assert.True(session.WasStopped);
        Assert.Contains("Stopped", session.Console.GetText(), StringComparison.Ordinal);
        await WaitUntilAsync(() => !IsAlive(child));
    }

    [Fact]
    public async Task TheProgramReadsWhatIsTypedIntoItsInput()
    {
        // cmd expands %line% as it reads the whole line, before set /p ran; call expands it again afterwards.
        var command = OperatingSystem.IsWindows() ? "set /p line= && call echo got %line%" : "read line; echo got $line";
        var session = await runs.RunAsync(Config("Echo", command, []));
        Assert.NotNull(session);
        await WaitUntilAsync(() => session.State == RunState.Running);

        await session.SendInputAsync("abc");
        await session.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);

        var lines = session.Console.Drain(all: true).Lines;
        Assert.Contains(lines, l => l.Source == ConsoleSource.Input && l.Text == "abc");
        Assert.Contains(lines, l => l.Text.Trim() == "got abc");
    }

    [Fact]
    public void TheProcessGetsThePlansEnvironmentOverRunesmithsAndColorsForDotnet()
    {
        var plan = new LaunchPlan("tool", ["a b", "c"], root) { Environment = new Dictionary<string, string> { ["MY_VALUE"] = "1", ["PATH"] = "/custom" } };

        var start = RunProcess.CreateStartInfo(plan);

        Assert.Equal(["a b", "c"], start.ArgumentList);
        Assert.Equal(root, start.WorkingDirectory);
        Assert.Equal("1", start.Environment["MY_VALUE"]);
        Assert.Equal("/custom", start.Environment["PATH"]);
        Assert.Equal("1", start.Environment["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"]);
        Assert.True(start.RedirectStandardInput && start.RedirectStandardOutput && start.RedirectStandardError);
        Assert.Equal("tool \"a b\" c", RunService.CommandLine(plan.Program, plan.Arguments));
    }

    private static string LongCommand => OperatingSystem.IsWindows() ? "ping -n 60 127.0.0.1 > nul" : "sleep 60";

    [Fact]
    public async Task WithTheTerminalSettingTheProgramRunsInATerminalTab()
    {
        if (OperatingSystem.IsWindows())
            return;

        var terminals = new TerminalService(new FakeWorkspace(root), null, () => null, _ => { });
        var inTerminal = new RunService(configurations, build, new FakeWorkspace(root), () => Task.CompletedTask, new FakeOutput(), new FakeDiagnostics(), notifications,
            null, null, () => terminals);

        await HeadlessSession.Value.Dispatch(async () =>
        {
            var session = await inTerminal.RunAsync(Config("App", "printf 'in a terminal'; exit 4", []));
            Assert.NotNull(session);
            await session.Completion.WaitAsync(Timeout);

            Assert.Equal(4, session.ExitCode);
            Assert.Contains("runs in the Terminal panel", session.Console.GetText(), StringComparison.Ordinal);
            Assert.Contains("exit code 4", session.Console.GetText(), StringComparison.Ordinal);
            var terminal = (TerminalSession)Assert.Single(terminals.Tabs);
            Assert.Equal("App", terminal.Name);
            lock (terminal.Gate)
                Assert.Contains("in a terminal", terminal.Screen.GetAllText(), StringComparison.Ordinal);
            terminal.Close();
        }, TestContext.Current.CancellationToken);
    }

    private async Task<RunSession> RunAsync(RunConfiguration configuration)
    {
        var session = await runs.RunAsync(configuration);
        Assert.NotNull(session);
        await session.Completion.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        return session;
    }

    private static RunConfiguration Config(string name, string command, IReadOnlyList<BeforeLaunchStep> steps)
    {
        var values = new OptionValues();
        values.Set("command", command);
        return new RunConfiguration("fake", name, values) { BeforeLaunch = steps };
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < Timeout, "The condition did not come true in time.");
            await Task.Delay(50);
        }
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        foreach (var session in runs.Sessions)
            session.Stop();
        configurations.Dispose();
        Directory.Delete(root, recursive: true);
        Directory.Delete(state, recursive: true);
    }
}
