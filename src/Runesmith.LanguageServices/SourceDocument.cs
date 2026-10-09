using Runesmith.Text;

namespace Runesmith.LanguageServices;

/// <summary>One version of a document's text, as the language services see it.</summary>
/// <param name="Path">The document's full path, which identifies it.</param>
/// <param name="LanguageId">The document's language, such as <c>csharp</c> or <c>java</c>.</param>
/// <param name="Version">A number that grows with every change, so results can be matched to the text they were computed for.</param>
/// <param name="Snapshot">The text; immutable and safe to read from any thread.</param>
public sealed record SourceDocument(string Path, string LanguageId, int Version, TextSnapshot Snapshot);

/// <summary>A position in one version of a document.</summary>
public readonly record struct DocumentPosition(SourceDocument Document, int Offset);

/// <summary>A place in a file, for definitions.</summary>
/// <param name="End">The end of the place, or null when only its start is known.</param>
public sealed record SourceLocation(string Path, TextPosition Start, TextPosition? End = null);
