using System.Diagnostics.Metrics;
using System.Globalization;

namespace Runesmith.TypingBenchmark;

/// <summary>Collects every measurement Runesmith's meters record while it is started, by instrument and tags.</summary>
internal sealed class MetricRecorder : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly Dictionary<string, List<double>> samples = [];
    private readonly List<(string Instrument, string Request, TaskCompletionSource Signal)> waits = [];
    private readonly Lock gate = new();
    private bool recording;

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

    /// <summary>Completes when the instrument records a measurement whose <c>request</c> tag is <paramref name="request"/>.</summary>
    public Task WaitForAsync(string instrument, string request)
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
            waits.Add((instrument, request, signal));
        return signal.Task;
    }

    public void Start()
    {
        lock (gate)
        {
            samples.Clear();
            recording = true;
        }
    }

    public void Stop()
    {
        lock (gate)
            recording = false;
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
        string? request = null;
        foreach (var tag in tags)
        {
            name += $" {tag.Key}={tag.Value}";
            if (tag.Key == "request")
                request = tag.Value as string;
        }

        lock (gate)
        {
            for (var i = waits.Count - 1; i >= 0; i--)
            {
                if (waits[i].Instrument == instrument.Name && waits[i].Request == request)
                {
                    waits[i].Signal.TrySetResult();
                    waits.RemoveAt(i);
                }
            }

            if (!recording)
                return;
            if (!samples.TryGetValue(name, out var list))
                samples[name] = list = [];
            list.Add(value);
        }
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
