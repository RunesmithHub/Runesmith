namespace Runesmith.Languages.Java.Syntax.Tests;

public sealed class LexerTests
{
    private static List<JavaToken> Lex(string text, out List<JavaComment> comments, out List<SyntaxDiagnostic> diagnostics)
    {
        comments = [];
        diagnostics = [];
        return Lexer.LexAll(text, comments, diagnostics);
    }

    private static TokenKind[] Kinds(string text)
    {
        var tokens = Lex(text, out _, out var diagnostics);
        Assert.Empty(diagnostics);
        return [.. tokens.Take(tokens.Count - 1).Select(t => t.Kind)];
    }

    private static TokenKind Single(string text)
    {
        var kinds = Kinds(text);
        return Assert.Single(kinds);
    }

    [Fact]
    public void ShiftsAreSeparateGreaterThanTokens()
    {
        var tokens = Lex("a >> b >>> c", out _, out _);
        Assert.Equal(
            [TokenKind.Identifier, TokenKind.GreaterThan, TokenKind.GreaterThan, TokenKind.Identifier, TokenKind.GreaterThan, TokenKind.GreaterThan,
             TokenKind.GreaterThan, TokenKind.Identifier, TokenKind.EndOfFile],
            tokens.Select(t => t.Kind));
        Assert.Equal(tokens[1].Span.End, tokens[2].Span.Start);
    }

    [Theory]
    [InlineData(">=", TokenKind.GreaterThanEquals)]
    [InlineData(">>=", TokenKind.GreaterThanGreaterThanEquals)]
    [InlineData(">>>=", TokenKind.GreaterThanGreaterThanGreaterThanEquals)]
    [InlineData("<<=", TokenKind.LessThanLessThanEquals)]
    [InlineData("<<", TokenKind.LessThanLessThan)]
    [InlineData("->", TokenKind.Arrow)]
    [InlineData("::", TokenKind.ColonColon)]
    [InlineData("...", TokenKind.Ellipsis)]
    [InlineData("&&", TokenKind.AmpersandAmpersand)]
    [InlineData("||", TokenKind.BarBar)]
    [InlineData("^=", TokenKind.CaretEquals)]
    [InlineData("%=", TokenKind.PercentEquals)]
    [InlineData("!=", TokenKind.ExclamationEquals)]
    [InlineData("++", TokenKind.PlusPlus)]
    [InlineData("@", TokenKind.At)]
    public void ReadsOperators(string text, TokenKind kind) => Assert.Equal(kind, Single(text));

    [Theory]
    [InlineData("var")]
    [InlineData("record")]
    [InlineData("sealed")]
    [InlineData("permits")]
    [InlineData("yield")]
    [InlineData("module")]
    [InlineData("open")]
    [InlineData("requires")]
    [InlineData("when")]
    [InlineData("non")]
    public void ReadsContextualKeywordsAsIdentifiers(string word) => Assert.Equal(TokenKind.Identifier, Single(word));

    [Fact]
    public void ReadsKeywordsAndTheUnderscore()
    {
        Assert.Equal([TokenKind.ClassKeyword, TokenKind.Underscore, TokenKind.Identifier, TokenKind.NullKeyword, TokenKind.TrueKeyword],
            Kinds("class _ __ null true"));
        Assert.Equal(TokenKind.Identifier, Single("ünïcødé_名前$"));
        Assert.Equal(TokenKind.Identifier, Single("classy"));
    }

    [Theory]
    [InlineData("0", TokenKind.IntegerLiteral)]
    [InlineData("1_000_000", TokenKind.IntegerLiteral)]
    [InlineData("0x7fff_ffff", TokenKind.IntegerLiteral)]
    [InlineData("0b1010_1010", TokenKind.IntegerLiteral)]
    [InlineData("0777", TokenKind.IntegerLiteral)]
    [InlineData("0_7", TokenKind.IntegerLiteral)]
    [InlineData("42L", TokenKind.LongLiteral)]
    [InlineData("0xFFl", TokenKind.LongLiteral)]
    [InlineData("1.5f", TokenKind.FloatLiteral)]
    [InlineData("1e10F", TokenKind.FloatLiteral)]
    [InlineData("1.5", TokenKind.DoubleLiteral)]
    [InlineData(".5", TokenKind.DoubleLiteral)]
    [InlineData("1.", TokenKind.DoubleLiteral)]
    [InlineData("1e-5", TokenKind.DoubleLiteral)]
    [InlineData("1D", TokenKind.DoubleLiteral)]
    [InlineData("09.5", TokenKind.DoubleLiteral)]
    [InlineData("0x1.8p1", TokenKind.DoubleLiteral)]
    [InlineData("0x1p-3f", TokenKind.FloatLiteral)]
    [InlineData("'a'", TokenKind.CharacterLiteral)]
    [InlineData("'\\n'", TokenKind.CharacterLiteral)]
    [InlineData("'\\''", TokenKind.CharacterLiteral)]
    [InlineData("'\\177'", TokenKind.CharacterLiteral)]
    [InlineData("\"a \\\"b\\\" \\s\"", TokenKind.StringLiteral)]
    [InlineData("\"\"\"\n  text \"\"\"", TokenKind.TextBlock)]
    public void ReadsLiterals(string text, TokenKind kind) => Assert.Equal(kind, Single(text));

    [Theory]
    [InlineData("0x", "A hexadecimal literal needs at least one digit after 0x")]
    [InlineData("0b", "A binary literal needs at least one digit after 0b")]
    [InlineData("1_", "An underscore can only be between digits")]
    [InlineData("0x1.8", "A hexadecimal floating-point literal needs an exponent, such as p0")]
    [InlineData("1e", "The exponent needs at least one digit")]
    [InlineData("09", "An octal literal can only have the digits 0 to 7")]
    [InlineData("''", "A character literal needs a character")]
    [InlineData("'ab'", "A character literal can hold only one character")]
    [InlineData("\"abc", "The string is not closed with \"")]
    [InlineData("\"\\q\"", "'\\q' is not a valid escape sequence")]
    [InlineData("\"\"\" text\"\"\"", "A text block's opening \"\"\" must be followed by the end of the line")]
    [InlineData("\"\"\"\nabc", "The text block is not closed with \"\"\"")]
    [InlineData("/* abc", "The comment is not closed with */")]
    [InlineData("#", "The character '#' cannot appear here")]
    public void ReportsMalformedTokens(string text, string message)
    {
        Lex(text, out _, out var diagnostics);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(message, diagnostic.Message);
        Assert.Equal(SyntaxDiagnostic.SyntaxError, diagnostic.Code);
    }

    [Fact]
    public void AStringEndsAtTheLineEnd()
    {
        var tokens = Lex("\"abc\nx", out _, out var diagnostics);
        Assert.Single(diagnostics);
        Assert.Equal([TokenKind.StringLiteral, TokenKind.Identifier, TokenKind.EndOfFile], tokens.Select(t => t.Kind));
    }

    [Fact]
    public void CollectsCommentsOfEachKind()
    {
        var tokens = Lex("// line\n/* block */ /**/ /** doc */ /// markdown\nx", out var comments, out _);
        Assert.Equal([CommentKind.Line, CommentKind.Block, CommentKind.Block, CommentKind.Documentation, CommentKind.MarkdownLine],
            comments.Select(c => c.Kind));
        Assert.Equal(new SourceSpan(0, 7), comments[0].Span);
        Assert.Equal(TokenKind.Identifier, tokens[0].Kind);
    }

    [Fact]
    public void ATextBlockMayHoldQuotesAndEscapedLineEnds()
    {
        var text = "\"\"\"\n  He said \"hi\" and \"\"done\\\"\"\"\n  next \\\n  line\n  \"\"\"";
        Assert.Equal(TokenKind.TextBlock, Single(text));
    }

    [Fact]
    public void IgnoresAControlZAtTheEnd() => Assert.Equal([TokenKind.Identifier], Kinds("x\u001a"));

    [Fact]
    public void ReadsUnicodeEscapesBeforeTokens()
    {
        // \u0069nt is "int", and \u000a ends the line comment.
        var text = "\\u0069nt x; // c\\u000a y";
        var tree = JavaSyntaxTree.Parse(text);
        Assert.Equal(TokenKind.IntKeyword, tree.Tokens[0].Kind);
        Assert.Equal(new SourceSpan(0, 8), tree.Tokens[0].Span);
        Assert.Equal(TokenKind.Identifier, tree.Tokens[3].Kind);
        Assert.Equal("y", text[tree.Tokens[3].Span.Start..tree.Tokens[3].Span.End]);
        Assert.Equal("// c", text.Substring(tree.Comments[0].Span.Start, tree.Comments[0].Span.Length));
    }

    [Fact]
    public void AnEscapedBackslashDoesNotStartAUnicodeEscape()
    {
        var tree = JavaSyntaxTree.Parse("class C { String s = \"\\\\u0041\"; }");
        Assert.Empty(tree.Diagnostics);
        var literal = tree.Tokens.Single(t => t.Kind == TokenKind.StringLiteral);
        Assert.Equal("\\u0041", JavaLiterals.StringValue(tree.Text.AsSpan(literal.Span.Start, literal.Span.Length)));
    }

    [Fact]
    public void ReportsAMalformedUnicodeEscape()
    {
        var tree = JavaSyntaxTree.Parse("class C { /* \\u12 */ }");
        Assert.Contains(tree.Diagnostics, d => d.Code == SyntaxDiagnostic.SyntaxError && d.Span.Start == 13);
    }

    [Fact]
    public void AcceptsManyUsInAUnicodeEscape()
    {
        var tree = JavaSyntaxTree.Parse("class C { char c = '\\uuuu0041'; }");
        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void KeywordsHaveTheirText()
    {
        foreach (var word in Keywords.All)
            Assert.Equal(word, Keywords.TextOf(Keywords.KindOf(word)));
    }
}
