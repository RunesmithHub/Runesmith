using System.Text.Json.Nodes;
using Runesmith.Dap;
using Runesmith.Sdk.Debugging;
using Runesmith.Sdk.Running;
using Runesmith.Shell.Debugging;
using Runesmith.Shell.Tests.Running;
using Runesmith.Tests.Dap;

namespace Runesmith.Shell.Tests.Debugging;

/// <summary>A debug adapter scripted like a small program paused in <c>Program.cs</c>: it answers the start-up requests, and stops at the line
/// <see cref="Line"/> when told, with a frame without source above the program's frame.</summary>
internal sealed class ScriptedProgram : IAsyncDisposable
{
    public ScriptedProgram(string programPath, string capabilities = """{ "supportsConfigurationDoneRequest": true, "supportsTerminateRequest": true, "exceptionBreakpointFilters": [ { "filter": "all", "label": "All exceptions" }, { "filter": "unhandled", "label": "Unhandled exceptions", "default": true } ] }""")
    {
        ProgramPath = programPath;
        Adapter.On("initialize", _ => FakeReply.Success(capabilities) with { Then = [("initialized", null)] });
        Adapter.On("setBreakpoints", arguments => FakeReply.Success(new JsonObject
        {
            ["breakpoints"] = new JsonArray([.. arguments!["breakpoints"]!.AsArray().Select((b, i) => (JsonNode)new JsonObject
            {
                ["id"] = 100 + b!["line"]!.GetValue<int>(),
                ["verified"] = b["line"]!.GetValue<int>() != UnverifiedLine,
                ["line"] = b["line"]!.GetValue<int>(),
                ["message"] = b["line"]!.GetValue<int>() == UnverifiedLine ? "No code on this line" : null,
            })]),
        }.ToJsonString()));
        Adapter.On("threads", """{ "threads": [ { "id": 1, "name": "Main Thread" }, { "id": 2, "name": "Worker" } ] }""");
        Adapter.On("stackTrace", arguments => FakeReply.Success(new JsonObject
        {
            ["stackFrames"] = new JsonArray(
                new JsonObject { ["id"] = 10, ["name"] = "System.Console.WriteLine()", ["line"] = 0, ["column"] = 0 },
                new JsonObject { ["id"] = 11, ["name"] = arguments!["threadId"]!.GetValue<int>() == 2 ? "Worker.Run()" : "Program.Main()", ["line"] = Line, ["column"] = 5, ["source"] = new JsonObject { ["path"] = ProgramPath } },
                new JsonObject { ["id"] = 12, ["name"] = "Program.<Main>()", ["line"] = 1, ["column"] = 1, ["source"] = new JsonObject { ["path"] = ProgramPath } }),
        }.ToJsonString()));
        Adapter.On("scopes", """{ "scopes": [ { "name": "Locals", "variablesReference": 1 }, { "name": "Globals", "variablesReference": 2, "expensive": true } ] }""");
        Adapter.On("variables", arguments => arguments!["variablesReference"]!.GetValue<int>() switch
        {
            1 => FakeReply.Success("""{ "variables": [ { "name": "count", "value": "3", "type": "int", "variablesReference": 0 }, { "name": "person", "value": "{Person}", "type": "Person", "variablesReference": 5 } ] }"""),
            5 => FakeReply.Success("""{ "variables": [ { "name": "Name", "value": "\"Ada\"", "type": "string", "variablesReference": 0 } ] }"""),
            _ => FakeReply.Success("""{ "variables": [] }"""),
        });
        Adapter.On("evaluate", arguments => arguments!["expression"]!.GetValue<string>() switch
        {
            "count * 2" => FakeReply.Success("""{ "result": "6", "type": "int", "variablesReference": 0 }"""),
            "person" => FakeReply.Success("""{ "result": "{Person}", "type": "Person", "variablesReference": 5 }"""),
            _ => FakeReply.Failure("evaluate failed", """{ "error": { "id": 1, "format": "The name is not in scope" } }"""),
        });
        Adapter.On("next", _ =>
        {
            Line++;
            return FakeReply.Success() with { Then = [("stopped", """{ "reason": "step", "threadId": 1, "allThreadsStopped": true }""")] };
        });
        Adapter.On("continue", _ => FakeReply.Success("""{ "allThreadsContinued": true }"""));
        Adapter.On("terminate", _ => FakeReply.Success() with { Then = [("exited", """{ "exitCode": 0 }"""), ("terminated", null)] });
        Client = new DebugAdapterClient(Adapter.ClientInput, Adapter.ClientOutput);
    }

    /// <summary>The line, counted from 1, setBreakpoints says it cannot set.</summary>
    public const int UnverifiedLine = 40;

    public FakeDebugAdapter Adapter { get; } = new();

    public DebugAdapterClient Client { get; }

    public string ProgramPath { get; }

    /// <summary>Gets or sets the line, counted from 1, the program's frame is on.</summary>
    public int Line { get; set; } = 5;

    public bool Released { get; private set; }

    public async ValueTask ReleaseAsync()
    {
        Released = true;
        await Client.DisposeAsync();
    }

    public Task StopAtBreakpointAsync() => Adapter.SendEventAsync("stopped", """{ "reason": "breakpoint", "threadId": 1, "allThreadsStopped": true, "hitBreakpointIds": [105] }""");

    public async Task ExitAsync(int code)
    {
        await Adapter.SendEventAsync("exited", $$"""{ "exitCode": {{code}} }""");
        await Adapter.SendEventAsync("terminated");
    }

    public async ValueTask DisposeAsync()
    {
        await Client.DisposeAsync();
        await Adapter.DisposeAsync();
    }
}

/// <summary>A debug engine for the <c>dotnet</c> debugger that launches the plan's program with fixed arguments.</summary>
internal sealed class FakeEngine(DebugAdapterDescriptor? adapter = null) : IDebugAdapterProvider
{
    public IReadOnlyList<string> Debuggers => ["dotnet"];

    public string Name => "Fake debugger";

    public List<DebugAdapterContext> Contexts { get; } = [];

    public Task<DebugAdapterDescriptor> CreateAdapterAsync(DebugAdapterContext context, CancellationToken cancellationToken)
    {
        Contexts.Add(context);
        return Task.FromResult(adapter ?? new DebugAdapterExecutable("fake-adapter", ["--dap"]));
    }

    public Task<DebugAdapterRequest> CreateRequestAsync(DebugAdapterContext context, CancellationToken cancellationToken) =>
        Task.FromResult(new DebugAdapterRequest(DebugRequestKind.Launch, new JsonObject { ["program"] = context.Launch.Settings["program"] }) { AdapterId = "fake" });
}

internal static class DebugTesting
{
    public static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public static LaunchPlan Plan(string program) =>
        new("dotnet", [program], Path.GetDirectoryName(program)!) { Debug = new DebugLaunch("dotnet", new Dictionary<string, string> { ["program"] = program }) };

    public static BreakpointService Breakpoints(string root, string state) => new(new FakeWorkspace(root), null, state);

    /// <summary>Waits until a condition holds, checking every few milliseconds.</summary>
    public static async Task WaitAsync(Func<bool> condition, string? what = null)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, what ?? "The condition was not met in time.");
            await Task.Delay(5, Token);
        }
    }
}
