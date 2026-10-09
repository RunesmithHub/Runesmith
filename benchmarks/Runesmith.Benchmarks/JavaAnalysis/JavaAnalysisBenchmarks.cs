using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using Runesmith.Languages.Java.Analysis;
using Runesmith.LanguageServices;
using Runesmith.Text;

namespace Runesmith.Benchmarks.JavaAnalysis;

/// <summary>The Java analyzer's answer times, through the language service host, on a generated project of 150 classes.</summary>
/// <remarks>Every measured request first makes a new document version with an edit inside the method before the caret, as typing does, so
/// the analyzer parses the change and answers for the new text instead of repeating an answer it has.</remarks>
[Config(typeof(Config))]
public class JavaAnalysisBenchmarks : IDisposable
{
    private const string Anchor = "/* bench */";
    private const string Edited = "/* edit */";

    private LanguageServiceHost host = null!;
    private JavaAnalyzer analyzer = null!;
    private string root = "";
    private string path = "";
    private string largePath = "";
    private TextSnapshot snapshot = TextSnapshot.Empty;
    private TextSnapshot large = TextSnapshot.Empty;
    private int editOffset;
    private int memberOffset;
    private int statementOffset;
    private int hoverOffset;
    private int largeEditOffset;
    private bool padded;
    private bool largePadded;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        root = Path.Combine(Path.GetTempPath(), "runesmith-bench-java", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        for (var type = 0; type < 150; type++)
            await File.WriteAllTextAsync(Path.Combine(Folder(type), $"Type{type}.java"), GeneratedType(type));

        path = Path.Combine(Folder(0), "Program.java");
        var program = $$"""
            package generated.area0;

            import java.util.ArrayList;
            import java.util.List;
            import java.util.Map;

            public class Program {
                private final Map<String, List<Type10>> groups = new java.util.HashMap<>();

                public void run(List<String> names) {
                    {{Edited}}
                    var builder = new StringBuilder();
                    Type0 first = new Type0();
                    {{Anchor}}
                    System.out.println(first.describe0(1, "a"));
                }
            }
            """;
        await File.WriteAllTextAsync(path, program);
        largePath = Path.Combine(Folder(0), "Large.java");
        var largeText = LargeFile();
        await File.WriteAllTextAsync(largePath, largeText);

        host = new LanguageServiceHost(Path.Combine(Path.GetTempPath(), "runesmith-bench-cache"), (_, _) => { });
        analyzer = new JavaAnalyzer();
        host.Add(analyzer);
        await host.OpenWorkspaceAsync(root);
        await analyzer.Loaded;

        const string Body = "builder.";
        var anchor = program.IndexOf(Anchor, StringComparison.Ordinal);
        var text = string.Concat(program.AsSpan(0, anchor), Body, program.AsSpan(anchor + Anchor.Length));
        editOffset = text.IndexOf(Edited, StringComparison.Ordinal);
        memberOffset = anchor + Body.Length;
        statementOffset = anchor;
        hoverOffset = text.IndexOf("describe0", StringComparison.Ordinal) + 3;
        snapshot = TextSnapshot.Create(text);
        host.Open(path, JavaAnalyzer.LanguageId, snapshot);

        large = TextSnapshot.Create(largeText);
        largeEditOffset = largeText.IndexOf(Edited, StringComparison.Ordinal);
        host.Open(largePath, JavaAnalyzer.LanguageId, large);
        for (var i = 0; i < 3; i++)
        {
            await MemberAccessAsync();
            await StatementStartAsync();
            await DiagnosticsAsync();
        }
    }

    [GlobalCleanup]
    public void Dispose()
    {
        host.DisposeAsync().AsTask().GetAwaiter().GetResult();
        if (Directory.Exists(root))
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
        var hover = await host.HoverAsync(path, snapshot, hoverOffset + shift, CancellationToken.None);
        return hover?.Markdown;
    }

    [Benchmark(Description = "Diagnostics of a 2,000-line file")]
    public async Task<int> DiagnosticsAsync()
    {
        var change = largePadded ? new TextChange(new TextSpan(largeEditOffset, 1), "") : new TextChange(new TextSpan(largeEditOffset, 0), " ");
        large = large.Apply([change]);
        host.Change(largePath, large, [change]);
        largePadded = !largePadded;
        var problems = await analyzer.DiagnosticsAsync(host.GetDocument(largePath)!, CancellationToken.None);
        return problems.Count;
    }

    // Adds or removes a space inside the method before the caret, so every request sees a new version.
    private int Edit()
    {
        var change = padded ? new TextChange(new TextSpan(editOffset, 1), "") : new TextChange(new TextSpan(editOffset, 0), " ");
        snapshot = snapshot.Apply([change]);
        host.Change(path, snapshot, [change]);
        padded = !padded;
        return padded ? 1 : 0;
    }

    private string Folder(int type)
    {
        var folder = Path.Combine(root, "generated", "area" + (type % 10).ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string GeneratedType(int type)
    {
        var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"package generated.area{type % 10};\n\nimport java.util.List;\n\n");
        text.Append(CultureInfo.InvariantCulture, $"/** Generated type {type}. */\npublic class Type{type} {{\n");
        for (var member = 0; member < 20; member++)
        {
            text.Append(CultureInfo.InvariantCulture, $"    private int value{member};\n\n");
            text.Append(CultureInfo.InvariantCulture, $"    /** Describes {{@code count}} items. @param count how many @return the text */\n");
            text.Append(CultureInfo.InvariantCulture, $"    public String describe{member}(int count, String label) {{ return label + count + value{member}; }}\n\n");
            text.Append(CultureInfo.InvariantCulture, $"    public List<String> names{member}(List<String> source) {{ return source.subList(0, value{member}); }}\n\n");
        }

        return text.Append("}\n").ToString();
    }

    // About 2,000 lines of ordinary code: fields, generic collections, locals, loops, lambdas and calls on JDK and project types.
    private static string LargeFile()
    {
        var text = new StringBuilder("package generated.area0;\n\nimport java.util.*;\nimport java.util.stream.Collectors;\n\npublic class Large {\n");
        text.Append("    private final Map<String, List<Integer>> index = new HashMap<>();\n\n");
        text.Append("    void edited() {\n        ").Append(Edited).Append("\n    }\n\n");
        for (var method = 0; method < 133; method++)
        {
            text.Append(CultureInfo.InvariantCulture, $$"""
                    public String method{{method}}(List<String> names, int limit) {
                        var builder = new StringBuilder();
                        Type{{method % 15 * 10}} helper = new Type{{method % 15 * 10}}();
                        for (var name : names) {
                            if (name.length() > limit && !name.isBlank()) {
                                builder.append(name.trim().toUpperCase()).append(',');
                            }
                        }
                        List<Integer> values = index.getOrDefault("key{{method}}", new ArrayList<>());
                        int total = values.stream().mapToInt(Integer::intValue).sum();
                        String joined = names.stream().filter(n -> n.startsWith("a")).collect(Collectors.joining(";"));
                        return builder.toString() + helper.describe{{method % 20}}(total, joined).substring(0);
                    }


                """);
        }

        return text.Append("}\n").ToString();
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
