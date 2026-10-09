namespace Runesmith.Languages.Java.Syntax;

/// <summary>How to parse a Java file: for which release, and whether preview features are on.</summary>
public sealed record JavaParseOptions
{
    /// <summary>Gets options for the latest release without preview features.</summary>
    public static JavaParseOptions Default { get; } = new();

    /// <summary>Gets the release the file is written for; syntax newer than it is reported.</summary>
    public JavaVersion Version { get; init; } = JavaVersion.Latest;
}

/// <summary>A change to a text: <see cref="Length"/> characters at <see cref="Start"/> replaced by <see cref="NewText"/>.</summary>
public readonly record struct TextEdit(int Start, int Length, string NewText)
{
    public int End => Start + Length;
}

/// <summary>A documentation comment: one <c>/** */</c> comment, or a run of <c>///</c> lines.</summary>
/// <param name="Span">Where the comment is, from its first delimiter to its end.</param>
/// <param name="Kind"><see cref="CommentKind.Documentation"/> or <see cref="CommentKind.MarkdownLine"/>.</param>
/// <param name="Content">The text without the comment's delimiters, leading asterisks and common indentation.</param>
public sealed record DocComment(SourceSpan Span, CommentKind Kind, string Content);
