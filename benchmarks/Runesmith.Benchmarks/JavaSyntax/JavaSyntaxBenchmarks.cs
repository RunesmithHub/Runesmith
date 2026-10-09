using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Benchmarks.JavaSyntax;

/// <summary>Java's lexer and parser: lexing, parsing a small and a large file from scratch, and reparsing after typing in a method.</summary>
[Config(typeof(Config))]
[MemoryDiagnoser]
public class JavaSyntaxBenchmarks
{
    private string small = "";
    private string large = "";
    private JavaSyntaxTree largeTree = null!;
    private string edited = "";
    private TextEdit[] edits = [];

    [GlobalSetup]
    public void Setup()
    {
        small = JavaSamples.File(100);
        large = JavaSamples.File(10_000);
        largeTree = JavaSyntaxTree.Parse(large);
        var position = large.IndexOf("return Optional.empty();", large.Length / 2, StringComparison.Ordinal);
        edits = [new TextEdit(position, 0, "x")];
        edited = large.Insert(position, "x");
    }

    [Benchmark(Description = "Lex 10,000 lines")]
    public int Lex() => Lexer.LexAll(large, [], []).Count;

    [Benchmark(Description = "Parse 100 lines")]
    public JavaSyntaxTree ParseSmall() => JavaSyntaxTree.Parse(small);

    [Benchmark(Description = "Parse 10,000 lines")]
    public JavaSyntaxTree ParseLarge() => JavaSyntaxTree.Parse(large);

    [Benchmark(Description = "Reparse after typing in a method, 10,000 lines")]
    public JavaSyntaxTree Reparse() => largeTree.WithChanges(edited, edits);

    private sealed class Config : ManualConfig
    {
        public Config()
        {
            AddColumn(new MegabytesPerSecondColumn());
        }
    }

    /// <summary>Shows lexing and parsing as megabytes of source a second.</summary>
    private sealed class MegabytesPerSecondColumn : IColumn
    {
        private static readonly int LargeBytes = Encoding.UTF8.GetByteCount(JavaSamples.File(10_000));
        private static readonly int SmallBytes = Encoding.UTF8.GetByteCount(JavaSamples.File(100));

        public string Id => nameof(MegabytesPerSecondColumn);

        public string ColumnName => "MB/s";

        public bool AlwaysShow => true;

        public ColumnCategory Category => ColumnCategory.Custom;

        public int PriorityInCategory => 0;

        public bool IsNumeric => true;

        public UnitType UnitType => UnitType.Dimensionless;

        public string Legend => "Megabytes of source a second";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase) =>
            GetValue(summary, benchmarkCase, SummaryStyle.Default);

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
        {
            var bytes = benchmarkCase.Descriptor.WorkloadMethod.Name switch
            {
                nameof(Lex) or nameof(ParseLarge) => LargeBytes,
                nameof(ParseSmall) => SmallBytes,
                _ => 0,
            };
            if (bytes == 0 || summary[benchmarkCase]?.ResultStatistics is not { } statistics)
                return "-";

            return (bytes / 1_000_000.0 / (statistics.Mean / 1_000_000_000.0)).ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        }

        public bool IsAvailable(Summary summary) => true;

        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
    }
}
