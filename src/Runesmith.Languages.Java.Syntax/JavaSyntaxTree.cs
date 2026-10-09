using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;

namespace Runesmith.Languages.Java.Syntax;

/// <summary>A parsed Java file: its text, syntax tree, tokens, comments and diagnostics. Immutable and safe to read from any thread.</summary>
/// <remarks>Every position is an offset in <see cref="Text"/>, also when the text has Unicode escapes.</remarks>
public sealed class JavaSyntaxTree
{
    private JavaSyntaxTree(string text, JavaParseOptions options, CompilationUnitSyntax root, JavaToken[] tokens, JavaComment[] comments,
        SyntaxDiagnostic[] diagnostics, bool hasUnicodeEscapes)
    {
        Text = text;
        Options = options;
        Root = root;
        Tokens = ImmutableCollectionsMarshal.AsImmutableArray(tokens);
        Comments = ImmutableCollectionsMarshal.AsImmutableArray(comments);
        Diagnostics = ImmutableCollectionsMarshal.AsImmutableArray(diagnostics);
        HasUnicodeEscapes = hasUnicodeEscapes;
    }

    public string Text { get; }

    public JavaParseOptions Options { get; }

    public CompilationUnitSyntax Root { get; }

    /// <summary>Gets the tokens in source order, ending with an end-of-file token.</summary>
    public ImmutableArray<JavaToken> Tokens { get; }

    /// <summary>Gets the comments in source order.</summary>
    public ImmutableArray<JavaComment> Comments { get; }

    /// <summary>Gets the syntax errors and the features the release does not have, sorted by position.</summary>
    public ImmutableArray<SyntaxDiagnostic> Diagnostics { get; }

    private bool HasUnicodeEscapes { get; }

    /// <summary>Parses a Java file.</summary>
    public static JavaSyntaxTree Parse(string text, JavaParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= JavaParseOptions.Default;

        var diagnostics = new List<SyntaxDiagnostic>();
        var decoded = UnicodeEscapes.Decode(text, diagnostics, out var map);
        var comments = new List<JavaComment>();
        var lexerDiagnostics = new List<SyntaxDiagnostic>();
        var tokens = Lexer.LexAll(decoded, comments, lexerDiagnostics);
        var parserDiagnostics = new List<SyntaxDiagnostic>();
        var root = new Parser(decoded, map, tokens, parserDiagnostics).ParseCompilationUnit(text.Length);

        var tokenArray = tokens.ToArray();
        var commentArray = comments.ToArray();
        if (map is not null)
        {
            for (var i = 0; i < tokenArray.Length; i++)
                tokenArray[i] = tokenArray[i] with { Span = ToOriginal(tokenArray[i].Span, map) };
            for (var i = 0; i < commentArray.Length; i++)
                commentArray[i] = commentArray[i] with { Span = ToOriginal(commentArray[i].Span, map) };
            foreach (var diagnostic in lexerDiagnostics)
                diagnostics.Add(diagnostic with { Span = ToOriginal(diagnostic.Span, map) });
        }
        else
        {
            diagnostics.AddRange(lexerDiagnostics);
        }

        diagnostics.AddRange(parserDiagnostics);
        new VersionChecker(options.Version, diagnostics).CheckCompilationUnit(root);
        var diagnosticArray = diagnostics.ToArray();
        Array.Sort(diagnosticArray, SyntaxDiagnostic.Compare);
        return new JavaSyntaxTree(text, options, root, tokenArray, commentArray, diagnosticArray, map is not null);
    }

    private static SourceSpan ToOriginal(SourceSpan span, int[] map) => SourceSpan.FromBounds(map[span.Start], map[span.End]);

    /// <summary>Parses the file again after edits, reusing everything outside the method, constructor or initializer body the edits are in.</summary>
    /// <param name="newText">The text after the edits.</param>
    /// <param name="edits">The edits, in positions of this tree's text, sorted and not overlapping.</param>
    /// <returns>A tree equal to parsing <paramref name="newText"/> from scratch.</returns>
    public JavaSyntaxTree WithChanges(string newText, IReadOnlyList<TextEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(newText);
        ArgumentNullException.ThrowIfNull(edits);
        return IncrementalParser.TryReparse(this, newText, edits) ?? Parse(newText, Options);
    }

    /// <summary>Gets the index of the token at an offset, or of the first token after it when the offset is in whitespace or a comment.</summary>
    public int FindTokenIndex(int offset)
    {
        int low = 0, high = Tokens.Length - 1;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (Tokens[middle].Span.End <= offset)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    /// <summary>Gets the token at an offset, or the first token after it when the offset is in whitespace or a comment.</summary>
    public JavaToken FindToken(int offset) => Tokens[FindTokenIndex(offset)];

    /// <summary>Gets the last token that ends at or before an offset, such as the token before the caret.</summary>
    public JavaToken? FindTokenBefore(int offset)
    {
        var index = FindTokenIndex(offset);
        if (Tokens[index].Span.End <= offset && Tokens[index].Kind != TokenKind.EndOfFile)
            return Tokens[index];
        return index > 0 ? Tokens[index - 1] : null;
    }

    /// <summary>Gets the comment that contains an offset, if any.</summary>
    public JavaComment? FindComment(int offset)
    {
        var index = LastCommentStartingBefore(offset + 1);
        return index >= 0 && Comments[index].Span.Contains(offset) ? Comments[index] : null;
    }

    /// <summary>Gets the nodes that contain an offset, from the root down to the deepest.</summary>
    /// <param name="offset">The offset.</param>
    /// <param name="includeEnd">Whether a node also contains the offset just after its end, as the caret after <c>foo.</c> is in the member
    /// name that is still missing; a node that contains the offset itself is preferred.</param>
    public IReadOnlyList<SyntaxNode> GetPath(int offset, bool includeEnd = false)
    {
        var path = new List<SyntaxNode> { Root };
        SyntaxNode node = Root;
        while (true)
        {
            SyntaxNode? next = null;
            foreach (var child in node.ChildNodes())
            {
                if (child.Start <= offset && offset < child.End)
                {
                    next = child;
                    break;
                }

                if (includeEnd && child.Start <= offset && offset == child.End)
                    next = child;
                else if (child.Start > offset)
                    break;
            }

            if (next is null)
                return path;

            path.Add(next);
            node = next;
        }
    }

    /// <summary>Gets the deepest node that contains an offset.</summary>
    public SyntaxNode FindNode(int offset, bool includeEnd = false) => GetPath(offset, includeEnd)[^1];

    /// <summary>Gets the documentation comment right before a declaration, if it has one. <c>///</c> comments count from Java 23 on.</summary>
    public DocComment? LeadingDocComment(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var tokenIndex = FindTokenIndex(node.Start);
        var previousTokenEnd = tokenIndex > 0 ? Tokens[tokenIndex - 1].Span.End : 0;
        var markdown = Options.Version.Release >= JavaFeatures.FinalRelease(JavaFeature.MarkdownDocComments);

        for (var i = LastCommentStartingBefore(node.Start); i >= 0 && Comments[i].Span.Start >= previousTokenEnd; i--)
        {
            var comment = Comments[i];
            if (comment.Kind == CommentKind.Documentation)
                return new DocComment(comment.Span, CommentKind.Documentation, DocCommentText.Javadoc(Text.AsSpan(comment.Span.Start, comment.Span.Length)));

            if (comment.Kind == CommentKind.MarkdownLine && markdown)
            {
                var first = i;
                while (first > 0 && Comments[first - 1].Kind == CommentKind.MarkdownLine && Comments[first - 1].Span.Start >= previousTokenEnd
                    && IsSingleLineBreak(Comments[first - 1].Span.End, Comments[first].Span.Start))
                {
                    first--;
                }

                var span = SourceSpan.FromBounds(Comments[first].Span.Start, comment.Span.End);
                var lines = new List<string>(i - first + 1);
                for (var j = first; j <= i; j++)
                    lines.Add(Text.Substring(Comments[j].Span.Start + 3, Comments[j].Span.Length - 3));
                return new DocComment(span, CommentKind.MarkdownLine, DocCommentText.Markdown(lines));
            }
        }

        return null;
    }

    private bool IsSingleLineBreak(int from, int to)
    {
        var breaks = 0;
        for (var i = from; i < to; i++)
        {
            var c = Text[i];
            if (c == '\n' || (c == '\r' && (i + 1 >= to || Text[i + 1] != '\n')))
                breaks++;
            else if (!char.IsWhiteSpace(c))
                return false;
        }

        return breaks == 1;
    }

    private int LastCommentStartingBefore(int offset)
    {
        int low = 0, high = Comments.Length;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (Comments[middle].Span.Start < offset)
                low = middle + 1;
            else
                high = middle;
        }

        return low - 1;
    }

    internal bool CanReparseIncrementally => !HasUnicodeEscapes;

    internal static JavaSyntaxTree Create(string text, JavaParseOptions options, CompilationUnitSyntax root, JavaToken[] tokens, JavaComment[] comments,
        SyntaxDiagnostic[] diagnostics) => new(text, options, root, tokens, comments, diagnostics, hasUnicodeEscapes: false);

    /// <summary>Strips documentation comments down to their content.</summary>
    private static class DocCommentText
    {
        public static string Javadoc(ReadOnlySpan<char> comment)
        {
            var body = comment[3..];
            if (body.EndsWith("*/"))
                body = body[..^2];

            var builder = new StringBuilder(body.Length);
            var first = true;
            foreach (var range in body.EnumerateLines())
            {
                var line = range;
                if (!first)
                {
                    builder.Append('\n');
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith('*'))
                    {
                        line = trimmed.TrimStart('*');
                        if (line.StartsWith(' '))
                            line = line[1..];
                    }
                }

                builder.Append(first ? line.TrimStart() : line);
                first = false;
            }

            return builder.ToString().Trim();
        }

        public static string Markdown(List<string> lines)
        {
            var indent = int.MaxValue;
            foreach (var line in lines)
            {
                var spaces = line.Length - line.TrimStart().Length;
                if (spaces < line.Length)
                    indent = Math.Min(indent, spaces);
            }

            if (indent == int.MaxValue)
                indent = 0;

            var builder = new StringBuilder();
            for (var i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                    builder.Append('\n');
                var line = lines[i];
                builder.Append(line.Length > indent ? line.AsSpan(indent).TrimEnd() : default);
            }

            return builder.ToString();
        }
    }
}
