using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Runesmith.Dap.Protocol;

/// <summary>The source-generated JSON serialization of the protocol types: camelCase names, nulls left out.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InitializeArguments))]
[JsonSerializable(typeof(Capabilities))]
[JsonSerializable(typeof(SetBreakpointsArguments))]
[JsonSerializable(typeof(SetBreakpointsResponse))]
[JsonSerializable(typeof(SetExceptionBreakpointsArguments))]
[JsonSerializable(typeof(ThreadsResponse))]
[JsonSerializable(typeof(StackTraceArguments))]
[JsonSerializable(typeof(StackTraceResponse))]
[JsonSerializable(typeof(ScopesArguments))]
[JsonSerializable(typeof(ScopesResponse))]
[JsonSerializable(typeof(VariablesArguments))]
[JsonSerializable(typeof(VariablesResponse))]
[JsonSerializable(typeof(EvaluateArguments))]
[JsonSerializable(typeof(EvaluateResponse))]
[JsonSerializable(typeof(ThreadArguments))]
[JsonSerializable(typeof(ContinueResponse))]
[JsonSerializable(typeof(DisconnectArguments))]
[JsonSerializable(typeof(TerminateArguments))]
[JsonSerializable(typeof(CancelArguments))]
[JsonSerializable(typeof(StoppedEvent))]
[JsonSerializable(typeof(ContinuedEvent))]
[JsonSerializable(typeof(ExitedEvent))]
[JsonSerializable(typeof(TerminatedEvent))]
[JsonSerializable(typeof(ThreadEvent))]
[JsonSerializable(typeof(OutputEvent))]
[JsonSerializable(typeof(BreakpointEvent))]
[JsonSerializable(typeof(CapabilitiesEvent))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonElement))]
public sealed partial class DapJsonContext : JsonSerializerContext;
