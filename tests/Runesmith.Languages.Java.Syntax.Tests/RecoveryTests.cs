namespace Runesmith.Languages.Java.Syntax.Tests;

public sealed class RecoveryTests
{
    private static MethodDeclarationSyntax Method(JavaSyntaxTree tree, int index) =>
        Assert.IsType<MethodDeclarationSyntax>(Java.Class(tree).Body.Members[index]);

    [Fact]
    public void AHalfTypedMemberAccessHasAMissingName()
    {
        var text = "class C { void m() { foo. } void n() { } }";
        var tree = Java.Parse(text);
        Assert.NotEmpty(tree.Diagnostics);
        var statement = Method(tree, 0).Body!.Statements[0];
        var access = Assert.IsType<FieldAccessExpressionSyntax>(Assert.IsType<ExpressionStatementSyntax>(statement).Expression);
        Assert.True(access.Name.IsMissing);
        Assert.Equal(text.IndexOf("foo.", StringComparison.Ordinal) + 4, access.Name.Start);
        Assert.Equal("n", Method(tree, 1).Name.Text);
        Java.AssertWellFormed(tree);
    }

    [Fact]
    public void AHalfTypedAccessBeforeTheNextStatementKeepsBoth()
    {
        var tree = Java.Parse("class C { void m() { foo.\n int x = 1; } }");
        var statements = Method(tree, 0).Body!.Statements;
        Assert.True(statements.Count >= 2);
        Assert.Contains(statements, s => s is LocalVariableDeclarationSyntax);
    }

    [Fact]
    public void AMissingSemicolonIsReportedAndReadingGoesOn()
    {
        var tree = Java.Parse("class C { void m() { int x = 1 int y = 2; return; } }");
        var diagnostic = Assert.Single(tree.Diagnostics);
        Assert.Equal("Expected ';'", diagnostic.Message);
        Assert.Equal(3, Method(tree, 0).Body!.Statements.Count);
    }

    [Fact]
    public void AnUnclosedCallKeepsTheFollowingMembers()
    {
        var tree = Java.Parse("class C { void m() { foo(a, ; } int f; void n() {} }");
        Assert.NotEmpty(tree.Diagnostics);
        Assert.Equal(3, Java.Class(tree).Body.Members.Count);
        Assert.IsType<FieldDeclarationSyntax>(Java.Class(tree).Body.Members[1]);
    }

    [Fact]
    public void BadCharactersAreSkipped()
    {
        var tree = Java.Parse("class C { void m() { # x = 1; } int f; }");
        Assert.Contains(tree.Diagnostics, d => d.Message.Contains('#', StringComparison.Ordinal));
        Assert.IsType<FieldDeclarationSyntax>(Java.Class(tree).Body.Members[1]);
    }

    [Fact]
    public void AnUnclosedStringEndsAtTheLine()
    {
        var tree = Java.Parse("class C {\n void m() { String s = \"abc;\n }\n int f;\n}");
        Assert.NotEmpty(tree.Diagnostics);
        Assert.IsType<FieldDeclarationSyntax>(Java.Class(tree).Body.Members[1]);
    }

    [Fact]
    public void AnUnclosedFileStillHasATree()
    {
        var tree = Java.Parse("class C { void m() { if (x) {");
        Assert.Contains(tree.Diagnostics, d => d.Message.EndsWith("but the file ends", StringComparison.Ordinal));
        Assert.False(Method(tree, 0).Body!.HasCloseBrace);
        Java.AssertWellFormed(tree);
    }

    [Fact]
    public void StrayTokensBetweenMembersAreSkipped()
    {
        var tree = Java.Parse("class C { ) int a; ] void m() {} = }");
        Assert.Equal(2, Java.Class(tree).Body.Members.Count);
        Assert.Equal(3, tree.Diagnostics.Length);
    }

    [Fact]
    public void AMissingTypeBodyEndsTheClass()
    {
        var tree = Java.Parse("class A extends\nclass B {}");
        Assert.NotEmpty(tree.Diagnostics);
        Assert.Contains(tree.Root.Members, m => m is ClassDeclarationSyntax { Name.Text: "B" });
    }

    [Fact]
    public void AnIncompleteDeclarationIsKept()
    {
        var tree = Java.Parse("class C { public static }");
        Assert.IsType<IncompleteMemberSyntax>(Java.Class(tree).Body.Members[0]);
        var typed = Java.Parse("class C { List<String> }");
        Assert.IsType<IncompleteMemberSyntax>(Java.Class(typed).Body.Members[0]);
    }

    [Fact]
    public void DeepNestingIsReportedInsteadOfOverflowingTheStack()
    {
        var text = "class C { int f = " + new string('(', 200_000) + "1" + new string(')', 200_000) + "; }";
        Exception? error = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    var tree = Java.Parse(text);
                    Assert.Contains(tree.Diagnostics, d => d.Message == "The code is nested too deeply to read");
                }
                catch (Exception e)
                {
                    error = e;
                }
            },
            1024 * 1024);
        thread.Start();
        thread.Join();
        Assert.Null(error);
    }

    [Fact]
    public void NeverThrowsOnTruncatedOrShuffledSource()
    {
        var text = JavaSamples.File(300);
        var random = new Random(1234);
        for (var i = 0; i < 300; i++)
        {
            var cut = text[..random.Next(text.Length)];
            var tree = Java.Parse(cut);
            Java.AssertWellFormed(tree);
        }

        var tokens = JavaSyntaxTree.Parse(text).Tokens;
        for (var i = 0; i < 100; i++)
        {
            var pieces = new List<string>();
            for (var j = 0; j < 400; j++)
            {
                var token = tokens[random.Next(tokens.Length - 1)];
                pieces.Add(text.Substring(token.Span.Start, token.Span.Length));
            }

            var tree = Java.Parse("class C { void m() { " + string.Join(' ', pieces) + " } }");
            Java.AssertWellFormed(tree);
        }
    }
}
