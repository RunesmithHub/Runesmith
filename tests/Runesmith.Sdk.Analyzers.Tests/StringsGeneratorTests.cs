using Microsoft.CodeAnalysis;
using Runesmith.Sdk.Analyzers.Strings;

namespace Runesmith.Sdk.Analyzers.Tests;

public sealed class StringsGeneratorTests
{
    private const string English = """
        ### The messages of the to-do plugin.

        # The title of the to-do panel.
        panel-title = To-dos
        # How many to-dos the folder has.
        # Variables:
        #   $count (Number) - the number of to-dos
        #   $folder - the folder's name
        todo-count = { $count ->
            [0] No to-dos in { $folder }
            [one] One to-do in { $folder }
           *[other] { $count } to-dos in { $folder }
        }
        due = Due { DATETIME($date, dateStyle: "medium") }, { NUMBER($days) } days left
        -brand = Acme
        about = About { -brand }
        save-button = Save
            .tooltip = Save the to-dos of { $folder }
            .access-key = S
        only-attributes =
            .title = A title
        class = Escaped & <marked>
        """;

    private static readonly string Snapshots = Path.Combine(Repository.Root, "tests", "Runesmith.Sdk.Analyzers.Tests", "Snapshots");

    [Fact]
    public void TheStringsClassHasAMemberForEachMessage()
    {
        var result = TestCompilation.Generate([("/plugin/locales/en/acme.todo.ftl", English)], new Dictionary<string, string> { [StringsGenerator.NamespaceProperty] = "Acme.Todo" });

        Assert.Empty(result.Diagnostics);
        Snapshot.Match(Path.Combine(Snapshots, "Strings.g.cs.txt"), System.Text.RegularExpressions.Regex.Replace(result.Generated!, "\"Runesmith.Sdk.Analyzers\", \"[^\"]*\"", "\"Runesmith.Sdk.Analyzers\", \"VERSION\""));
    }

    [Fact]
    public void TheGeneratedCodeCompilesWithoutWarnings()
    {
        var use = """
            namespace Acme.Todo;

            public static class Use
            {
                public static string[] All() =>
                [
                    Strings.PanelTitle,
                    Strings.TodoCount(3, "src"),
                    Strings.Due(System.DateTimeOffset.Now, 2),
                    Strings.About,
                    Strings.SaveButton("src"),
                    Strings.SaveButton("src").Tooltip,
                    Strings.SaveButton("src").AccessKey,
                    Strings.OnlyAttributes.Title,
                    Strings.Class,
                ];
            }
            """;
        var result = TestCompilation.Generate([("/plugin/locales/en/acme.todo.ftl", English)],
            new Dictionary<string, string> { [StringsGenerator.NamespaceProperty] = "Acme.Todo" }, use);

        var problems = result.Output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity >= DiagnosticSeverity.Warning).ToList();
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void AMessageWithVariablesAndAttributesTakesItsArgumentsOnce()
    {
        var result = TestCompilation.Generate([("/p/locales/en/x.ftl", "save = Save { $name }\n    .tooltip = Save { $name } to { $folder }")],
            new Dictionary<string, string>(), """
            public static class Use
            {
                public static string Tip() => Acme.Todo.Strings.Save("a", "b").Tooltip;
                public static string Text() => Acme.Todo.Strings.Save("a", "b");
            }
            """);

        Assert.Empty(result.Output.GetDiagnostics(TestContext.Current.CancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains("public static SaveMessage Save(string name, string folder)", result.Generated, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNamespaceIsTheAssemblysNameUnlessAPropertyNamesIt()
    {
        var plain = TestCompilation.Generate([("/p/locales/en/x.ftl", "a = A")]);
        var named = TestCompilation.Generate([("/p/locales/en/x.ftl", "a = A")], new Dictionary<string, string> { [StringsGenerator.NamespaceProperty] = "My.Texts" });

        Assert.Contains("namespace Acme.Todo", plain.Generated, StringComparison.Ordinal);
        Assert.Contains("namespace My.Texts", named.Generated, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheEnglishFilesMakeTheClass()
    {
        var result = TestCompilation.Generate([("/p/locales/en/x.ftl", "a = A"), ("/p/locales/nl/x.ftl", "b = B"), ("/p/notes/x.ftl", "c = C")]);
        var none = TestCompilation.Generate([("/p/locales/nl/x.ftl", "b = B")]);

        Assert.Contains("public static string A =>", result.Generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public static string B", result.Generated, StringComparison.Ordinal);
        Assert.DoesNotContain("public static string C", result.Generated, StringComparison.Ordinal);
        Assert.Null(none.Generated);
    }

    [Fact]
    public void ASyntaxErrorIsReportedWhereItIs()
    {
        var result = TestCompilation.Generate([("/p/locales/en/x.ftl", "fine = Fine\nbroken = { $x\nafter = After")]);

        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("RSL0001", error.Id);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        var span = error.Location.GetLineSpan();
        Assert.Equal("/p/locales/en/x.ftl", span.Path);
        Assert.Equal((2, 0), (span.StartLinePosition.Line, span.StartLinePosition.Character));
        Assert.Contains("public static string Fine", result.Generated, StringComparison.Ordinal);
        Assert.Contains("public static string After", result.Generated, StringComparison.Ordinal);
    }

    [Fact]
    public void AMessageDefinedTwiceIsAnError()
    {
        var result = TestCompilation.Generate([("/p/locales/en/a.ftl", "same = One"), ("/p/locales/en/b.ftl", "other = Other\nsame = Two")]);

        var error = Assert.Single(result.Diagnostics);
        Assert.Equal("RSL0002", error.Id);
        Assert.Equal("/p/locales/en/b.ftl", error.Location.GetLineSpan().Path);
        Assert.Equal(1, error.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void MessagesThatBecomeTheSameNameAreReported()
    {
        var result = TestCompilation.Generate([("/p/locales/en/a.ftl", "save-all = One\nsave_all = Two\nstrings = Three")]);

        Assert.Equal(["RSL0003", "RSL0003"], result.Diagnostics.Select(d => d.Id));
        Assert.Contains("Strings.SaveAll", result.Diagnostics[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        Assert.Equal(2, result.Generated!.Split("public static string SaveAll").Length);
    }

    [Theory]
    [InlineData("a = { $count ->\n    [one] One\n   *[other] Many\n}", "double count")]
    [InlineData("a = { $count ->\n    [0] None\n   *[other] Many\n}", "double count")]
    [InlineData("a = { $kind ->\n    [file] A file\n   *[other] Something\n}", "string kind")]
    [InlineData("a = { NUMBER($size, maximumFractionDigits: 1) }", "double size")]
    [InlineData("a = { DATETIME($when) }", "global::System.DateTimeOffset when")]
    [InlineData("a = Hello { $name }", "string name")]
    [InlineData("# Variables:\n#   $n (Number) - a number\na = { $n }", "double n")]
    [InlineData("a = { $file-name } and { $class }", "string fileName, string @class")]
    public void AVariablesTypeComesFromHowTheMessageUsesIt(string fluent, string parameters)
    {
        var result = TestCompilation.Generate([("/p/locales/en/x.ftl", fluent)]);

        Assert.Contains($"public static string A({parameters})", result.Generated, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutRunesmithTheMessagesShowInTheirEnglishSource()
    {
        const string Fluent = """
            -maker = Acme
                .gender = neuter
            -thing = { $case ->
                [lower] thing
               *[other] Thing
            }
            owned = { -maker.gender ->
                [neuter] Its
               *[other] Their
            } { -thing(case: "lower") }
            share = { NUMBER($part, style: "percent") } done
            exact = { NUMBER($size, minimumFractionDigits: 2) } MB
            see = See { owned }
            """;
        var use = """
            namespace Acme.Todo;

            public static class Use
            {
                public static string[] All() =>
                [
                    Strings.PanelTitle,
                    Strings.TodoCount(0, "src"),
                    Strings.TodoCount(1, "src"),
                    Strings.TodoCount(1234, "src"),
                    Strings.About,
                    Strings.SaveButton("src").Tooltip,
                    Strings.OnlyAttributes.Title,
                    Strings.Class,
                    Strings.Owned,
                    Strings.Share(0.25),
                    Strings.Exact(1.5),
                    Strings.See,
                ];
            }
            """;
        var result = TestCompilation.Generate([("/plugin/locales/en/acme.todo.ftl", English), ("/plugin/locales/en/more.ftl", Fluent)],
            new Dictionary<string, string> { [StringsGenerator.NamespaceProperty] = "Acme.Todo" }, use);
        using var image = new MemoryStream();
        var emitted = result.Output.Emit(image, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));

        var context = new System.Runtime.Loader.AssemblyLoadContext("strings", isCollectible: true);
        try
        {
            image.Position = 0;
            var all = (string[])context.LoadFromStream(image).GetType("Acme.Todo.Use")!.GetMethod("All")!.Invoke(null, null)!;

            Assert.Equal(
            [
                "To-dos", "No to-dos in src", "One to-do in src", "1,234 to-dos in src", "About Acme", "Save the to-dos of src", "A title",
                "Escaped & <marked>", "Its thing", "25% done", "1.50 MB", "See Its thing",
            ], all);
        }
        finally
        {
            context.Unload();
        }
    }
}
