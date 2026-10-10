using System.Text.Json.Nodes;
using Runesmith.Sdk.Running;

namespace Runesmith.Sdk.Debugging;

/// <summary>A debug engine: starts a debug adapter, a debugger that speaks the Debug Adapter Protocol, for the debuggers it serves. Export it
/// with <c>[Export(typeof(IDebugAdapterProvider))]</c>.</summary>
/// <remarks>When a run configuration is debugged, Runesmith takes the <see cref="LaunchPlan.Debug"/> its type prepared, picks the provider
/// whose <see cref="Debuggers"/> has the plan's <see cref="DebugLaunch.Debugger"/>, starts or reaches the adapter that
/// <see cref="CreateAdapterAsync"/> describes, and sends it the request that <see cref="CreateRequestAsync"/> returns. Runesmith does the rest:
/// breakpoints, stepping, threads, variables, watches and the debug console. Starting an adapter process needs the <c>process</c> capability
/// and reaching one over a socket the <c>network</c> capability; a plugin that did not declare it is refused. Both methods are called on a
/// background thread.</remarks>
public interface IDebugAdapterProvider
{
    /// <summary>Gets the debuggers it serves, as run configurations name them in <see cref="DebugLaunch.Debugger"/>, such as <c>dotnet</c>.</summary>
    IReadOnlyList<string> Debuggers { get; }

    /// <summary>Gets the debugger's name for the user, such as "netcoredbg".</summary>
    string Name { get; }

    /// <summary>Says how to start or reach the adapter for a launch, such as by downloading it on first use and returning its executable.</summary>
    /// <exception cref="InvalidOperationException">The adapter cannot be started; the message tells the user why.</exception>
    Task<DebugAdapterDescriptor> CreateAdapterAsync(DebugAdapterContext context, CancellationToken cancellationToken);

    /// <summary>Turns a launch into the adapter's <c>launch</c> or <c>attach</c> request.</summary>
    Task<DebugAdapterRequest> CreateRequestAsync(DebugAdapterContext context, CancellationToken cancellationToken);
}

/// <summary>What a debug engine gets when a run configuration is debugged.</summary>
/// <param name="Plan">The launch plan the configuration's type prepared in debug mode.</param>
/// <param name="Launch">The plan's <see cref="LaunchPlan.Debug"/>: the debugger and its settings.</param>
/// <param name="RootPath">The open folder.</param>
public sealed record DebugAdapterContext(LaunchPlan Plan, DebugLaunch Launch, string RootPath);

/// <summary>How to start or reach a debug adapter: a <see cref="DebugAdapterExecutable"/> or a <see cref="DebugAdapterServer"/>.</summary>
public abstract record DebugAdapterDescriptor;

/// <summary>A debug adapter Runesmith starts as a child process and talks to over its standard input and output.</summary>
/// <param name="Command">The executable, found on the PATH unless it is a full path.</param>
/// <param name="Arguments">Its arguments, unquoted.</param>
public sealed record DebugAdapterExecutable(string Command, IReadOnlyList<string> Arguments) : DebugAdapterDescriptor
{
    /// <summary>Gets the folder it starts in, or null for the open folder.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Gets environment variables to set, besides Runesmith's own environment.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}

/// <summary>A debug adapter that already listens on a socket, such as one the engine started itself.</summary>
public sealed record DebugAdapterServer(string Host, int Port) : DebugAdapterDescriptor;

/// <summary>Whether a debug session starts the program or attaches to a running one.</summary>
public enum DebugRequestKind
{
    Launch,
    Attach,
}

/// <summary>The request that starts a debug session, with the adapter's own arguments.</summary>
/// <param name="Kind">Whether to send <c>launch</c> or <c>attach</c>.</param>
/// <param name="Arguments">The request's arguments as the adapter defines them, such as <c>program</c>, <c>args</c>, <c>cwd</c> and
/// <c>env</c>.</param>
public sealed record DebugAdapterRequest(DebugRequestKind Kind, JsonObject Arguments)
{
    /// <summary>Gets the id sent to the adapter as <c>adapterID</c> in <c>initialize</c>, or null for the launch's debugger.</summary>
    public string? AdapterId { get; init; }
}
