using BenchmarkDotNet.Attributes;
using Runesmith.Editor.Completion;
using Runesmith.LanguageServices;
using Runesmith.Text;

namespace Runesmith.Benchmarks.Completion;

/// <summary>Filtering and ranking 2,000 suggestions, in the language services and in the editor; the goal for each is under 2 ms.</summary>
[MemoryDiagnoser]
public class CompletionRankingBenchmarks
{
    private string[] labels = [];
    private TextSnapshot snapshot = TextSnapshot.Empty;

    [Params("W", "Wri", "WrLn")]
    public string Typed { get; set; } = "";

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(7);
        string[] parts = ["Write", "Read", "Line", "Get", "Set", "Async", "Value", "Name", "Count", "Item", "Text", "Span", "Buffer", "Lock", "Format"];
        labels = [.. Enumerable.Range(0, 2000).Select(i => string.Concat(Enumerable.Range(0, 1 + random.Next(3)).Select(_ => parts[random.Next(parts.Length)])) + i)];
        snapshot = TextSnapshot.Create("value." + Typed);
    }

    [Benchmark]
    public int LanguageServicesSink()
    {
        var sink = new CompletionSink(snapshot, snapshot.Length);
        foreach (var label in labels)
            sink.Add(new CompletionCandidate(label, CompletionKind.Method));
        return sink.ToResult(1).Items.Count;
    }

    [Benchmark]
    public int EditorFilter()
    {
        var matched = new List<int>();
        var typedMask = CompletionFilter.MaskOf(Typed);
        var count = 0;
        foreach (var label in labels)
        {
            if ((typedMask & ~CompletionFilter.MaskOf(label)) == 0 && CompletionFilter.Score(label, Typed, matched) is not null)
                count++;
        }

        return count;
    }
}
