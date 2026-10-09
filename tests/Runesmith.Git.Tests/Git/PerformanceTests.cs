using System.Diagnostics;
using System.Globalization;
using System.Text;
using Runesmith.Git.Git;
using Runesmith.Git.Views.History;

namespace Runesmith.Git.Tests.Git;

/// <summary>Runs the measurements alone, so other tests do not compete with them for the processor.</summary>
[CollectionDefinition(nameof(PerformanceTests), DisableParallelization = true)]
public sealed class MeasuredAlone;

[Collection(nameof(PerformanceTests))]
public sealed class PerformanceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadsTheStatusOfTenThousandFilesInUnder300Milliseconds()
    {
        using var temp = await TempRepository.CreateAsync();
        for (var i = 0; i < 10_000; i++)
            temp.Write($"folder{i / 100}/file{i}.txt", $"line {i}\n");
        await temp.GitAsync("add", "--all");
        await temp.GitAsync("commit", "--quiet", "-m", "ten thousand files");
        for (var i = 0; i < 10_000; i += 100)
        {
            temp.Write($"folder{i / 100}/file{i}.txt", "changed\n");
            temp.Write($"folder{i / 100}/new{i}.txt", "new\n");
        }

        await temp.Repository.GetStatusAsync(Token);
        var times = new List<double>();
        StatusSnapshot? status = null;
        for (var run = 0; run < 5; run++)
        {
            var watch = Stopwatch.StartNew();
            status = await temp.Repository.GetStatusAsync(Token);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        var median = times.Order().ElementAt(times.Count / 2);
        TestContext.Current.TestOutputHelper?.WriteLine($"Status of 10,000 files: {median:F1} ms (median of 5)");
        Assert.Equal(200, status!.ChangedCount);
        Assert.True(median < 300, $"Status took {median:F1} ms.");
    }

    [Fact]
    public async Task ReadsAndLaysOutTheFirstPageOfTwentyThousandCommitsInUnder100Milliseconds()
    {
        using var temp = await TempRepository.CreateAsync();
        await GitProcess.RunCheckedAsync(temp.Path, ["fast-import", "--quiet"], Token, input: History(20_000));
        await temp.GitAsync("reset", "--quiet", "--hard", "main");

        var query = new LogQuery { Count = 200 };
        var git = Stopwatch.StartNew();
        var output = await temp.Repository.ReadAsync(LogParser.Arguments(query), Token);
        var gitTime = git.Elapsed.TotalMilliseconds;

        var times = new List<double>();
        IReadOnlyList<GraphRow> rows = [];
        for (var run = 0; run < 5; run++)
        {
            var watch = Stopwatch.StartNew();
            var layout = new GraphLayout();
            rows = [.. LogParser.Parse(output).Select(layout.Add)];
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        var median = times.Order().ElementAt(times.Count / 2);
        TestContext.Current.TestOutputHelper?.WriteLine($"First page of 200 of 20,000 commits: git {gitTime:F1} ms, parsing and graph {median:F2} ms (median of 5)");
        Assert.Equal(200, rows.Count);
        Assert.Contains(rows, r => r.Width > 1);
        Assert.True(median < 100, $"Parsing and laying out took {median:F1} ms.");
    }

    // A main line with a side branch merged back every 20 commits.
    private static string History(int count)
    {
        var builder = new StringBuilder();
        var time = 1_600_000_000L;
        string Commit(string branch, int mark, string message, string? from, string? merge)
        {
            builder.Append(CultureInfo.InvariantCulture, $"commit refs/heads/{branch}\nmark :{mark}\ncommitter Ada Lovelace <ada@example.com> {time++} +0000\n");
            builder.Append(CultureInfo.InvariantCulture, $"data {Encoding.UTF8.GetByteCount(message)}\n{message}\n");
            if (from is not null)
                builder.Append(CultureInfo.InvariantCulture, $"from {from}\n");
            if (merge is not null)
                builder.Append(CultureInfo.InvariantCulture, $"merge {merge}\n");
            builder.Append(CultureInfo.InvariantCulture, $"M 644 inline file{mark % 50}.txt\ndata <<END\n{mark}\nEND\n\n");
            return $":{mark}";
        }

        var mark = 1;
        var main = Commit("main", mark++, "First commit", null, null);
        while (mark < count)
        {
            if (mark % 20 == 0)
            {
                var side = Commit("side", mark++, $"Side work {mark}", main, null);
                main = Commit("main", mark++, $"Main work {mark}", main, null);
                main = Commit("main", mark++, $"Merge side {mark}", main, side);
            }
            else
            {
                main = Commit("main", mark++, $"Change {mark}", main, null);
            }
        }

        return builder.ToString();
    }
}
