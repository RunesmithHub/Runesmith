namespace Runesmith.Lsp.Protocol;

/// <summary>A zero-based line and UTF-16 character offset in a document.</summary>
public sealed record Position(int Line, int Character);

/// <summary>A range between two positions; the end is exclusive.</summary>
public sealed record Range(Position Start, Position End);

/// <summary>A range in a document, by URI.</summary>
public sealed record Location(string Uri, Range Range);

/// <summary>A link to a target range, as servers return it for definitions when the client supports links.</summary>
public sealed record LocationLink(Range? OriginSelectionRange, string TargetUri, Range TargetRange, Range TargetSelectionRange);

/// <summary>Replaces a range with new text.</summary>
public sealed record TextEdit(Range Range, string NewText);

/// <summary>A completion edit with separate ranges for inserting and for replacing.</summary>
public sealed record InsertReplaceEdit(string NewText, Range Insert, Range Replace);

/// <summary>Identifies a document by URI.</summary>
public sealed record TextDocumentIdentifier(string Uri);

/// <summary>Identifies a version of a document.</summary>
public sealed record VersionedTextDocumentIdentifier(string Uri, int Version);

/// <summary>A document's full content as the client opens it.</summary>
public sealed record TextDocumentItem(string Uri, string LanguageId, int Version, string Text);

/// <summary>A change to a document: the text that replaces <see cref="Range"/>, or the whole document when the range is null.</summary>
public sealed record TextDocumentContentChangeEvent(Range? Range, string Text);

/// <summary>A document and a position in it.</summary>
public record TextDocumentPositionParams(TextDocumentIdentifier TextDocument, Position Position);

public sealed record DidOpenTextDocumentParams(TextDocumentItem TextDocument);

public sealed record DidChangeTextDocumentParams(VersionedTextDocumentIdentifier TextDocument, IReadOnlyList<TextDocumentContentChangeEvent> ContentChanges);

public sealed record DidSaveTextDocumentParams(TextDocumentIdentifier TextDocument, string? Text);

public sealed record DidCloseTextDocumentParams(TextDocumentIdentifier TextDocument);

/// <summary>Text with its format, <see cref="MarkupKind.PlainText"/> or <see cref="MarkupKind.Markdown"/>.</summary>
public sealed record MarkupContent(string Kind, string Value);

/// <summary>The formats of <see cref="MarkupContent"/>.</summary>
public static class MarkupKind
{
    public const string PlainText = "plaintext";
    public const string Markdown = "markdown";
}

/// <summary>A folder of the workspace.</summary>
public sealed record WorkspaceFolder(string Uri, string Name);

/// <summary>An item a server asks the client's configuration for.</summary>
public sealed record ConfigurationItem(string? ScopeUri, string? Section);

public sealed record ConfigurationParams(IReadOnlyList<ConfigurationItem> Items);

/// <summary>The client's name and version, sent in the initialize request.</summary>
public sealed record ClientInfo(string Name, string? Version);

/// <summary>The server's name and version, from the initialize result.</summary>
public sealed record ServerInfo(string Name, string? Version);
