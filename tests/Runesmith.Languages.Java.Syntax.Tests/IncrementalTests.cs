namespace Runesmith.Languages.Java.Syntax.Tests;

public sealed class IncrementalTests(ITestOutputHelper output)
{
    private static readonly string[] Insertions =
    [
        "x", "foo.", "foo.bar(", "{", "}", "(", ")", ";", "\"", "'", "/*", "*/", "//", "\n", "int a = 1;", "if (a) {", "} else {", "->",
        "<", ">>", "case 1 ->", "\"\"\"\n", "var ", "return ", "new Object() { void m() { } };", "switch (x) { case 1 -> 2; }", "\\", "@",
        "record R(int x) {}", "yield ", "_", "class", " ", "1.5e", "0x",
    ];

    private static JavaSyntaxTree Apply(JavaSyntaxTree tree, TextEdit edit, out string newText)
    {
        newText = string.Concat(tree.Text.AsSpan(0, edit.Start), edit.NewText, tree.Text.AsSpan(edit.End));
        return tree.WithChanges(newText, [edit]);
    }

    private static void AssertSameAsFullParse(JavaSyntaxTree incremental, string because)
    {
        var full = JavaSyntaxTree.Parse(incremental.Text, incremental.Options);
        var expected = TreeDump.Of(full);
        var actual = TreeDump.Of(incremental);
        if (expected != actual)
        {
            var expectedLines = expected.Split('\n');
            var actualLines = actual.Split('\n');
            var line = 0;
            while (line < expectedLines.Length && line < actualLines.Length && expectedLines[line] == actualLines[line])
                line++;
            Assert.Fail($"{because}\nfirst difference at line {line}:\nexpected {expectedLines.ElementAtOrDefault(line)}\nactual   {actualLines.ElementAtOrDefault(line)}");
        }
    }

    [Fact]
    public void AnEditInsideAMethodReparsesOnlyItsBody()
    {
        var text = "class C {\n void a() { int x = 1; }\n void b() { int y = 2; }\n int f; }";
        var tree = Java.ParseClean(text);
        var position = text.IndexOf("2;", StringComparison.Ordinal);
        var edit = new TextEdit(position, 1, "foo(3) + 4");
        var newText = text[..position] + "foo(3) + 4" + text[(position + 1)..];
        var reparsed = IncrementalParser.TryReparse(tree, newText, [edit]);
        Assert.NotNull(reparsed);
        AssertSameAsFullParse(reparsed, newText);

        var oldMembers = Java.Class(tree).Body.Members;
        var newMembers = Java.Class(reparsed).Body.Members;
        Assert.Same(oldMembers[0], newMembers[0]);
        Assert.NotSame(oldMembers[1], newMembers[1]);
        Assert.Equal(oldMembers[2].Start + 9, newMembers[2].Start);
    }

    [Fact]
    public void AnEditThatUnbalancesBracesParsesTheWholeFile()
    {
        var text = "class C { void a() { int x = 1; } void b() { } }";
        var tree = Java.ParseClean(text);
        var position = text.IndexOf("int", StringComparison.Ordinal);
        var newText = text.Insert(position, "}");
        Assert.Null(IncrementalParser.TryReparse(tree, newText, [new TextEdit(position, 0, "}")]));
        AssertSameAsFullParse(tree.WithChanges(newText, [new TextEdit(position, 0, "}")]), newText);
    }

    [Fact]
    public void AnEditOutsideABodyParsesTheWholeFile()
    {
        var text = "class C { void a() { } }";
        var tree = Java.ParseClean(text);
        var position = text.IndexOf("a()", StringComparison.Ordinal);
        var newText = text.Insert(position, "b");
        Assert.Null(IncrementalParser.TryReparse(tree, newText, [new TextEdit(position, 0, "b")]));
        Assert.Equal("ba", ((MethodDeclarationSyntax)Java.Class(tree.WithChanges(newText, [new TextEdit(position, 0, "b")])).Body.Members[0]).Name.Text);
    }

    [Fact]
    public void ReparsingKeepsVersionDiagnostics()
    {
        var text = "record R(int x) { R { var a = 1; } void m() { int b = 2; } }";
        var tree = Java.Parse(text, 9);
        var position = text.IndexOf("int b", StringComparison.Ordinal);
        var edit = new TextEdit(position, 3, "var");
        var newText = text[..position] + "var" + text[(position + 3)..];
        var reparsed = IncrementalParser.TryReparse(tree, newText, [edit]);
        Assert.NotNull(reparsed);
        var message = JavaFeatures.GetMessage(JavaFeature.LocalVariableTypeInference, new JavaVersion(9))!;
        Assert.Equal(2, reparsed.Diagnostics.Count(d => d.Message == message));
        Assert.Contains(reparsed.Diagnostics, d => d.Message == JavaFeatures.GetMessage(JavaFeature.Records, new JavaVersion(9)));
        AssertSameAsFullParse(reparsed, newText);
    }

    [Fact]
    public void SeveralEditsInOneBodyAreReparsedTogether()
    {
        var text = "class C { void a() { int x = 1; int y = 2; } }";
        var tree = Java.ParseClean(text);
        var first = text.IndexOf('1', StringComparison.Ordinal);
        var second = text.IndexOf('2', StringComparison.Ordinal);
        TextEdit[] edits = [new TextEdit(first, 1, "10"), new TextEdit(second, 1, "")];
        var newText = text[..first] + "10" + text[(first + 1)..second] + text[(second + 1)..];
        var reparsed = IncrementalParser.TryReparse(tree, newText, edits);
        Assert.NotNull(reparsed);
        AssertSameAsFullParse(reparsed, newText);
    }

    [Fact]
    public void RandomEditsGiveTheSameTreeAsAFullParse()
    {
        var random = new Random(20251008);
        var incremental = 0;
        var total = 0;
        foreach (var source in Sources())
        {
            var tree = JavaSyntaxTree.Parse(source, new JavaParseOptions { Version = new JavaVersion(17) });
            for (var i = 0; i < 100; i++)
            {
                var edit = RandomEdit(tree, random);
                var newText = string.Concat(tree.Text.AsSpan(0, edit.Start), edit.NewText, tree.Text.AsSpan(edit.End));
                var reparsed = IncrementalParser.TryReparse(tree, newText, [edit]);
                if (reparsed is not null)
                {
                    incremental++;
                    AssertSameAsFullParse(reparsed, $"edit {edit} on {tree.Text.Length} characters");
                }

                total++;
                tree = Apply(tree, edit, out _);

                // Start again from time to time, so that edits do not pile up into a file of nothing but errors.
                if (tree.Diagnostics.Length > 40)
                    tree = JavaSyntaxTree.Parse(source, tree.Options);
            }
        }

        output.WriteLine($"{incremental} of {total} edits reparsed one body");
        Assert.True(incremental > total / 3, $"only {incremental} of {total} edits reparsed one body");
    }

    [Fact]
    public void TypingAMethodCharacterByCharacterMatchesAFullParse()
    {
        var text = "class C {\n    void m() {\n        \n    }\n}\n";
        const string typed = "if (list.size() > 0) { for (var item : list) { System.out.println(item.name() + \"!\"); } } else return;";
        var tree = Java.ParseClean(text);
        var position = text.IndexOf("        \n", StringComparison.Ordinal) + 8;
        foreach (var character in typed)
        {
            tree = Apply(tree, new TextEdit(position, 0, character.ToString()), out var newText);
            AssertSameAsFullParse(tree, newText);
            position++;
        }

        Assert.Empty(tree.Diagnostics);
    }

    [Fact]
    public void TreesFromEditsCanBeReadFromManyThreads()
    {
        var text = JavaSamples.File(400);
        var tree = JavaSyntaxTree.Parse(text);
        var position = text.IndexOf("return Optional.empty();", StringComparison.Ordinal);
        tree = Apply(tree, new TextEdit(position, 0, "int z = 1; "), out _);
        var dumps = new string[8];
        Parallel.For(0, dumps.Length, i => dumps[i] = TreeDump.Of(tree.Root));
        Assert.All(dumps, d => Assert.Equal(dumps[0], d));
        AssertSameAsFullParse(tree, "parallel reads");
    }

    private static IEnumerable<string> Sources()
    {
        yield return JavaSamples.File(150);
        if (JdkSources.Path is null)
            yield break;

        var wanted = new HashSet<string>(StringComparer.Ordinal)
        {
            "java.base/java/util/ArrayList.java",
            "java.base/java/lang/String.java",
            "java.base/java/util/stream/Collectors.java",
            "java.base/java/util/concurrent/ConcurrentHashMap.java",
        };
        foreach (var (name, text) in JdkSources.ReadAll().Where(f => wanted.Contains(f.Name)))
            yield return text;
    }

    private static TextEdit RandomEdit(JavaSyntaxTree tree, Random random)
    {
        var text = tree.Text;
        int position;
        if (random.Next(4) > 0)
        {
            var bodies = tree.Root.DescendantNodesAndSelf().OfType<MethodDeclarationSyntax>().Where(m => m.Body is { Span.Length: > 2 }).ToList();
            if (bodies.Count > 0)
            {
                var body = bodies[random.Next(bodies.Count)].Body!;
                position = body.Start + 1 + random.Next(body.Span.Length - 2);
            }
            else
            {
                position = random.Next(text.Length + 1);
            }
        }
        else
        {
            position = random.Next(text.Length + 1);
        }

        var length = random.Next(3) == 0 ? Math.Min(random.Next(8), text.Length - position) : 0;
        var insertion = random.Next(4) == 0 ? "" : Insertions[random.Next(Insertions.Length)];
        if (length == 0 && insertion.Length == 0)
            insertion = "y";
        return new TextEdit(position, length, insertion);
    }
}
