using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Runesmith.Sdk.Analyzers.CodeFixes;
using Runesmith.Sdk.Analyzers.Strings;
using Runesmith.Sdk.Analyzers.Text;

namespace Runesmith.Sdk.Analyzers.Tests;

public sealed class LocalizeTextCodeFixTests
{
    private const string FluentPath = "/plugin/Acme.Todo/locales/en/acme.todo.ftl";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ALiteralMovesIntoTheEnglishFileAndTheGeneratedMemberTakesItsPlace()
    {
        var code = """
            using Avalonia.Controls;

            namespace Acme.Todo;

            public static class View
            {
                public static TextBlock Build() => new() { Text = "Save all files" };
            }
            """;

        var (fluent, fixedCode) = await FixAsync(code, "existing = Existing\n");

        Assert.Equal("existing = Existing\nsave-all-files = Save all files\n", fluent);
        Assert.Contains("new() { Text = Strings.SaveAllFiles };", fixedCode, StringComparison.Ordinal);
        AssertCompiles(fluent!, fixedCode!);
    }

    [Fact]
    public async Task AnInterpolatedStringBecomesAMessageWithVariables()
    {
        var code = """
            using Avalonia.Controls;

            namespace Acme.Todo;

            public static class View
            {
                public static void Show(TextBlock block, string path, int count) => block.Text = $"Opened {path} with {count} files";
            }
            """;

        var (fluent, fixedCode) = await FixAsync(code, "existing = Existing");

        Assert.Equal("existing = Existing\nopened-path-with-count-files = Opened { $path } with { NUMBER($count) } files\n", fluent);
        Assert.True(fixedCode!.Contains("block.Text = Strings.OpenedPathWithCountFiles(path, count);", StringComparison.Ordinal), fixedCode);
        AssertCompiles(fluent!, fixedCode!);
    }

    [Fact]
    public async Task AnIdThatIsTakenGetsANumber()
    {
        var code = """
            namespace Acme.Todo;

            public static class View
            {
                public static void Build() => _ = new Avalonia.Controls.Button { Content = "Save" };
            }
            """;

        var (fluent, fixedCode) = await FixAsync(code, "save = Save\n");

        Assert.Equal("save = Save\nsave-2 = Save\n", fluent);
        Assert.Contains("Content = Strings.Save2", fixedCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutAnEnglishFileNoFixIsOffered()
    {
        var code = "public static class View { public static void Build() => _ = new Avalonia.Controls.TextBlock { Text = \"Hello\" }; }";

        var (fluent, fixedCode) = await FixAsync(code, null);

        Assert.Null(fluent);
        Assert.Null(fixedCode);
    }

    private static void AssertCompiles(string fluent, string code)
    {
        var result = TestCompilation.Generate([(FluentPath, fluent)], new Dictionary<string, string> { [StringsGenerator.NamespaceProperty] = "Acme.Todo" }, code);
        var errors = result.Output.GetDiagnostics(Token).Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
    }

    private static async Task<(string? Fluent, string? Code)> FixAsync(string code, string? fluent)
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var fluentId = DocumentId.CreateNewId(projectId);
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(), "Acme.Todo", "Acme.Todo", LanguageNames.CSharp, filePath: "/plugin/Acme.Todo/Acme.Todo.csproj",
                compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable),
                metadataReferences: TestCompilation.References,
                analyzerReferences: [new AnalyzerFileReference(typeof(StringsGenerator).Assembly.Location, new LoadedAssemblies())]))
            .AddDocument(documentId, "View.cs", SourceText.From(code), filePath: "/plugin/Acme.Todo/View.cs")
            .AddAnalyzerConfigDocument(DocumentId.CreateNewId(projectId), ".globalconfig",
                SourceText.From("is_global = true\nbuild_property.RunesmithStringsNamespace = Acme.Todo\nbuild_property.RunesmithLocalize = true\n"), filePath: "/plugin/Acme.Todo/.globalconfig");
        if (fluent is not null)
            solution = solution.AddAdditionalDocument(fluentId, "acme.todo.ftl", SourceText.From(fluent), filePath: FluentPath);

        var document = solution.GetDocument(documentId)!;
        var compilation = (await document.Project.GetCompilationAsync(Token))!;
        var diagnostics = await compilation.WithAnalyzers([new UserVisibleTextAnalyzer()], document.Project.AnalyzerOptions).GetAnalyzerDiagnosticsAsync(Token);
        var diagnostic = Assert.Single(diagnostics);

        var actions = new List<CodeAction>();
        await new LocalizeTextCodeFix().RegisterCodeFixesAsync(new CodeFixContext(document, diagnostic, (action, _) => actions.Add(action), Token));
        if (actions.Count == 0)
            return (null, null);

        var operation = Assert.Single((await Assert.Single(actions).GetOperationsAsync(Token)).OfType<ApplyChangesOperation>());
        var changed = operation.ChangedSolution;
        return ((await changed.GetAdditionalDocument(fluentId)!.GetTextAsync(Token)).ToString(), (await changed.GetDocument(documentId)!.GetTextAsync(Token)).ToString());
    }

    private sealed class LoadedAssemblies : IAnalyzerAssemblyLoader
    {
        public void AddDependencyLocation(string fullPath)
        {
        }

        public Assembly LoadFromPath(string fullPath) => typeof(StringsGenerator).Assembly;
    }
}
