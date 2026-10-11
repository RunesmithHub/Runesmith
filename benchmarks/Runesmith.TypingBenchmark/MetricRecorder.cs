using System.Diagnostics.Metrics;
using System.Globalization;

namespace Runesmith.TypingBenchmark;

/// <summary>Collects every measurement Runesmith's meters record while a phase of the benchmark runs, by phase, instrument and tags, and the
/// benchmark's own measurements.</summary>
internal sealed class MetricRecorder : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly Dictionary<string, List<double>> samples = [];
    private readonly Lock gate = new();
    private string? phase;

    public MetricRecorder()
    {
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name.StartsWith("Runesmith.", StringComparison.Ordinal))
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        listener.Start();
    }

    /// <summary>Starts recording a phase, such as typing; its measurements are named after it.</summary>
    public void Start(string name)
    {
        lock (gate)
            phase = name;
    }

    public void Stop()
    {
        lock (gate)
            phase = null;
    }

    /// <summary>Adds a measurement of the benchmark's own to the phase that is recording.</summary>
    public void Record(string name, double value)
    {
        lock (gate)
            Add(name, value);
    }

    public IReadOnlyList<MetricSummary> Summarize()
    {
        lock (gate)
            return [.. samples.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => MetricSummary.Of(pair.Key, pair.Value))];
    }

    public void Dispose() => listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var name = instrument.Unit is { Length: > 0 } unit ? $"{instrument.Name} ({unit})" : instrument.Name;
        foreach (var tag in tags)
            name += $" {tag.Key}={tag.Value}";

        lock (gate)
            Add(name, value);
    }

    private void Add(string name, double value)
    {
        if (phase is null)
            return;

        var key = $"{phase}: {name}";
        if (!samples.TryGetValue(key, out var list))
            samples[key] = list = [];
        list.Add(value);
    }
}

/// <summary>The percentiles of one instrument's measurements over a run.</summary>
internal sealed record MetricSummary(string Name, int Count, double P50, double P95, double P99, double Max)
{
    public static MetricSummary Of(string name, List<double> values)
    {
        var sorted = values.Order().ToArray();
        return new MetricSummary(name, sorted.Length, Percentile(sorted, 0.50), Percentile(sorted, 0.95), Percentile(sorted, 0.99), sorted[^1]);
    }

    /// <summary>Gets a measurement taken once, such as a memory size.</summary>
    public static MetricSummary Single(string name, double value) => new(name, 1, value, value, value, value);

    public string ToLine() => string.Join('\t', Name, Count.ToString(CultureInfo.InvariantCulture), Format(P50), Format(P95), Format(P99), Format(Max));

    public static MetricSummary Parse(string line)
    {
        var parts = line.Split('\t');
        return new MetricSummary(parts[0], int.Parse(parts[1], CultureInfo.InvariantCulture), Number(parts[2]), Number(parts[3]), Number(parts[4]), Number(parts[5]));
    }

    private static double Percentile(double[] sorted, double fraction) => sorted[Math.Max(0, (int)Math.Ceiling(fraction * sorted.Length) - 1)];

    private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static double Number(string text) => double.Parse(text, CultureInfo.InvariantCulture);
}
