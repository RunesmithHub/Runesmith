namespace Runesmith.Languages.Java.Tests.Analysis;

public sealed class NavigationTests
{
    private const string Greeter = """
        package app;

        /** Says hello. */
        public class Greeter {
            /**
             * Greets someone by {@code name}.
             *
             * @param name who to greet
             * @return the greeting
             */
            public String greet(String name) {
                return "Hello, " + name;
            }
        }
        """;

    private static Dictionary<string, string> Files() => new()
    {
        ["app/Greeter.java"] = Greeter,
        ["app/App.java"] = "",
    };

    [Fact]
    public async Task HoverShowsTheSignatureAndTheDocumentationAsMarkdown()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/App.java", "package app;\nclass App { void run(Greeter g) { g.gr$$eet(\"Ada\"); } }");

        var hover = await project.HoverAsync("app/App.java", offset);

        Assert.NotNull(hover);
        Assert.Contains("```java\npublic String Greeter.greet(String name)\n```", hover.Markdown, StringComparison.Ordinal);
        Assert.Contains("Greets someone by `name`.", hover.Markdown, StringComparison.Ordinal);
        Assert.Contains("- `name` who to greet", hover.Markdown, StringComparison.Ordinal);
        Assert.Contains("**Returns** the greeting", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HoverOnAJdkMemberShowsItsSignature()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/App.java", "package app;\nclass App { void run() { System.out.print$$ln(\"x\"); } }");

        var hover = await project.HoverAsync("app/App.java", offset);

        Assert.NotNull(hover);
        Assert.Contains("void PrintStream.println(String", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HoverOnALocalShowsItsType()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/App.java", "package app;\nimport java.util.*;\nclass App { void run() { var names = new ArrayList<String>(); na$$mes.clear(); } }");

        var hover = await project.HoverAsync("app/App.java", offset);

        Assert.NotNull(hover);
        Assert.Contains("ArrayList<String> names", hover.Markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DefinitionFindsDeclarationsInOtherFiles()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/App.java", "package app;\nclass App { void run(Gree$$ter g) { g.greet(\"Ada\"); } }");

        var type = Assert.Single(await project.DefinitionAsync("app/App.java", offset));
        var method = Assert.Single(await project.DefinitionAsync("app/App.java", project.Snapshot("app/App.java").GetText().IndexOf("greet", StringComparison.Ordinal) + 1));

        Assert.Equal(project.FullPath("app/Greeter.java"), type.Path);
        Assert.Equal(3, type.Start.Line);
        Assert.Equal(13, type.Start.Column);
        Assert.Equal(project.FullPath("app/Greeter.java"), method.Path);
        Assert.Equal(10, method.Start.Line);
    }

    [Fact]
    public async Task DefinitionFindsLocals()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/App.java", "package app;\nclass App { void run(int count) { System.out.println(cou$$nt); } }");

        var local = Assert.Single(await project.DefinitionAsync("app/App.java", offset));

        Assert.Equal(project.FullPath("app/App.java"), local.Path);
        Assert.Equal(1, local.Start.Line);
        Assert.Equal("package app;\nclass App { void run(int ".Length - "package app;\n".Length, local.Start.Column);
    }

    [Fact]
    public async Task SignatureHelpListsOverloadsAndTheActiveParameter()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/App.java", "package app;\nclass App { void run() { Math.max(1, $$); } }");

        var help = await project.SignatureHelpAsync("app/App.java", offset);

        Assert.NotNull(help);
        Assert.Equal(1, help.ActiveParameter);
        Assert.Contains(help.Signatures, s => s.Label == "int max(int arg0, int arg1)");
        Assert.Contains(help.Signatures, s => s.Label == "double max(double arg0, double arg1)");
        var active = help.Signatures[help.ActiveSignature];
        Assert.Equal(2, active.Parameters.Count);
        Assert.Equal("int arg1", active.Label.Substring(active.Parameters[1].Start, active.Parameters[1].Length));
    }

    [Fact]
    public async Task SignatureHelpShowsSourceDocumentation()
    {
        await using var project = await JavaTestProject.CreateAsync(Files());
        var offset = project.Open("app/App.java", "package app;\nclass App { void run(Greeter g) { g.greet($$); } }");

        var help = await project.SignatureHelpAsync("app/App.java", offset);

        Assert.NotNull(help);
        var signature = Assert.Single(help.Signatures);
        Assert.Equal("String greet(String name)", signature.Label);
        Assert.Contains("Greets someone", signature.Documentation, StringComparison.Ordinal);
        Assert.Equal(0, help.ActiveParameter);
    }
}
