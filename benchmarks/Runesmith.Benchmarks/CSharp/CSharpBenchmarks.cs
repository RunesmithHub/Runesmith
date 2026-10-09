using System.Diagnostics;
using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using Runesmith.Languages.CSharp;
using Runesmith.LanguageServices;
using Runesmith.Text;

namespace Runesmith.Benchmarks.CSharp;

/// <summary>The C# analyzer's answer times, through the language service host, on a generated project and on the Runesmith solution.</summary>
/// <remarks>Every measured request first makes a new document version with an edit before the caret, as typing does, so the compiler
/// answers for changed text instead of repeating an answer it has.</remarks>
[Config(typeof(Config))]
public class CSharpBenchmarks : IDisposable
{
    private const string Anchor = "/* bench */";

    private LanguageServiceHost host = null!;
    private string? root;
    private string path = "";
    private TextSnapshot snapshot = TextSnapshot.Empty;
    private int memberOffset;
    private int statementOffset;
    private int callOffset;
    private bool padded;

    [Params("generated", "runesmith")]
    public string Workspace { get; set; } = "generated";

    [GlobalSetup]
    public async Task SetupAsync()
    {
        string text;
        (root, path, text) = Workspace == "runesmith" ? RunesmithSolution() : await GeneratedProjectAsync();
        host = new LanguageServiceHost(Path.Combine(Path.GetTempPath(), "runesmith-bench-cache"), (_, _) => { });
        var analyzer = new CSharpAnalyzer();
        host.Add(analyzer);
        await host.OpenWorkspaceAsync(root);
        await analyzer.Loaded;

        var anchor = text.IndexOf(Anchor, StringComparison.Ordinal);
        const string Body = "var builder = new System.Text.StringBuilder(); builder.Append(1, ); builder.";
        text = string.Concat(text.AsSpan(0, anchor), Body, text.AsSpan(anchor + Anchor.Length));
        memberOffset = anchor + Body.Length;
        statementOffset = anchor;
        callOffset = anchor + Body.IndexOf("1, ", StringComparison.Ordinal) + "1, ".Length;
        snapshot = TextSnapshot.Create(text);
        host.Open(path, CSharpAnalyzer.LanguageId, snapshot);
        for (var i = 0; i < 3; i++)
            await MemberAccessAsync();
    }

    [GlobalCleanup]
    public void Dispose()
    {
        host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Workspace == "generated" && root is not null)
            Directory.Delete(root, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Benchmark(Description = "Completion after a dot")]
    public async Task<int> MemberAccessAsync()
    {
        var shift = Edit();
        var result = await host.CompleteAsync(path, snapshot, memberOffset + shift, CompletionTriggerKind.Character, '.', CancellationToken.None);
        return result.Items.Count;
    }

    [Benchmark(Description = "Completion at a statement start")]
    public async Task<int> StatementStartAsync()
    {
        var shift = Edit();
        var result = await host.CompleteAsync(path, snapshot, statementOffset + shift, CompletionTriggerKind.Invoked, null, CancellationToken.None);
        return result.Items.Count;
    }

    [Benchmark(Description = "Completion for the same word again")]
    public async Task<int> SameWordAsync()
    {
        var result = await host.CompleteAsync(path, snapshot, memberOffset + (padded ? 1 : 0), CompletionTriggerKind.Incomplete, null, CancellationToken.None);
        return result.Items.Count;
    }

    [Benchmark(Description = "Hover")]
    public async Task<string?> HoverAsync()
    {
        var shift = Edit();
        var hover = await host.HoverAsync(path, snapshot, memberOffset + shift - 3, CancellationToken.None);
        return hover?.Markdown;
    }

    [Benchmark(Description = "Signature help")]
    public async Task<int> SignatureHelpAsync()
    {
        var shift = Edit();
        var help = await host.SignatureHelpAsync(path, snapshot, callOffset + shift, CancellationToken.None);
        return help?.Signatures.Count ?? 0;
    }

    [Benchmark(Description = "Applying an edit")]
    public int Change() => Edit();

    // Adds or removes a space at the start of the file, so every request sees a new version and the text before its word changed.
    private int Edit()
    {
        var change = padded ? new TextChange(new TextSpan(0, 1), "") : new TextChange(new TextSpan(0, 0), " ");
        snapshot = snapshot.Apply([change]);
        host.Change(path, snapshot, [change]);
        padded = !padded;
        return padded ? 1 : 0;
    }

    // The open document's text takes the place of the file in the solution, so the file on disk is only read.
    private static (string Root, string Path, string Text) RunesmithSolution()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "Runesmith.slnx")))
            folder = folder.Parent;
        if (folder is null)
            throw new InvalidOperationException("The benchmarks must run inside the Runesmith repository.");

        var file = Path.Combine(folder.FullName, "src", "Runesmith.Text", "TextBuffer.cs");
        var text = File.ReadAllText(file);
        var body = text.IndexOf('{', text.IndexOf("    public TextChangeSet Apply(", StringComparison.Ordinal)) + 1;
        return (folder.FullName, file, string.Concat(text.AsSpan(0, body), "\n        ", Anchor, text.AsSpan(body)));
    }

    private static async Task<(string Root, string Path, string Text)> GeneratedProjectAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "runesmith-bench-csharp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "Generated.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        for (var type = 0; type < 150; type++)
        {
            var text = new StringBuilder($"namespace Generated.Area{type % 10};\n\npublic sealed class Type{type}\n{{\n");
            for (var member = 0; member < 20; member++)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"    public int Value{member} {{ get; set; }}");
                text.AppendLine(CultureInfo.InvariantCulture, $"    public string Describe{member}(int count, string label) => label + count + Value{member};");
            }

            text.AppendLine("}");
            await File.WriteAllTextAsync(Path.Combine(root, $"Type{type}.cs"), text.ToString());
        }

        var main = Path.Combine(root, "Program.cs");
        var program = $"var first = new Generated.Area0.Type0();\n{Anchor}\nConsole.WriteLine(first.Describe0(1, \"a\"));\n";
        await File.WriteAllTextAsync(main, program);
        using var restore = Process.Start(new ProcessStartInfo("dotnet", ["restore", root, "--verbosity", "quiet"]) { UseShellExecute = false })!;
        await restore.WaitForExitAsync();
        return (root, main, program);
    }

    private sealed class Config : ManualConfig
    {
        public Config()
        {
            AddColumn(StatisticColumn.P95);
            AddColumn(StatisticColumn.Max);
        }
    }
}
