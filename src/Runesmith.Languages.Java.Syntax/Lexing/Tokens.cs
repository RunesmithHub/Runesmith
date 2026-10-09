namespace Runesmith.Languages.Java.Syntax;

/// <summary>A token: its kind and where it is in the source.</summary>
public readonly record struct JavaToken(TokenKind Kind, SourceSpan Span);

/// <summary>The kinds of comments.</summary>
public enum CommentKind : byte
{
    /// <summary>A <c>//</c> comment.</summary>
    Line,

    /// <summary>A <c>/* */</c> comment.</summary>
    Block,

    /// <summary>A <c>/** */</c> documentation comment.</summary>
    Documentation,

    /// <summary>A <c>///</c> line, which with the lines around it is a Markdown documentation comment since Java 23.</summary>
    MarkdownLine,
}

/// <summary>A comment in the source.</summary>
public readonly record struct JavaComment(CommentKind Kind, SourceSpan Span);
