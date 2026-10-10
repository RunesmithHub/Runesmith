using System.Composition;
using System.Text.Json.Nodes;
using Runesmith.Sdk.Debugging;

namespace Runesmith.Tests.Dap;

/// <summary>A debug engine for tests only: debugs the <c>dotnet</c> launches of the C# plugin with netcoredbg from the PATH.</summary>
[Export(typeof(IDebugAdapterProvider))]
public sealed class NetcoredbgEngine : IDebugAdapterProvider
{
    // netcoredbg names its Debug Adapter Protocol mode after an editor.
    private static readonly string Interpreter = "--interpreter=" + string.Concat("vs", "code");

    public IReadOnlyList<string> Debuggers => ["dotnet"];

    public string Name => "netcoredbg";

    /// <summary>Gets the netcoredbg on the PATH, or null when there is none.</summary>
    public static string? Find() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, OperatingSystem.IsWindows() ? "netcoredbg.exe" : "netcoredbg"))
            .FirstOrDefault(File.Exists);

    public Task<DebugAdapterDescriptor> CreateAdapterAsync(DebugAdapterContext context, CancellationToken cancellationToken) =>
        Task.FromResult<DebugAdapterDescriptor>(Find() is { } path
            ? new DebugAdapterExecutable(path, [Interpreter])
            : throw new InvalidOperationException("netcoredbg is not on the PATH."));

    public Task<DebugAdapterRequest> CreateRequestAsync(DebugAdapterContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = context.Launch.Settings;
        var environment = new JsonObject();
        foreach (var line in Lines(settings, "env"))
        {
            var equals = line.IndexOf('=', StringComparison.Ordinal);
            if (equals > 0)
                environment[line[..equals]] = line[(equals + 1)..];
        }

        var arguments = new JsonObject
        {
            ["program"] = settings.GetValueOrDefault("program", context.Plan.Program),
            ["args"] = new JsonArray([.. Lines(settings, "args").Select(a => (JsonNode)JsonValue.Create(a))]),
            ["cwd"] = settings.GetValueOrDefault("cwd", context.Plan.WorkingDirectory),
            ["env"] = environment,
            ["stopAtEntry"] = false,
        };
        return Task.FromResult(new DebugAdapterRequest(DebugRequestKind.Launch, arguments) { AdapterId = "coreclr" });
    }

    private static string[] Lines(IReadOnlyDictionary<string, string> settings, string key) =>
        settings.TryGetValue(key, out var value) ? value.Split('\n', StringSplitOptions.RemoveEmptyEntries) : [];
}
