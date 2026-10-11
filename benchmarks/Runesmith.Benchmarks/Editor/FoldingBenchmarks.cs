using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using Runesmith.Editor.Folding;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Benchmarks.Editor;

/// <summary>What folding costs an edit and a refresh in a C# file of 10,000 lines with a few blocks folded: typing a character should take
/// microseconds and allocate nothing.</summary>
[MemoryDiagnoser]
public class FoldingBenchmarks
{
    private TextBuffer buffer = new("");
    private FoldingModel model = new();
    private IReadOnlyList<FoldingRange> ranges = [];
    private int typingOffset;

    [GlobalSetup]
    public void Setup()
    {
        buffer = new TextBuffer(CSharpFile(240));
        var snapshot = buffer.Current;
        ranges = IndentationFolding.Compute(snapshot, 4);
        model = new FoldingModel();
        model.SetRanges(ranges, snapshot.LineCount);
        foreach (var region in model.Regions.Where((_, index) => index % 50 == 0))
            model.SetCollapsed(region, true);

        var line = snapshot.GetLine(snapshot.LineCount / 2);
        while (model.IsHidden(line.LineNumber))
            line = snapshot.GetLine(line.LineNumber + 1);
        typingOffset = line.End;
    }

    [Benchmark]
    public void TypeAndDeleteACharacter()
    {
        model.Map(buffer.Insert(typingOffset, "x"));
        model.Map(buffer.Delete(new TextSpan(typingOffset, 1)));
    }

    [Benchmark]
    public void InsertAndDeleteALine()
    {
        model.Map(buffer.Insert(typingOffset, "\n"));
        model.Map(buffer.Delete(new TextSpan(typingOffset, 1)));
    }

    [Benchmark]
    public IReadOnlyList<FoldingRange> ComputeByIndentation() => IndentationFolding.Compute(buffer.Current, 4);

    [Benchmark]
    public void RefreshWithTheSameRanges() => model.SetRanges(ranges, buffer.Current.LineCount);

    /// <summary>Gets a C# file of classes of 42 lines each, with fields, properties, methods, loops and a region.</summary>
    internal static string CSharpFile(int classes)
    {
        var text = new StringBuilder("namespace Sample;\n\n");
        for (var number = 1; number <= classes; number++)
        {
            text.Append(CultureInfo.InvariantCulture, $$"""
                public sealed class Widget{{number}}
                {
                    private readonly List<string> items = [];
                    private int count;

                    public string Name { get; set; } = "Widget{{number}}";

                    public void Add(string value)
                    {
                        if (string.IsNullOrEmpty(value))
                            throw new ArgumentException("A value is needed.", nameof(value));

                        items.Add(value);
                        count++;
                    }

                    public string Describe(int limit)
                    {
                        var builder = new StringBuilder(Name);
                        foreach (var item in items.Take(limit))
                        {
                            builder.Append(item).Append(", ");
                        }

                        return builder.ToString();
                    }

                    #region Totals
                    public int Total(IEnumerable<int> values)
                    {
                        var total = 0;
                        for (var i = 0; i < count; i++)
                        {
                            total += i % 3 == 0 ? values.Sum() : -i;
                        }

                        return total;
                    }
                    #endregion
                }


                """);
        }

        return text.ToString();
    }
}
