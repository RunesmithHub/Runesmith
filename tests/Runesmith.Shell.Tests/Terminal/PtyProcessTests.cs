using System.Text;
using Runesmith.Shell.Terminal;

namespace Runesmith.Shell.Tests.Terminal;

public sealed class PtyProcessTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task AProgramRunsInATerminalOfTheGivenSize()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var pty = Start("/bin/sh", ["-c", "echo hello; tty; stty size"], columns: 90, rows: 33);
        var output = await ReadAllAsync(pty);

        Assert.Equal(0, await pty.Exited.WaitAsync(Timeout, TestContext.Current.CancellationToken));
        Assert.Contains("hello", output, StringComparison.Ordinal);
        Assert.Contains("/dev/", output, StringComparison.Ordinal);
        Assert.Contains("33 90", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProgramGetsTheFolderAndTheEnvironment()
    {
        if (OperatingSystem.IsWindows())
            return;

        var folder = Directory.CreateTempSubdirectory("runesmith-pty-").FullName;
        try
        {
            Environment.SetEnvironmentVariable("RUNESMITH_PTY_REMOVED", "still here");
            using var pty = PtyProcess.Start(new PtyStart("sh", ["-c", "pwd; echo \"[$RUNESMITH_PTY_VALUE][$RUNESMITH_PTY_REMOVED]\""], folder,
                new Dictionary<string, string?> { ["RUNESMITH_PTY_VALUE"] = "set", ["RUNESMITH_PTY_REMOVED"] = null }, 80, 24));
            var output = await ReadAllAsync(pty);

            Assert.Contains(Path.GetFileName(folder), output, StringComparison.Ordinal);
            Assert.Contains("[set][]", output, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RUNESMITH_PTY_REMOVED", null);
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ResizingTellsTheProgram()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var pty = Start("/bin/sh", ["-c", "stty size; read line; stty size"], columns: 80, rows: 24);
        var output = new StringBuilder();
        var reading = Task.Run(() => Pump(pty, output), TestContext.Current.CancellationToken);
        await WaitForAsync(() => Contains(output, "24 80"));

        pty.Resize(120, 40);
        pty.Write("\r"u8);

        await reading.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        Assert.Contains("40 120", Text(output), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheExitCodeIsReported()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var pty = Start("/bin/sh", ["-c", "exit 7"]);
        _ = await ReadAllAsync(pty);

        Assert.Equal(7, await pty.Exited.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CtrlCInterruptsTheProgramBecauseTheTerminalControlsIt()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var pty = Start("/bin/sh", ["-c", "echo ready; exec sleep 60"]);
        var output = new StringBuilder();
        _ = Task.Run(() => Pump(pty, output), TestContext.Current.CancellationToken);
        await WaitForAsync(() => Contains(output, "ready"));
        await Task.Delay(200, TestContext.Current.CancellationToken);

        pty.Write([0x03]);

        Assert.Equal(128 + 2, await pty.Exited.WaitAsync(Timeout, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task KillEndsTheProgram()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var pty = Start("/bin/sh", ["-c", "sleep 60"]);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        pty.Kill();

        Assert.True(await pty.Exited.WaitAsync(Timeout, TestContext.Current.CancellationToken) > 128);
    }

    [Fact]
    public void AProgramThatDoesNotExistCannotStart()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Throws<InvalidOperationException>(() => Start("/nonexistent/program", []));
    }

    [Fact]
    public void ProgramsAreFoundOnThePath()
    {
        if (OperatingSystem.IsWindows())
            return;

        var found = PtyProcess.FindProgram("sh", Environment.GetEnvironmentVariable("PATH"));

        Assert.True(Path.IsPathRooted(found));
        Assert.Equal("./tool", PtyProcess.FindProgram("./tool", "/usr/bin"));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("two words", "\"two words\"")]
    [InlineData("", "\"\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("C:\\path with space\\", "\"C:\\path with space\\\\\"")]
    public void WindowsArgumentsAreQuotedForTheCRuntime(string argument, string quoted) =>
        Assert.Equal(quoted, PtyProcess.Quote(argument));

    private static PtyProcess Start(string program, IReadOnlyList<string> arguments, int columns = 80, int rows = 24) =>
        PtyProcess.Start(new PtyStart(program, arguments, Path.GetTempPath(), new Dictionary<string, string?>(), columns, rows));

    private static async Task<string> ReadAllAsync(PtyProcess pty)
    {
        var output = new StringBuilder();
        await Task.Run(() => Pump(pty, output)).WaitAsync(Timeout);
        return Text(output);
    }

    private static void Pump(PtyProcess pty, StringBuilder output)
    {
        var buffer = new byte[4096];
        int read;
        while ((read = pty.Read(buffer)) > 0)
        {
            lock (output)
                output.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
    }

    private static bool Contains(StringBuilder output, string text) => Text(output).Contains(text, StringComparison.Ordinal);

    private static string Text(StringBuilder output)
    {
        lock (output)
            return output.ToString();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The terminal's output did not come in time.");
            await Task.Delay(20);
        }
    }
}
