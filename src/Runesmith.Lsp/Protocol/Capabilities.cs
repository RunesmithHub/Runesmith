using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Runesmith.Lsp.Protocol;

public enum TextDocumentSyncKind
{
    None = 0,
    Full = 1,
    Incremental = 2,
}

/// <summary>How the server wants documents synchronized, normalized from the number or object form.</summary>
public sealed record TextDocumentSyncOptions(bool OpenClose, TextDocumentSyncKind Change, bool Save, bool SaveIncludesText)
{
    public static TextDocumentSyncOptions None { get; } = new(false, TextDocumentSyncKind.None, false, false);
}

public sealed record CompletionOptions(IReadOnlyList<string>? TriggerCharacters, IReadOnlyList<string>? AllCommitCharacters, bool? ResolveProvider);

public sealed record SignatureHelpOptions(IReadOnlyList<string>? TriggerCharacters, IReadOnlyList<string>? RetriggerCharacters);

/// <summary>The features a server offers. Providers sent as either a flag or an options object arrive as a flag.</summary>
public sealed record ServerCapabilities
{
    [JsonConverter(typeof(TextDocumentSyncConverter))]
    public TextDocumentSyncOptions? TextDocumentSync { get; init; }

    public CompletionOptions? CompletionProvider { get; init; }

    [JsonConverter(typeof(FlagOrOptionsConverter))]
    public bool HoverProvider { get; init; }

    [JsonConverter(typeof(FlagOrOptionsConverter))]
    public bool DefinitionProvider { get; init; }

    public SignatureHelpOptions? SignatureHelpProvider { get; init; }

    [JsonConverter(typeof(FlagOrOptionsConverter))]
    public bool ReferencesProvider { get; init; }

    [JsonConverter(typeof(ProviderOptionsConverter))]
    public ProviderOptions? CodeActionProvider { get; init; }

    [JsonConverter(typeof(ProviderOptionsConverter))]
    public ProviderOptions? RenameProvider { get; init; }

    [JsonConverter(typeof(FlagOrOptionsConverter))]
    public bool DocumentFormattingProvider { get; init; }

    [JsonConverter(typeof(FlagOrOptionsConverter))]
    public bool DocumentRangeFormattingProvider { get; init; }

    [JsonConverter(typeof(ProviderOptionsConverter))]
    public ProviderOptions? InlayHintProvider { get; init; }

    [JsonConverter(typeof(ProviderOptionsConverter))]
    public ProviderOptions? CodeLensProvider { get; init; }

    public ExecuteCommandOptions? ExecuteCommandProvider { get; init; }
}

/// <summary>The commands a server runs with <c>workspace/executeCommand</c>.</summary>
public sealed record ExecuteCommandOptions(IReadOnlyList<string>? Commands);

public sealed record InitializeResult(ServerCapabilities Capabilities, ServerInfo? ServerInfo);

public sealed record InitializeParams(
    int? ProcessId,
    ClientInfo ClientInfo,
    string? RootUri,
    string? RootPath,
    JsonNode Capabilities,
    JsonElement? InitializationOptions,
    IReadOnlyList<WorkspaceFolder>? WorkspaceFolders);

/// <summary>The capabilities this client declares to every server.</summary>
internal static class ClientCapabilities
{
    public static JsonNode Create() => new JsonObject
    {
        ["workspace"] = new JsonObject
        {
            ["configuration"] = true,
            ["workspaceFolders"] = true,
            ["applyEdit"] = true,
            ["workspaceEdit"] = new JsonObject { ["documentChanges"] = true, ["resourceOperations"] = new JsonArray() },
            ["executeCommand"] = new JsonObject { ["dynamicRegistration"] = false },
            ["inlayHint"] = new JsonObject { ["refreshSupport"] = true },
            ["codeLens"] = new JsonObject { ["refreshSupport"] = true },
        },
        ["textDocument"] = new JsonObject
        {
            ["synchronization"] = new JsonObject { ["didSave"] = true, ["willSave"] = false, ["dynamicRegistration"] = false },
            ["completion"] = new JsonObject
            {
                ["completionItem"] = new JsonObject
                {
                    ["snippetSupport"] = false,
                    ["commitCharactersSupport"] = true,
                    ["documentationFormat"] = new JsonArray("markdown", "plaintext"),
                    ["insertReplaceSupport"] = true,
                    ["preselectSupport"] = true,
                },
                ["contextSupport"] = true,
            },
            ["hover"] = new JsonObject { ["contentFormat"] = new JsonArray("markdown", "plaintext") },
            ["codeAction"] = new JsonObject
            {
                ["codeActionLiteralSupport"] = new JsonObject
                {
                    ["codeActionKind"] = new JsonObject { ["valueSet"] = new JsonArray("", "quickfix", "refactor", "refactor.extract", "refactor.inline", "refactor.rewrite", "source", "source.organizeImports") },
                },
                ["isPreferredSupport"] = true,
                ["disabledSupport"] = true,
                ["dataSupport"] = true,
                ["resolveSupport"] = new JsonObject { ["properties"] = new JsonArray("edit") },
            },
            ["rename"] = new JsonObject { ["prepareSupport"] = true },
            ["formatting"] = new JsonObject { ["dynamicRegistration"] = false },
            ["rangeFormatting"] = new JsonObject { ["dynamicRegistration"] = false },
            ["inlayHint"] = new JsonObject { ["resolveSupport"] = new JsonObject { ["properties"] = new JsonArray() } },
            ["codeLens"] = new JsonObject { ["dynamicRegistration"] = false },
            ["definition"] = new JsonObject { ["linkSupport"] = true },
            ["signatureHelp"] = new JsonObject
            {
                ["signatureInformation"] = new JsonObject
                {
                    ["documentationFormat"] = new JsonArray("markdown", "plaintext"),
                    ["parameterInformation"] = new JsonObject { ["labelOffsetSupport"] = true },
                    ["activeParameterSupport"] = true,
                },
                ["contextSupport"] = false,
            },
            ["publishDiagnostics"] = new JsonObject { ["relatedInformation"] = false, ["versionSupport"] = true, ["tagSupport"] = new JsonObject { ["valueSet"] = new JsonArray(1, 2) } },
        },
        ["window"] = new JsonObject { ["workDoneProgress"] = true, ["showMessage"] = new JsonObject() },
        ["general"] = new JsonObject { ["positionEncodings"] = new JsonArray("utf-16") },
    };
}
