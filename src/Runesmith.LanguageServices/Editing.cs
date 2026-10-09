using Runesmith.Text;

namespace Runesmith.LanguageServices;

/// <summary>Replaces the text between two positions.</summary>
public sealed record PositionEdit(TextPosition Start, TextPosition End, string NewText);

/// <summary>Edits to one file.</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="Version">The open document version the positions refer to, or null when they refer to the file on disk.</param>
public sealed record FileEdit(string Path, int? Version, IReadOnlyList<PositionEdit> Edits);

/// <summary>A quick fix or refactoring an analyzer offers.</summary>
/// <param name="Title">What the menu shows.</param>
/// <param name="IsRefactoring">Whether the action restructures code rather than fixing a problem.</param>
/// <param name="Token">What the analyzer needs to compute the action's edits later.</param>
public sealed record CodeActionEntry(string Title, bool IsRefactoring, object Token)
{
    /// <summary>Gets whether the action is the most likely fix.</summary>
    public bool IsPreferred { get; init; }

    /// <summary>Gets the analyzer that made the entry; set by the host.</summary>
    public ILanguageAnalyzer? Analyzer { get; set; }
}

/// <summary>The name a rename starts from and where it is.</summary>
public sealed record RenameSite(TextSpan Span, string Name);

/// <summary>Thrown by an analyzer to tell the user why a request cannot be done, such as a symbol that cannot be renamed; the host passes it to
/// the caller rather than logging a failure.</summary>
public sealed class AnalyzerRefusalException : Exception
{
    public AnalyzerRefusalException()
    {
    }

    public AnalyzerRefusalException(string message)
        : base(message)
    {
    }

    public AnalyzerRefusalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>An analyzer that offers quick fixes and refactorings.</summary>
public interface ICodeActionAnalyzer
{
    /// <summary>Gets the actions for a span of a document version; with <paramref name="includeRefactorings"/> false, only the quick fixes
    /// for the problems in the span, which must be quick to find.</summary>
    ValueTask<IReadOnlyList<CodeActionEntry>> CodeActionsAsync(SourceDocument document, TextSpan span, bool includeRefactorings, CancellationToken cancellationToken);

    /// <summary>Computes the edits of an action this analyzer offered.</summary>
    ValueTask<IReadOnlyList<FileEdit>> ResolveCodeActionAsync(CodeActionEntry entry, CancellationToken cancellationToken);
}

/// <summary>An analyzer that renames symbols.</summary>
public interface IRenameAnalyzer
{
    /// <summary>Finds the name at a position that can be renamed, or null; throws <see cref="AnalyzerRefusalException"/> to say why not.</summary>
    ValueTask<RenameSite?> PrepareRenameAsync(DocumentPosition position, CancellationToken cancellationToken);

    /// <summary>Computes the edits that rename the symbol at a position.</summary>
    ValueTask<IReadOnlyList<FileEdit>> RenameAsync(DocumentPosition position, string newName, CancellationToken cancellationToken);
}

/// <summary>An analyzer that formats documents.</summary>
public interface IFormattingAnalyzer
{
    /// <summary>Gets the changes that format a document version, or only a span of it, in that version's coordinates.</summary>
    ValueTask<IReadOnlyList<TextChange>> FormatAsync(SourceDocument document, TextSpan? span, int tabSize, bool insertSpaces, CancellationToken cancellationToken);
}

/// <summary>Faint text an analyzer shows inside a line, such as a parameter name before an argument.</summary>
/// <param name="IsType">Whether the hint is a type that follows the text before it, such as <c>: int</c> after a variable's name; otherwise it
/// precedes the text after it.</param>
public sealed record InlayHintEntry(int Offset, string Text, bool IsType);

/// <summary>An analyzer that shows inlay hints.</summary>
public interface IInlayHintAnalyzer
{
    /// <summary>Gets the inlay hints of a document version, in its coordinates.</summary>
    ValueTask<IReadOnlyList<InlayHintEntry>> InlayHintsAsync(SourceDocument document, CancellationToken cancellationToken);
}
