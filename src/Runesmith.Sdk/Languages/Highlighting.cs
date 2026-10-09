using Avalonia.Media;
using Runesmith.Sdk.Documents;

namespace Runesmith.Sdk.Languages;

/// <summary>How a token is drawn.</summary>
public sealed record SyntaxStyle(Color Foreground, bool IsBold = false, bool IsItalic = false, bool IsUnderline = false);

/// <summary>A highlighted part of one line.</summary>
/// <param name="Start">The first column of the token in its line.</param>
public readonly record struct SyntaxToken(int Start, int Length, SyntaxStyle Style);

/// <summary>The lines whose highlighting changed, inclusive.</summary>
public sealed class HighlightingChangedEventArgs(int firstLine, int lastLine) : EventArgs
{
    public int FirstLine { get; } = firstLine;

    public int LastLine { get; } = lastLine;
}

/// <summary>Highlights one document, line by line, in the background.</summary>
public interface ISyntaxHighlighter : IDisposable
{
    /// <summary>Gets the tokens of a line, or an empty list while the line has not been highlighted yet; columns not covered by a token use the
    /// editor's default text color.</summary>
    IReadOnlyList<SyntaxToken> GetTokens(int lineNumber);

    /// <summary>Asks to highlight these lines first, such as the lines on screen.</summary>
    void Prioritize(int firstLine, int lastLine);

    /// <summary>Raised, on the UI thread, when lines have new tokens, such as after an edit or a theme change.</summary>
    event EventHandler<HighlightingChangedEventArgs>? Changed;
}

/// <summary>Creates highlighters for documents.</summary>
public interface ISyntaxHighlighterProvider
{
    /// <summary>Creates a highlighter that follows the document's text and language, or returns null when nothing highlights it.</summary>
    ISyntaxHighlighter? Create(IDocument document);
}
