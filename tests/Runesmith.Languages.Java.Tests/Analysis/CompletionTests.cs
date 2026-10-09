using Runesmith.LanguageServices;

namespace Runesmith.Languages.Java.Tests.Analysis;

public sealed class CompletionTests
{
    private static Dictionary<string, string> Folder(params (string Path, string Text)[] files) => files.ToDictionary(f => f.Path, f => f.Text);

    private static Dictionary<string, string> Maven(int release, params (string Path, string Text)[] files)
    {
        var all = Folder(files);
        all["pom.xml"] = JavaTestProject.Pom(release);
        return all;
    }

    private static string Labels(CompletionResult result) => string.Join(", ", result.Items.Take(30).Select(i => i.Label));

    [Fact]
    public async Task MemberAccessOnAFieldOffersTheMembersOfItsType()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = project.Open("App.java", "class App { void run() { System.out.$$ } }");

        var result = await project.CompleteMemberAsync("App.java", offset);

        Assert.Contains(result.Items, i => i.Label == "println" && i.Kind == CompletionKind.Method);
        Assert.Equal(offset, result.ReplaceSpan.Start);
    }

    [Fact]
    public async Task StringMembersFollowTheRelease()
    {
        const string Text = "class App { void run() { \"abc\".$$ } }";
        await using var modern = await JavaTestProject.CreateAsync(Maven(11, ("src/main/java/App.java", "")));
        var offset = modern.Open("src/main/java/App.java", Text);
        var withStrip = await modern.CompleteMemberAsync("src/main/java/App.java", offset);

        await using var old = await JavaTestProject.CreateAsync(Maven(8, ("src/main/java/App.java", "")));
        offset = old.Open("src/main/java/App.java", Text);
        var withoutStrip = await old.CompleteMemberAsync("src/main/java/App.java", offset);

        Assert.Contains(withStrip.Items, i => i.Label == "length");
        Assert.Contains(withStrip.Items, i => i.Label == "strip");
        Assert.Contains(withoutStrip.Items, i => i.Label == "length");
        Assert.DoesNotContain(withoutStrip.Items, i => i.Label == "strip");
    }

    [Fact]
    public async Task LocalsComeBeforeTypesThatMatchAsWell()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = project.Open("App.java", "class App { void run() { int Objectz = 1; int total = Obje$$ } }");

        var result = await project.CompleteAsync("App.java", offset);
        var items = result.Items.ToList();
        var local = items.FindIndex(i => i.Label == "Objectz");
        var type = items.FindIndex(i => i.Label == "Objects");

        Assert.True(local >= 0 && type >= 0, Labels(result));
        Assert.True(local < type, Labels(result));
        Assert.Equal(CompletionKind.Variable, items[local].Kind);
        Assert.Equal(0, items[local].SortGroup);
        Assert.Equal(2, items[type].SortGroup);
    }

    [Fact]
    public async Task TypesThatAreNotImportedAreOfferedWithTheirImport()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("app/App.java", "")));
        var text = "package app;\n\nimport java.io.File;\n\nclass App { void run() { var list = new Arr$$ } }";
        var offset = project.Open("app/App.java", text);

        var result = await project.CompleteAsync("app/App.java", offset);
        var arrayList = Assert.Single(result.Items, i => i.Label == "ArrayList" && i.Detail == "java.util");
        var details = await project.Host.ResolveAsync(arrayList, TestContext.Current.CancellationToken);

        var change = Assert.Single(details!.AdditionalChanges);
        Assert.Equal(text.IndexOf("import java.io.File;", StringComparison.Ordinal) + "import java.io.File;".Length, change.Span.Start);
        Assert.Equal("\nimport java.util.ArrayList;", change.NewText);
    }

    [Fact]
    public async Task ImportedTypesNeedNoImport()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = project.Open("App.java", "import java.util.ArrayList;\nclass App { void run() { var list = new Arr$$ } }");

        var result = await project.CompleteAsync("App.java", offset);
        var arrayList = Assert.Single(result.Items, i => i.Label == "ArrayList");
        var details = await project.Host.ResolveAsync(arrayList, TestContext.Current.CancellationToken);

        Assert.Empty(details!.AdditionalChanges);
        Assert.Contains("java.util.ArrayList", details.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordsHaveTheirAccessors()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("Point.java", "record Point(int x, int y) { int sum() { return x + y; } }")));
        var offset = project.Open("App.java", "class App { void run(Point p) { p.$$ } }");

        var result = await project.CompleteMemberAsync("App.java", offset);

        Assert.Contains(result.Items, i => i.Label == "x" && i.Kind == CompletionKind.Method);
        Assert.Contains(result.Items, i => i.Label == "y");
        Assert.Contains(result.Items, i => i.Label == "sum");
        Assert.Contains(result.Items, i => i.Label == "hashCode");
    }

    [Fact]
    public async Task TypeArgumentsAreSubstituted()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = project.Open("App.java", "import java.util.List;\nclass App { void run() { List<String> l = null; l.get(0).$$ } }");

        var result = await project.CompleteMemberAsync("App.java", offset);

        Assert.Contains(result.Items, i => i.Label == "length");
        Assert.Contains(result.Items, i => i.Label == "isBlank");
    }

    [Fact]
    public async Task VarTakesTheTypeOfItsInitializer()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var text = "import java.util.HashMap;\nclass App { void run() { var x = new HashMap<String, Integer>(); x.$$ } }";
        var offset = project.Open("App.java", text);

        var members = await project.CompleteMemberAsync("App.java", offset);
        offset = project.Insert("App.java", offset, "get(\"a\").");
        var value = await project.CompleteMemberAsync("App.java", offset);

        Assert.Contains(members.Items, i => i.Label == "put");
        Assert.Contains(members.Items, i => i.Label == "computeIfAbsent");
        Assert.Contains(value.Items, i => i.Label == "intValue");
    }

    [Fact]
    public async Task InheritedMembersAreOffered()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(
            ("Base.java", "class Base { protected int count; void reset() { } }"),
            ("Derived.java", "import java.util.ArrayList;\nclass Derived extends Base { void more() { } }")));
        var offset = project.Open("App.java", "import java.util.ArrayList;\nclass App { void run(Derived d, ArrayList<String> list) { d.$$ } }");

        var derived = await project.CompleteMemberAsync("App.java", offset);
        offset = project.Insert("App.java", offset, "hashCode(); list.");
        var list = await project.CompleteMemberAsync("App.java", offset);

        Assert.Contains(derived.Items, i => i.Label == "reset");
        Assert.Contains(derived.Items, i => i.Label == "count");
        Assert.Contains(derived.Items, i => i.Label == "more");
        Assert.Contains(derived.Items, i => i.Label == "toString");
        Assert.Contains(list.Items, i => i.Label == "stream");
        Assert.Contains(list.Items, i => i.Label == "add");
    }

    [Fact]
    public async Task StaticImportsBringTheirMembers()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = project.Open("App.java", "import static java.lang.Math.max;\nimport static java.util.Collections.*;\nclass App { void run() { $$ } }");

        var max = await project.CompleteAsync("App.java", project.Insert("App.java", offset, "ma"));
        var sort = await project.CompleteAsync("App.java", project.Insert("App.java", offset + 2, "; emptyL"));

        Assert.Contains(max.Items, i => i.Label == "max" && i.Kind == CompletionKind.Method);
        Assert.DoesNotContain(max.Items, i => i.Label == "min");
        Assert.Contains(sort.Items, i => i.Label == "emptyList");
    }

    [Fact]
    public async Task LambdaParametersTakeTheirTypesFromTheTarget()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = project.Open("App.java", "import java.util.List;\nclass App { void run(List<String> names) { names.forEach(name -> name.$$); } }");

        var result = await project.CompleteMemberAsync("App.java", offset);

        Assert.Contains(result.Items, i => i.Label == "toUpperCase");
    }

    [Fact]
    public async Task EnhancedForTakesTheElementType()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = project.Open("App.java", "import java.util.List;\nclass App { void run(List<String> names, int[] numbers) { for (var name : names) { name.$$ } } }");

        var result = await project.CompleteMemberAsync("App.java", offset);

        Assert.Contains(result.Items, i => i.Label == "charAt");
    }

    [Fact]
    public async Task RecordIsAKeywordOnlyFromJava16()
    {
        const string Text = "class App {\n    rec$$\n}";
        await using var modern = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = modern.Open("App.java", Text);
        var withRecord = await modern.CompleteAsync("App.java", offset);

        await using var old = await JavaTestProject.CreateAsync(Maven(11, ("src/main/java/App.java", "")));
        offset = old.Open("src/main/java/App.java", Text);
        var withoutRecord = await old.CompleteAsync("src/main/java/App.java", offset);

        Assert.Contains(withRecord.Items, i => i.Label == "record" && i.Kind == CompletionKind.Keyword && i.SortGroup == 3);
        Assert.DoesNotContain(withoutRecord.Items, i => i.Label == "record");
    }

    [Fact]
    public async Task StatementKeywordsAndMembersAreOfferedAtAStatementStart()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = project.Open("App.java", "class App { int total; void run(int limit) { int i = 0;\n $$\n } }");

        var result = await project.CompleteAsync("App.java", offset);

        Assert.Contains(result.Items, i => i.Label == "limit" && i.SortGroup == 0);
        Assert.Contains(result.Items, i => i.Label == "i");
        Assert.Contains(result.Items, i => i.Label == "total" && i.SortGroup == 1);
        Assert.Contains(result.Items, i => i.Label == "run");
        Assert.Contains(result.Items, i => i.Label == "while" && i.Kind == CompletionKind.Keyword);
        Assert.Contains(result.Items, i => i.Label == "var");
        Assert.Contains(result.Items, i => i.Label == "String");
    }

    [Fact]
    public async Task ImportsCompletePackagesAndTypes()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = project.Open("App.java", "import java.util.$$\nclass App { }");

        var result = await project.CompleteAsync("App.java", offset);

        Assert.Contains(result.Items, i => i.Label == "concurrent" && i.Kind == CompletionKind.Module);
        Assert.Contains(result.Items, i => i.Label == "List");
    }

    [Fact]
    public async Task ModuleImportsCompleteModuleNames()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = project.Open("App.java", "import module java.$$\nclass App { }");

        var result = await project.CompleteAsync("App.java", offset);

        Assert.Contains(result.Items, i => i.Label == "base");
        Assert.Contains(result.Items, i => i.Label == "sql");
    }

    [Fact]
    public async Task TypingMoreOfTheSameWordFiltersTheKeptList()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var offset = project.Open("App.java", "class App { void run() { System.out.$$ } }");
        await project.CompleteMemberAsync("App.java", offset);
        var computed = project.Analyzer.ComputedCompletionLists;

        offset = project.Insert("App.java", offset, "pri");
        var result = await project.CompleteAsync("App.java", offset, CompletionTriggerKind.Incomplete);

        Assert.Equal(computed, project.Analyzer.ComputedCompletionLists);
        Assert.Equal("print", result.Items[0].Label);
        Assert.All(result.Items, i => Assert.Contains("p", i.Label, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompactSourceFilesSeeJavaBase()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("Main.java", "")));
        var text = "void main() {\n    List<String> names = new ArrayList<>();\n    IO.println(names.size());\n    greet();\n    names.$$\n}\n\nvoid greet() { }\n";
        var offset = project.Open("Main.java", text);

        var members = await project.CompleteMemberAsync("Main.java", offset);
        var io = await project.CompleteMemberAsync("Main.java", project.Insert("Main.java", offset, "clear(); IO."));
        var problems = await project.DiagnosticsAsync("Main.java");

        Assert.Contains(members.Items, i => i.Label == "add");
        Assert.Contains(io.Items, i => i.Label == "readln");
        Assert.DoesNotContain(problems, p => p.Code is "JAVA-TYPE" or "JAVA-IMPORT" or "JAVA-MEMBER");
    }

    [Fact]
    public async Task TopLevelMethodsOfACompactSourceFileAreInScope()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("Main.java", "")));
        var offset = project.Open("Main.java", "void main() {\n    gre$$\n}\n\nvoid greet() { }\n");

        var result = await project.CompleteAsync("Main.java", offset);

        Assert.Contains(result.Items, i => i.Label == "greet" && i.Kind == CompletionKind.Method);
    }

    [Fact]
    public async Task NothingIsOfferedInCommentsAndStrings()
    {
        await using var project = await JavaTestProject.CreateAsync(Folder(("App.java", "")));
        var comment = project.Open("App.java", "class App { void run() { // Sys$$\n String s = \"Sys\"; } }");

        var inComment = await project.CompleteAsync("App.java", comment);

        Assert.Empty(inComment.Items);
    }
}
