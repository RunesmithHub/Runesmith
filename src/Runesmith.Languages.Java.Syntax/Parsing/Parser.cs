using System.Runtime.CompilerServices;

namespace Runesmith.Languages.Java.Syntax;

/// <summary>Reads Java tokens into a syntax tree by recursive descent, recovering from errors so a half-typed file still has a tree.</summary>
/// <remarks>The parser works on decoded text (Unicode escapes applied) and gives nodes and diagnostics positions in the original text.</remarks>
internal sealed partial class Parser
{
    private readonly string text;
    private readonly int[]? map;
    private readonly IReadOnlyList<JavaToken> tokens;
    private readonly List<SyntaxDiagnostic> diagnostics;
    private readonly NameTable names = new();
    private int index;
    private int lastEnd;
    private int speculation;
    private bool speculationFailed;
    private bool inCaseLabel;

    public Parser(string text, int[]? map, IReadOnlyList<JavaToken> tokens, List<SyntaxDiagnostic> diagnostics, int startIndex = 0)
    {
        this.text = text;
        this.map = map;
        this.tokens = tokens;
        this.diagnostics = diagnostics;
        index = startIndex;
        lastEnd = startIndex > 0 ? EndOf(tokens[startIndex - 1]) : Pos(tokens[startIndex].Span.Start);
    }

    public int Index => index;

    private JavaToken Current => tokens[index];

    private TokenKind Kind => tokens[index].Kind;

    private TokenKind PeekKind(int ahead) => tokens[Math.Min(index + ahead, tokens.Count - 1)].Kind;

    private JavaToken PeekToken(int ahead) => tokens[Math.Min(index + ahead, tokens.Count - 1)];

    private int Pos(int decoded) => map is null ? decoded : map[decoded];

    private int StartOf(JavaToken token) => Pos(token.Span.Start);

    private int EndOf(JavaToken token) => Pos(token.Span.End);

    private int CurrentStart => StartOf(Current);

    // A node that read no tokens is empty and sits where reading stopped, like a missing node.
    private SourceSpan From(int start) => lastEnd < start ? new SourceSpan(lastEnd, 0) : SourceSpan.FromBounds(start, lastEnd);

    private JavaToken Advance()
    {
        var token = Current;
        if (token.Kind != TokenKind.EndOfFile)
        {
            index++;
            lastEnd = EndOf(token);
        }

        return token;
    }

    private bool Accept(TokenKind kind)
    {
        if (Kind != kind)
            return false;

        Advance();
        return true;
    }

    private bool Expect(TokenKind kind)
    {
        if (Accept(kind))
            return true;

        ErrorExpected($"'{Keywords.TextOf(kind)}'");
        return false;
    }

    private void ErrorExpected(string what) =>
        Error(new SourceSpan(lastEnd, 0), Kind == TokenKind.EndOfFile ? $"Expected {what}, but the file ends" : $"Expected {what}");

    private void ErrorAtCurrent(string message) => Error(new SourceSpan(CurrentStart, EndOf(Current) - CurrentStart), message);

    private void Error(SourceSpan span, string message)
    {
        if (speculation > 0)
        {
            speculationFailed = true;
            return;
        }

        // One error per position is enough; recovery often finds the same problem twice.
        if (diagnostics.Count > 0 && diagnostics[^1].Span.Start == span.Start && diagnostics[^1].Code == SyntaxDiagnostic.SyntaxError)
            return;

        diagnostics.Add(new SyntaxDiagnostic(span, SyntaxDiagnostic.SyntaxError, message));
    }

    /// <summary>Tries a parse; on failure goes back to where it started and reports nothing.</summary>
    private T? Speculate<T>(Func<T?> parse)
        where T : class
    {
        var savedIndex = index;
        var savedLastEnd = lastEnd;
        var savedFailed = speculationFailed;
        speculation++;
        speculationFailed = false;
        T? result;
        try
        {
            result = parse();
        }
        finally
        {
            speculation--;
        }

        var succeeded = result is not null && !speculationFailed;
        speculationFailed = savedFailed;
        if (succeeded)
            return result;

        index = savedIndex;
        lastEnd = savedLastEnd;
        return null;
    }

    /// <summary>Tries a parse to see whether it succeeds, then goes back to where it started either way.</summary>
    private bool LooksLike(Func<bool> parse)
    {
        var savedIndex = index;
        var savedLastEnd = lastEnd;
        var savedFailed = speculationFailed;
        speculation++;
        speculationFailed = false;
        bool succeeded;
        try
        {
            succeeded = parse() && !speculationFailed;
        }
        finally
        {
            speculation--;
        }

        speculationFailed = savedFailed;
        index = savedIndex;
        lastEnd = savedLastEnd;
        return succeeded;
    }

    private static bool IsName(TokenKind kind) => kind is TokenKind.Identifier or TokenKind.Underscore;

    private bool IsContextual(string word) => Kind == TokenKind.Identifier && TextOf(Current).SequenceEqual(word);

    private bool IsContextual(int ahead, string word) => PeekKind(ahead) == TokenKind.Identifier && TextOf(PeekToken(ahead)).SequenceEqual(word);

    private ReadOnlySpan<char> TextOf(JavaToken token) => text.AsSpan(token.Span.Start, token.Span.Length);

    private IdentifierSyntax ParseIdentifier()
    {
        if (IsName(Kind))
        {
            var token = Advance();
            return new IdentifierSyntax(SourceSpan.FromBounds(StartOf(token), EndOf(token)), names.Get(TextOf(token)));
        }

        ErrorExpected("a name");
        return new IdentifierSyntax(new SourceSpan(lastEnd, 0), "", isMissing: true);
    }

    /// <summary>Makes an identifier of a keyword token, such as <c>new</c> in a constructor reference or <c>this</c> in a receiver parameter.</summary>
    private IdentifierSyntax KeywordIdentifier()
    {
        var token = Advance();
        return new IdentifierSyntax(SourceSpan.FromBounds(StartOf(token), EndOf(token)), names.Get(TextOf(token)));
    }

    private NameSyntax ParseName()
    {
        var start = CurrentStart;
        var parts = new List<IdentifierSyntax> { ParseIdentifier() };
        while (Kind == TokenKind.Dot && IsName(PeekKind(1)))
        {
            Advance();
            parts.Add(ParseIdentifier());
        }

        return new NameSyntax(From(start), List(parts));
    }

    private static SyntaxList<T>? List<T>(List<T>? items)
        where T : SyntaxNode => items is { Count: > 0 } ? new SyntaxList<T>(items) : null;

    /// <summary>Whether the stack has room for one more level of nesting; very deep source would otherwise overflow it.</summary>
    private bool HasStackRoom()
    {
        if (RuntimeHelpers.TryEnsureSufficientExecutionStack())
            return true;

        ErrorAtCurrent("The code is nested too deeply to read");
        return false;
    }

    /// <summary>Skips one token that cannot start anything here, reporting it.</summary>
    private void SkipUnexpected(string context)
    {
        if (Kind == TokenKind.EndOfFile)
            return;

        ErrorAtCurrent($"Unexpected '{TextOf(Current)}' {context}");
        Advance();
    }
}
