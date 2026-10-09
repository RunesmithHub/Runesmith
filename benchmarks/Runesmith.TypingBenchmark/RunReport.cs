using System.Globalization;
using System.Text;

namespace Runesmith.TypingBenchmark;

/// <summary>Writes one run's results, and a summary of every run in the folder: the median of each percentile over the runs, and the range
/// of the p95 values, which shows how much the runs disagree.</summary>
internal static class RunReport
{
    private const string RunPattern = "run-*.tsv";

    public static void Write(string folder, IReadOnlyList<MetricSummary> metrics, ProcessSnapshot process)
    {
        Directory.CreateDirectory(folder);
        var lines = metrics.Concat(process.ToSummaries()).Select(metric => metric.ToLine());
        File.WriteAllLines(Path.Combine(folder, $"run-{DateTime.Now:yyyyMMdd-HHmmss-fff}.tsv"), lines);
        WriteSummary(folder);
    }

    private static void WriteSummary(string folder)
    {
        var runs = Directory.GetFiles(folder, RunPattern).Order(StringComparer.Ordinal)
            .Select(file => File.ReadAllLines(file).Select(MetricSummary.Parse).ToDictionary(metric => metric.Name))
            .ToList();
        var names = runs.SelectMany(run => run.Keys).Distinct().Order(StringComparer.Ordinal);

        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Median over {runs.Count} runs.");
        text.AppendLine();
        text.AppendLine("| Measurement | Count | p50 | p95 | p99 | Max | p95 range |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var name in names)
        {
            var values = runs.Where(run => run.ContainsKey(name)).Select(run => run[name]).ToList();
            var p95 = values.Select(value => value.P95).Order().ToList();
            text.AppendLine(CultureInfo.InvariantCulture,
                $"| {name} | {Median(values.Select(v => (double)v.Count)):0} | {Median(values.Select(v => v.P50)):0.0} | {Median(p95):0.0} | {Median(values.Select(v => v.P99)):0.0} | {Median(values.Select(v => v.Max)):0.0} | {p95[0]:0.0} to {p95[^1]:0.0} |");
        }

        File.WriteAllText(Path.Combine(folder, "summary.md"), text.ToString());
    }

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
    }
}
