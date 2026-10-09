namespace Runesmith.Lsp.Protocol;

public sealed record ReferenceParams(TextDocumentIdentifier TextDocument, Position Position, ReferenceContext Context)
    : TextDocumentPositionParams(TextDocument, Position);

/// <summary>Whether references include the declaration.</summary>
public sealed record ReferenceContext(bool IncludeDeclaration);
