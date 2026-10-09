namespace Runesmith.Languages.Java.Syntax.Tests;

public sealed class TreeTests
{
    [Fact]
    public void FindsTokens()
    {
        var text = "class C { int x; }";
        var tree = Java.ParseClean(text);
        Assert.Equal(TokenKind.IntKeyword, tree.FindToken(text.IndexOf("int", StringComparison.Ordinal) + 1).Kind);
        Assert.Equal(TokenKind.Identifier, tree.FindToken(text.IndexOf(" x", StringComparison.Ordinal)).Kind);
        Assert.Equal(TokenKind.EndOfFile, tree.FindToken(text.Length).Kind);
        Assert.Equal(TokenKind.Semicolon, tree.FindTokenBefore(text.IndexOf(" }", StringComparison.Ordinal) + 1)!.Value.Kind);
        Assert.Equal(TokenKind.Identifier, tree.FindTokenBefore(text.IndexOf(';', StringComparison.Ordinal))!.Value.Kind);
        Assert.Null(tree.FindTokenBefore(0));
        Assert.Equal(TokenKind.CloseBrace, tree.FindTokenBefore(text.Length)!.Value.Kind);
    }

    [Fact]
    public void FindsTheDeepestNode()
    {
        var text = "class C { void m() { a.b(c); } }";
        var tree = Java.ParseClean(text);
        var identifier = Assert.IsType<IdentifierSyntax>(tree.FindNode(text.IndexOf('c', 10)));
        Assert.Equal("c", identifier.Text);
        var path = tree.GetPath(text.IndexOf('b', StringComparison.Ordinal));
        Assert.Same(tree.Root, path[0]);
        Assert.Contains(path, n => n is MethodInvocationExpressionSyntax);
        Assert.IsType<IdentifierSyntax>(path[^1]);
    }

    [Fact]
    public void FindsTheMissingNameAfterADot()
    {
        var text = "class C { void m() { foo. } }";
        var tree = Java.Parse(text);
        var caret = text.IndexOf('.', StringComparison.Ordinal) + 1;
        var node = Assert.IsType<IdentifierSyntax>(tree.FindNode(caret, includeEnd: true));
        Assert.True(node.IsMissing);
        Assert.IsType<FieldAccessExpressionSyntax>(tree.GetPath(caret, includeEnd: true)[^2]);
    }

    [Fact]
    public void FindsComments()
    {
        var text = "class C { /* note */ }";
        var tree = Java.ParseClean(text);
        Assert.Equal(CommentKind.Block, tree.FindComment(text.IndexOf("note", StringComparison.Ordinal))!.Value.Kind);
        Assert.Null(tree.FindComment(1));
    }

    [Fact]
    public void ReadsJavadocComments()
    {
        var tree = Java.ParseClean("""
            class C {
                /**
                 * Adds two numbers.
                 *
                 * @param a the first
                 */
                // a regular comment in between
                @Deprecated
                public int add(int a, int b) { return a + b; }

                /** Not this one. */
                int x;
                int y;
            }
            """);
        var members = Java.Class(tree).Body.Members;
        var doc = tree.LeadingDocComment(members[0])!;
        Assert.Equal(CommentKind.Documentation, doc.Kind);
        Assert.Equal("Adds two numbers.\n\n@param a the first", doc.Content);
        Assert.Equal("Not this one.", tree.LeadingDocComment(members[1])!.Content);
        Assert.Null(tree.LeadingDocComment(members[2]));
    }

    [Fact]
    public void ReadsMarkdownCommentsFromJava23()
    {
        var text = """
            /// Old comment.

            /// A **Markdown** comment.
            ///
            ///   - indented
            class C {}
            """;
        var doc = Java.ParseClean(text, 23).LeadingDocComment(Java.ParseClean(text, 23).Root.Members[0])!;
        Assert.Equal(CommentKind.MarkdownLine, doc.Kind);
        Assert.Equal("A **Markdown** comment.\n\n  - indented", doc.Content);
        Assert.StartsWith("/// A", text[doc.Span.Start..doc.Span.End], StringComparison.Ordinal);
        var old = Java.ParseClean(text, 22);
        Assert.Null(old.LeadingDocComment(old.Root.Members[0]));
    }

    [Fact]
    public void TokensAndCommentsAreInOrder()
    {
        var tree = Java.ParseClean(JavaSamples.File(300));
        for (var i = 1; i < tree.Tokens.Length; i++)
            Assert.True(tree.Tokens[i].Span.Start >= tree.Tokens[i - 1].Span.End);
        for (var i = 1; i < tree.Comments.Length; i++)
            Assert.True(tree.Comments[i].Span.Start >= tree.Comments[i - 1].Span.End);
        Assert.Equal(TokenKind.EndOfFile, tree.Tokens[^1].Kind);
    }
}
