using System.Text.Json;
using System.Text.Json.Nodes;
using Runesmith.Dap.Protocol;
using Runesmith.Tests.Dap;

namespace Runesmith.Dap.Tests;

public sealed class DebugAdapterClientTests : IAsyncDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly FakeDebugAdapter adapter = new();
    private readonly DebugAdapterClient client;

    public DebugAdapterClientTests()
    {
        client = new DebugAdapterClient(adapter.ClientInput, adapter.ClientOutput);
        client.Start();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await client.DisposeAsync();
        await adapter.DisposeAsync();
    }

    [Fact]
    public async Task InitializeSendsTheClientsSettingsAndKeepsTheCapabilities()
    {
        adapter.On("initialize", """
            { "supportsConfigurationDoneRequest": true, "supportsConditionalBreakpoints": true,
              "exceptionBreakpointFilters": [ { "filter": "all", "label": "All exceptions" }, { "filter": "user-unhandled", "label": "Unhandled", "default": true } ] }
            """);

        var capabilities = await client.InitializeAsync(new InitializeArguments("coreclr") { ClientId = "runesmith", ClientName = "Runesmith" }, Token);

        Assert.True(capabilities.SupportsConfigurationDoneRequest);
        Assert.True(client.Capabilities.SupportsConditionalBreakpoints);
        Assert.False(capabilities.SupportsLogPoints);
        Assert.Equal(["all", "user-unhandled"], capabilities.ExceptionBreakpointFilters!.Select(f => f.Filter));
        Assert.True(capabilities.ExceptionBreakpointFilters![1].Default);
        var sent = adapter.Arguments("initialize")!;
        Assert.Equal("coreclr", sent["adapterId"]!.GetValue<string>());
        Assert.True(sent["linesStartAt1"]!.GetValue<bool>());
        Assert.Equal("path", sent["pathFormat"]!.GetValue<string>());
    }

    [Fact]
    public async Task MatchesResponsesToRequestsWhateverOrderTheyComeIn()
    {
        adapter.On("threads", _ => FakeReply.Success("""{ "threads": [ { "id": 1, "name": "Main" } ] }""") with { Delay = TimeSpan.FromMilliseconds(150) });
        adapter.On("scopes", """{ "scopes": [ { "name": "Locals", "variablesReference": 7 } ] }""");

        var threads = client.ThreadsAsync(Token);
        var scopes = await client.ScopesAsync(3, Token);

        Assert.False(threads.IsCompleted);
        Assert.Equal("Locals", Assert.Single(scopes).Name);
        Assert.Equal(3, adapter.Arguments("scopes")!["frameId"]!.GetValue<int>());
        Assert.Equal("Main", Assert.Single(await threads.WaitAsync(Timeout, Token)).Name);
    }

    [Fact]
    public async Task SendsTypedArgumentsAndReadsTypedBodies()
    {
        adapter.On("setBreakpoints", """{ "breakpoints": [ { "verified": true, "line": 4, "id": 1 }, { "verified": false, "message": "No code" } ] }""");
        adapter.On("stackTrace", """{ "stackFrames": [ { "id": 10, "name": "Main", "line": 4, "column": 1, "source": { "path": "/p/Program.cs" } } ], "totalFrames": 1 }""");
        adapter.On("variables", """{ "variables": [ { "name": "count", "value": "3", "type": "int", "variablesReference": 0 } ] }""");
        adapter.On("evaluate", """{ "result": "6", "type": "int", "variablesReference": 0 }""");

        var breakpoints = await client.SetBreakpointsAsync(new SetBreakpointsArguments(new Source { Path = "/p/Program.cs" },
            [new SourceBreakpoint(4), new SourceBreakpoint(9) { Condition = "i > 2", LogMessage = "i is {i}" }]), Token);
        var stack = await client.StackTraceAsync(new StackTraceArguments(1), Token);
        var variables = await client.VariablesAsync(new VariablesArguments(7), Token);
        var result = await client.EvaluateAsync(new EvaluateArguments("count * 2") { FrameId = 10, Context = "watch" }, Token);

        Assert.Equal([true, false], breakpoints.Select(b => b.Verified));
        Assert.Equal("No code", breakpoints[1].Message);
        var sent = adapter.Arguments("setBreakpoints")!;
        Assert.Equal("/p/Program.cs", sent["source"]!["path"]!.GetValue<string>());
        Assert.Equal("i > 2", sent["breakpoints"]![1]!["condition"]!.GetValue<string>());
        Assert.Null(sent["breakpoints"]![0]!["condition"]);
        Assert.Equal("/p/Program.cs", Assert.Single(stack.StackFrames).Source!.Path);
        Assert.Equal(("count", "3", "int"), (variables[0].Name, variables[0].Value, variables[0].Type));
        Assert.Equal("6", result.Result);
        Assert.Equal("watch", adapter.Arguments("evaluate")!["context"]!.GetValue<string>());
    }

    [Fact]
    public async Task AnErrorAnswerThrowsWithTheAdaptersMessage()
    {
        adapter.On("evaluate", _ => FakeReply.Failure("evaluate failed", """{ "error": { "id": 2001, "format": "The name '{name}' does not exist", "variables": { "name": "x" } } }"""));
        adapter.On("next", _ => FakeReply.Failure("notStopped"));

        var evaluate = await Assert.ThrowsAsync<DebugAdapterException>(() => client.EvaluateAsync(new EvaluateArguments("x"), Token));
        var next = await Assert.ThrowsAsync<DebugAdapterException>(() => client.NextAsync(1, Token));

        Assert.Equal("The name 'x' does not exist", evaluate.Message);
        Assert.Equal(2001, evaluate.ErrorId);
        Assert.Equal("evaluate", evaluate.Command);
        Assert.Equal("notStopped", next.Message);
    }

    [Fact]
    public async Task RaisesEventsInTheOrderTheyCameOffTheUiThread()
    {
        var seen = new List<string>();
        var done = new TaskCompletionSource();
        client.Output += (_, e) => seen.Add($"output {e.Category}: {e.Output}");
        client.Stopped += (_, e) => seen.Add($"stopped {e.Reason} {e.ThreadId}");
        client.Continued += (_, e) => seen.Add($"continued {e.ThreadId}");
        client.ThreadChanged += (_, e) => seen.Add($"thread {e.Reason} {e.ThreadId}");
        client.BreakpointChanged += (_, e) => seen.Add($"breakpoint {e.Reason} {e.Breakpoint.Verified}");
        client.Exited += (_, e) => seen.Add($"exited {e.ExitCode}");
        client.OtherEvent += (_, e) => seen.Add($"other {e.Event}");
        client.Terminated += (_, _) =>
        {
            seen.Add("terminated");
            done.TrySetResult();
        };

        await adapter.SendEventAsync("initialized");
        await adapter.SendEventAsync("thread", """{ "reason": "started", "threadId": 1 }""");
        await adapter.SendEventAsync("output", """{ "category": "stdout", "output": "hello\n" }""");
        await adapter.SendEventAsync("breakpoint", """{ "reason": "changed", "breakpoint": { "verified": true, "id": 1 } }""");
        await adapter.SendEventAsync("stopped", """{ "reason": "breakpoint", "threadId": 1, "allThreadsStopped": true }""");
        await adapter.SendEventAsync("module", """{ "reason": "new", "module": { "id": 1, "name": "App.dll" } }""");
        await adapter.SendEventAsync("continued", """{ "threadId": 1 }""");
        await adapter.SendEventAsync("exited", """{ "exitCode": 3 }""");
        await adapter.SendEventAsync("terminated");
        await done.Task.WaitAsync(Timeout, Token);

        await client.Initialized.WaitAsync(Timeout, Token);
        Assert.Equal(
        [
            "thread started 1", "output stdout: hello\n", "breakpoint changed True", "stopped breakpoint 1", "other module", "continued 1", "exited 3",
            "terminated",
        ], seen);
    }

    [Fact]
    public async Task CancelingARequestCancelsItsTaskAndTellsAnAdapterThatSupportsIt()
    {
        adapter.On("initialize", """{ "supportsCancelRequest": true }""");
        adapter.On("evaluate", _ => FakeReply.None);
        await client.InitializeAsync(new InitializeArguments("fake"), Token);
        using var cancel = new CancellationTokenSource();

        var evaluate = client.EvaluateAsync(new EvaluateArguments("slow()"), cancel.Token);
        await adapter.WaitForAsync("evaluate");
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => evaluate);
        await adapter.WaitForAsync("cancel");
        Assert.True(adapter.Arguments("cancel")!["requestId"]!.GetValue<int>() > 0);
    }

    [Fact]
    public async Task CancelingDoesNotSendCancelToAnAdapterWithoutIt()
    {
        adapter.On("evaluate", _ => FakeReply.None);
        using var cancel = new CancellationTokenSource();

        var evaluate = client.EvaluateAsync(new EvaluateArguments("slow()"), cancel.Token);
        await adapter.WaitForAsync("evaluate");
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => evaluate);
        await client.ThreadsAsync(Token);

        Assert.DoesNotContain("cancel", adapter.Commands);
    }

    [Fact]
    public async Task AnAdapterThatCrashesFailsWaitingRequestsAndRaisesClosed()
    {
        adapter.On("continue", _ => FakeReply.None);
        var closed = new TaskCompletionSource<Exception?>();
        client.Closed += (_, failure) => closed.TrySetResult(failure);

        var waiting = client.ContinueAsync(1, Token);
        await adapter.WaitForAsync("continue");
        adapter.Crash();

        await Assert.ThrowsAsync<IOException>(() => waiting.WaitAsync(Timeout, Token));
        await closed.Task.WaitAsync(Timeout, Token);
        Assert.True(client.IsClosed);
        await Assert.ThrowsAsync<IOException>(() => client.ThreadsAsync(Token));
        await Assert.ThrowsAsync<IOException>(() => client.Initialized.WaitAsync(Timeout, Token));
    }

    [Fact]
    public async Task GarbageFromTheAdapterClosesTheClientInsteadOfThrowing()
    {
        var closed = new TaskCompletionSource<Exception?>();
        client.Closed += (_, failure) => closed.TrySetResult(failure);

        await adapter.WriteRawAsync("Content-Length: 5\r\n\r\nnope!"u8.ToArray());

        Assert.IsAssignableFrom<JsonException>(await closed.Task.WaitAsync(Timeout, Token));
    }

    [Fact]
    public async Task AnswersTheAdaptersRequestsWithTheHandlerOrAsNotSupported()
    {
        var refused = await adapter.SendRequestAsync("runInTerminal", """{ "args": ["dotnet", "App.dll"] }""");
        client.RequestHandler = (command, arguments, _) =>
            Task.FromResult<object?>(new JsonObject { ["processId"] = arguments!.Value.GetProperty("args").GetArrayLength() });
        var answered = await adapter.SendRequestAsync("runInTerminal", """{ "args": ["dotnet", "App.dll"] }""");

        Assert.False(refused["success"]!.GetValue<bool>());
        Assert.Contains("not supported", refused["message"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.True(answered["success"]!.GetValue<bool>());
        Assert.Equal(2, answered["body"]!["processId"]!.GetValue<int>());
    }

    [Fact]
    public async Task ACapabilitiesEventAddsToTheCapabilities()
    {
        adapter.On("initialize", """{ "supportsConfigurationDoneRequest": true }""");
        await client.InitializeAsync(new InitializeArguments("fake"), Token);
        var seen = new TaskCompletionSource();
        client.OtherEvent += (_, _) => seen.TrySetResult();

        await adapter.SendEventAsync("capabilities", """{ "capabilities": { "supportsLogPoints": true } }""");
        await adapter.SendEventAsync("process", """{ "name": "App" }""");
        await seen.Task.WaitAsync(Timeout, Token);

        Assert.True(client.Capabilities.SupportsLogPoints);
        Assert.True(client.Capabilities.SupportsConfigurationDoneRequest);
    }

    [Fact]
    public async Task LaunchSendsTheProvidersArgumentsAsTheyAre()
    {
        adapter.On("initialize", """{ "supportsConfigurationDoneRequest": true, "supportsTerminateRequest": true }""");
        await client.InitializeAsync(new InitializeArguments("fake"), Token);

        await client.LaunchAsync(new JsonObject { ["program"] = "/p/App.dll", ["args"] = new JsonArray("a", "b c"), ["stopAtEntry"] = false }, Token);
        await client.ConfigurationDoneAsync(Token);
        Assert.True(await client.TerminateAsync(Token));
        await client.DisconnectAsync(new DisconnectArguments { TerminateDebuggee = true }, Token);

        Assert.Equal(["initialize", "launch", "configurationDone", "terminate", "disconnect"], adapter.Commands);
        Assert.Equal("b c", adapter.Arguments("launch")!["args"]![1]!.GetValue<string>());
        Assert.True(adapter.Arguments("disconnect")!["terminateDebuggee"]!.GetValue<bool>());
    }

    [Fact]
    public async Task SkipsConfigurationDoneAndTerminateWhenTheAdapterLacksThem()
    {
        await client.InitializeAsync(new InitializeArguments("fake"), Token);

        await client.ConfigurationDoneAsync(Token);
        Assert.False(await client.TerminateAsync(Token));

        Assert.Equal(["initialize"], adapter.Commands);
    }
}
