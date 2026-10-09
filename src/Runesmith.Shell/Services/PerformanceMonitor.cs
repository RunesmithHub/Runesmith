using System.Collections.Concurrent;
using System.Composition;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using Runesmith.Sdk;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;

namespace Runesmith.Shell.Services;

/// <summary>Collects the latency measurements of the editor, the language analyzers and servers, and highlighting, and reports their percentiles.</summary>
[Export(typeof(ICommandContributor))]
[Export]
[Shared]
public sealed class PerformanceMonitor : ICommandContributor, IDisposable
{
    public const string ReportCommand = "help.performanceReport";
    private const int SamplesKept = 4000;

    private const string ShellMeterName = "Runesmith.Shell";
    private static readonly string[] Meters = ["Runesmith.Editor", "Runesmith.Lsp", "Runesmith.Languages", "Runesmith.LanguageServices", ShellMeterName];
    private static readonly TimeSpan StallProbeInterval = TimeSpan.FromMilliseconds(50);
    private static readonly System.Diagnostics.Metrics.Meter ShellMeter = new(ShellMeterName);

    /// <summary>How late the UI thread ran a timer that should run every 50 ms: the time it was busy with something else.</summary>
    private static readonly Histogram<double> UiStall = ShellMeter.CreateHistogram<double>("runesmith.ui.stall", "ms");

    private readonly Avalonia.Threading.DispatcherTimer stallProbe = new() { Interval = StallProbeInterval };
    private long lastTick = System.Diagnostics.Stopwatch.GetTimestamp();

    private readonly IOutputService output;
    private readonly MeterListener listener = new();
    private readonly ConcurrentDictionary<string, Samples> samples = new(StringComparer.Ordinal);

    [ImportingConstructor]
    public PerformanceMonitor(IOutputService output)
    {
        this.output = output;
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (Meters.Contains(instrument.Meter.Name))
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.Start();
        stallProbe.Tick += (_, _) =>
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var late = System.Diagnostics.Stopwatch.GetElapsedTime(lastTick, now) - StallProbeInterval;
            lastTick = now;
            UiStall.Record(Math.Max(0, late.TotalMilliseconds));
        };
        stallProbe.Start();
    }

    public void Contribute(ICommandRegistry registry) =>
        registry.Add(
            new CommandDefinition(ReportCommand, "Performance Report", "Help")
            {
                Icon = "gauge",
                Description = "Show how long typing, drawing, highlighting and language feature requests take, as percentiles.",
            },
            _ =>
            {
                WriteReport();
                return Task.CompletedTask;
            });

    /// <summary>Writes the report to the Performance output channel and to a file in the logs folder; returns the file's path.</summary>
    public string? WriteReport()
    {
        var report = CreateReport();
        var channel = output.GetChannel("Performance");
        channel.Clear();
        channel.Append(report);
        channel.Show();
        try
        {
            Directory.CreateDirectory(RunesmithPaths.Logs);
            var path = Path.Combine(RunesmithPaths.Logs, $"performance-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt");
            File.WriteAllText(path, report);
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Creates the report: per measurement, the number of samples and the 50th, 95th and 99th percentiles and the maximum.</summary>
    public string CreateReport()
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Runesmith performance at {DateTime.Now:yyyy-MM-dd HH:mm:ss}, the last {SamplesKept} samples of each measurement.");
        text.AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"{"Measurement",-58} {"Count",7} {"p50",9} {"p95",9} {"p99",9} {"Max",9}");
        foreach (var (name, values) in samples.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var sorted = values.Snapshot();
            if (sorted.Length == 0)
                continue;

            text.AppendLine(CultureInfo.InvariantCulture,
                $"{name,-58} {sorted.Length,7} {Percentile(sorted, 0.50),9:0.0} {Percentile(sorted, 0.95),9:0.0} {Percentile(sorted, 0.99),9:0.0} {sorted[^1],9:0.0}");
        }

        return text.ToString();
    }

    public void Dispose()
    {
        stallProbe.Stop();
        listener.Dispose();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var name = instrument.Unit is { Length: > 0 } unit ? $"{instrument.Name} ({unit})" : instrument.Name;
        foreach (var tag in tags)
            name += $" {tag.Key}={tag.Value}";
        samples.GetOrAdd(name, _ => new Samples()).Add(value);
    }

    private static double Percentile(double[] sorted, double percentile) =>
        sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];

    private sealed class Samples
    {
        private readonly double[] values = new double[SamplesKept];
        private int count;

        public void Add(double value)
        {
            lock (values)
                values[count++ % SamplesKept] = value;
        }

        public double[] Snapshot()
        {
            double[] copy;
            lock (values)
                copy = values[..Math.Min(count, SamplesKept)];
            Array.Sort(copy);
            return copy;
        }
    }
}
