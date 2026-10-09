using System.Diagnostics;
using System.Text;

namespace Runesmith.Languages.Java.Syntax.Tests;

/// <summary>Runs the performance tests alone, after the other tests, so that they do not share the processor.</summary>
[CollectionDefinition(nameof(PerformanceGroup), DisableParallelization = true)]
public sealed class PerformanceGroup;

/// <summary>Checks the performance goals, as the best of several runs. They hold for Release builds only.</summary>
[Trait("Category", "Performance")]
[Collection(nameof(PerformanceGroup))]
public sealed class PerformanceTests(ITestOutputHelper output)
{
    private static readonly string TenThousandLines = JavaSamples.File(10_000);

    private static void RequireRelease()
    {
#if DEBUG
        Assert.Skip("Performance goals are checked in Release builds");
#endif
    }

    private static double BestMilliseconds(int runs, Action action)
    {
        // The first runs compile the code; the goals are for code that is warm.
        for (var i = 0; i < 5; i++)
            action();

        var best = double.MaxValue;
        for (var i = 0; i < runs; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            action();
            best = Math.Min(best, stopwatch.Elapsed.TotalMilliseconds);
        }

        return best;
    }

    [Fact]
    public void LexingIsFasterThan100MegabytesASecond()
    {
        RequireRelease();
        var text = TenThousandLines;
        var bytes = Encoding.UTF8.GetByteCount(text);
        var best = BestMilliseconds(50, () => Lexer.LexAll(text, [], []));
        var megabytesPerSecond = bytes / 1_000_000.0 / (best / 1000);
        output.WriteLine($"{bytes / 1000} KB lexed in {best:F2} ms: {megabytesPerSecond:F0} MB/s");
        Assert.True(megabytesPerSecond > 100, $"{megabytesPerSecond:F0} MB/s");
    }

    [Fact]
    public void ParsingTenThousandLinesTakesUnder20Milliseconds()
    {
        RequireRelease();
        var text = TenThousandLines;
        Assert.InRange(text.Count(c => c == '\n'), 9_000, 10_000);
        var best = BestMilliseconds(30, () => JavaSyntaxTree.Parse(text));
        output.WriteLine($"{text.Count(c => c == '\n')} lines parsed in {best:F2} ms");
        Assert.True(best < 20, $"{best:F2} ms");
    }

    [Fact]
    public void ReparsingAfterTypingInAMethodTakesUnder1Millisecond()
    {
        RequireRelease();
        var text = TenThousandLines;
        var tree = JavaSyntaxTree.Parse(text);
        var position = text.IndexOf("return Optional.empty();", text.Length / 2, StringComparison.Ordinal);
        var newText = text.Insert(position, "x");
        TextEdit[] edits = [new TextEdit(position, 0, "x")];
        Assert.NotNull(IncrementalParser.TryReparse(tree, newText, edits));
        var best = BestMilliseconds(200, () => tree.WithChanges(newText, edits));
        output.WriteLine($"reparsed in {best:F3} ms");
        Assert.True(best < 1, $"{best:F3} ms");
    }
}
