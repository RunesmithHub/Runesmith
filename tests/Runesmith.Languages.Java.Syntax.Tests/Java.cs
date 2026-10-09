namespace Runesmith.Languages.Java.Syntax.Tests;

/// <summary>Parses snippets for tests.</summary>
internal static class Java
{
    public static JavaSyntaxTree Parse(string text, int release = JavaVersion.LatestRelease, bool preview = false) =>
        JavaSyntaxTree.Parse(text, new JavaParseOptions { Version = new JavaVersion(release, preview) });

    /// <summary>Parses a file that must have no diagnostics at all.</summary>
    public static JavaSyntaxTree ParseClean(string text, int release = JavaVersion.LatestRelease, bool preview = true)
    {
        var tree = Parse(text, release, preview);
        Assert.True(tree.Diagnostics.IsEmpty, string.Join('\n', tree.Diagnostics));
        return tree;
    }

    /// <summary>Parses statements in a method and gets the first one.</summary>
    public static T Statement<T>(string statements)
        where T : StatementSyntax
    {
        var tree = ParseClean($"class C {{ void m() {{ {statements} }} }}");
        var method = (MethodDeclarationSyntax)Class(tree).Body.Members[0];
        return Assert.IsType<T>(method.Body!.Statements[0]);
    }

    /// <summary>Parses an expression as a field's initializer.</summary>
    public static T Expression<T>(string expression)
        where T : ExpressionSyntax
    {
        var tree = ParseClean($"class C {{ Object f = {expression}; }}");
        var field = (FieldDeclarationSyntax)Class(tree).Body.Members[0];
        return Assert.IsType<T>(field.Variables[0].Initializer);
    }

    public static T Member<T>(string member)
        where T : MemberDeclarationSyntax
    {
        var tree = ParseClean($"class C {{ {member} }}");
        return Assert.IsType<T>(Class(tree).Body.Members[0]);
    }

    public static TypeDeclarationSyntax Class(JavaSyntaxTree tree) => (TypeDeclarationSyntax)tree.Root.Members[0];

    public static string TextOf(JavaSyntaxTree tree, SyntaxNode node) => tree.Text.Substring(node.Start, node.Span.Length);

    /// <summary>Checks that every node lies inside its parent and after its previous sibling.</summary>
    public static void AssertWellFormed(JavaSyntaxTree tree)
    {
        Assert.Equal(new SourceSpan(0, tree.Text.Length), tree.Root.Span);
        foreach (var node in tree.Root.DescendantNodesAndSelf())
        {
            var previousEnd = node.Start;
            foreach (var child in node.ChildNodes())
            {
                Assert.True(child.Start >= previousEnd && child.End <= node.End, $"{child} is out of place in {node}");
                previousEnd = child.End;
            }
        }
    }
}
