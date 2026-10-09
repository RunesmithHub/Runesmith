using System.Diagnostics;

namespace Runesmith.TypingBenchmark;

/// <summary>The process's garbage collection and processor counters at one moment; subtracting two gives what happened between them.</summary>
internal readonly record struct ProcessSnapshot(int Gen0, int Gen1, int Gen2, double PauseMs, double AllocatedMb, double CpuMs, long Timestamp)
{
    public static ProcessSnapshot Take() => new(
        GC.CollectionCount(0),
        GC.CollectionCount(1),
        GC.CollectionCount(2),
        GC.GetTotalPauseDuration().TotalMilliseconds,
        GC.GetTotalAllocatedBytes() / 1048576.0,
        Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds,
        Stopwatch.GetTimestamp());

    public static ProcessSnapshot operator -(ProcessSnapshot after, ProcessSnapshot before) => new(
        after.Gen0 - before.Gen0,
        after.Gen1 - before.Gen1,
        after.Gen2 - before.Gen2,
        after.PauseMs - before.PauseMs,
        after.AllocatedMb - before.AllocatedMb,
        after.CpuMs - before.CpuMs,
        after.Timestamp - before.Timestamp);

    /// <summary>The counters as single-value summaries, so runs can be compared like the meters.</summary>
    public IEnumerable<MetricSummary> ToSummaries()
    {
        var wallMs = Timestamp * 1000.0 / Stopwatch.Frequency;
        (string Name, double Value)[] values =
        [
            ("process.gc.gen0", Gen0), ("process.gc.gen1", Gen1), ("process.gc.gen2", Gen2), ("process.gc.pause (ms)", PauseMs),
            ("process.allocated (MB)", AllocatedMb), ("process.cpu (ms)", CpuMs), ("process.wall (ms)", wallMs),
            ("process.cpu_cores_busy", CpuMs / wallMs),
        ];
        return values.Select(value => new MetricSummary(value.Name, 1, value.Value, value.Value, value.Value, value.Value));
    }
}
