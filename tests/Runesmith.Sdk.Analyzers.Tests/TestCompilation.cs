using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Runesmith.Sdk.Analyzers.Strings;

namespace Runesmith.Sdk.Analyzers.Tests;

/// <summary>Compiles test code against .NET, Avalonia and the SDK, with the SDK's generator and analyzer, as a plugin's build would.</summary>
internal static class TestCompilation
{
    public const string AssemblyName = "Acme.Todo";

    private static readonly Lazy<MetadataReference[]> Framework = new(() =>
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return [.. trusted.Where(path => Path.GetFileName(path) is var name
            && (name.StartsWith("System.", StringComparison.Ordinal) || name is "mscorlib.dll" or "netstandard.dll" or "Avalonia.Base.dll" or "Avalonia.Controls.dll"
                || name is "Avalonia.Markup.Xaml.dll" or "Runesmith.Sdk.dll" or "Runesmith.Text.dll"))
            .Select(path => MetadataReference.CreateFromFile(path))];
    });

    public static IReadOnlyList<MetadataReference> References => Framework.Value;

    /// <summary>Creates a compilation of the code, as the assembly <see cref="AssemblyName"/>.</summary>
    public static CSharpCompilation Create(params string[] sources) =>
        CSharpCompilation.Create(
            AssemblyName,
            sources.Select((source, index) => CSharpSyntaxTree.ParseText(source, path: $"Code{index}.cs")),
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

    /// <summary>Runs the Strings generator over the Fluent files, and gives the compilation with the generated code.</summary>
    public static GeneratorResult Generate(IEnumerable<(string Path, string Text)> files, IReadOnlyDictionary<string, string>? properties = null, params string[] sources)
    {
        var options = new TestOptions(properties ?? new Dictionary<string, string>());
        var texts = files.Select(f => (AdditionalText)new TestText(f.Path, f.Text)).ToImmutableArray();
        var driver = CSharpGeneratorDriver.Create([new StringsGenerator().AsSourceGenerator()], texts, CSharpParseOptions.Default, options);
        driver.RunGeneratorsAndUpdateCompilation(Create(sources), out var output, out var diagnostics);
        var generated = output.SyntaxTrees.FirstOrDefault(t => t.FilePath.EndsWith("Strings.g.cs", StringComparison.Ordinal))?.ToString();
        return new GeneratorResult(output, diagnostics, generated);
    }

    /// <summary>Runs an analyzer over the code with the given MSBuild properties.</summary>
    public static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(DiagnosticAnalyzer analyzer, IReadOnlyDictionary<string, string> properties, params string[] sources)
    {
        var compilation = Create(sources);
        var errors = compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
        var options = new AnalyzerOptions([], new TestOptions(properties));
        return await compilation.WithAnalyzers([analyzer], options).GetAnalyzerDiagnosticsAsync(TestContext.Current.CancellationToken);
    }

    internal sealed record GeneratorResult(Compilation Output, ImmutableArray<Diagnostic> Diagnostics, string? Generated);

    internal sealed class TestText(string path, string text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text);
    }

    internal sealed class TestOptions(IReadOnlyDictionary<string, string> properties) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(properties);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;

        private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
        {
            public static readonly Options Empty = new(new Dictionary<string, string>());

            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) => values.TryGetValue(key, out value);
        }
    }
}
