using System.Text;
using Runesmith.Composition;
using Runesmith.Sdk.Terminals;
using Runesmith.Shell.Terminal;
using Runesmith.Shell.Tests.Plugins;
using Runesmith.Shell.Tests.Running;

namespace Runesmith.Shell.Tests.Terminal;

public sealed class TerminalServiceTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);
    private readonly List<string> log = [];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void APluginWithoutTheProcessCapabilityCannotCreateATerminal()
    {
        var service = Service(TestCallers.Plugin("acme.todo", "network"));

        var exception = Assert.Throws<UnauthorizedAccessException>(() => service.CreateTerminal("Build", new TerminalOptions { Command = "echo", IsHidden = true }));

        Assert.Contains("process", exception.Message, StringComparison.Ordinal);
        Assert.Equal(exception.Message, Assert.Single(log));
        Assert.Empty(service.Terminals);
    }

    [Fact]
    public async Task APluginWithTheProcessCapabilityGetsItsProgramsOutputAndExitCode()
    {
        if (OperatingSystem.IsWindows())
            return;

        var service = Service(TestCallers.Plugin("acme.todo", "process"));
        var output = new StringBuilder();
        var exited = new TaskCompletionSource<int?>();

        var terminal = service.CreateTerminal("Greeting", new TerminalOptions
        {
            Command = "/bin/sh",
            Arguments = ["-c", "read name; echo \"hello $name from $RUNESMITH_TERMINAL_TEST\"; exit 3"],
            Environment = new Dictionary<string, string?> { ["RUNESMITH_TERMINAL_TEST"] = "a plugin" },
            IsHidden = true,
        });
        terminal.Data += (_, e) =>
        {
            lock (output)
                output.Append(e.Text);
        };
        terminal.Exited += (_, e) => exited.TrySetResult(e.ExitCode);
        terminal.SendText("world");

        Assert.Equal(3, await exited.Task.WaitAsync(Timeout, Token));
        Assert.True(terminal.HasExited);
        Assert.Equal(3, terminal.ExitCode);
        Assert.NotNull(terminal.ProcessId);
        lock (output)
            Assert.Contains("hello world from a plugin", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(log);
        Assert.Same(terminal, Assert.Single(service.Terminals));
    }

    [Fact]
    public async Task AHiddenTerminalJoinsTheTabsWhenShownAndLeavesWhenClosed()
    {
        if (OperatingSystem.IsWindows())
            return;

        var service = Service(null);
        var terminal = service.CreateTerminal("Hidden", new TerminalOptions { Command = "/bin/sh", Arguments = ["-c", "sleep 30"], IsHidden = true });

        await HeadlessSession.Value.Dispatch(() =>
        {
            Assert.Empty(service.Tabs);
            terminal.Show(takeFocus: false);
            Assert.Same(terminal, Assert.Single(service.Tabs));
            terminal.Close();
            Assert.Empty(service.Tabs);
        }, Token);

        Assert.Empty(service.Terminals);
    }

    [Fact]
    public async Task TheScreenShowsWhatTheProgramDraws()
    {
        if (OperatingSystem.IsWindows())
            return;

        var service = Service(null);
        var terminal = (TerminalSession)service.CreateTerminal("Colors", new TerminalOptions
        {
            Command = "/bin/sh",
            Arguments = ["-c", "printf '\\033[31mred\\033[0m plain\\n'; stty size"],
            IsHidden = true,
        });
        var exited = new TaskCompletionSource();
        terminal.Exited += (_, _) => exited.TrySetResult();
        await exited.Task.WaitAsync(Timeout, Token);

        lock (terminal.Gate)
        {
            Assert.Equal("red plain", terminal.Screen.RowText(0));
            Assert.Equal(1, terminal.Screen.Cell(0, 0).Style.Foreground.Index);
            Assert.Equal("24 80", terminal.Screen.RowText(1));
            Assert.Contains("Process exited with code 0.", terminal.Screen.GetAllText(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AProgramThatCannotStartIsReported()
    {
        if (OperatingSystem.IsWindows())
            return;

        var service = Service(null);

        Assert.Throws<InvalidOperationException>(() => service.CreateTerminal("Missing", new TerminalOptions { Command = "/nonexistent/tool", IsHidden = true }));
        Assert.Empty(service.Terminals);
    }

    private TerminalService Service(PluginInfo? caller) => new(new FakeWorkspace(Path.GetTempPath()), null, () => caller, log.Add);
}
