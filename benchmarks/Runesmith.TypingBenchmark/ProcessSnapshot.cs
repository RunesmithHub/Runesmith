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

    /// <summary>The counters as single-value summaries named after a phase, so runs can be compared like the meters.</summary>
    public IEnumerable<MetricSummary> ToSummaries(string phase)
    {
        var wallMs = Timestamp * 1000.0 / Stopwatch.Frequency;
        (string Name, double Value)[] values =
        [
            ("process.gc.gen0", Gen0), ("process.gc.gen1", Gen1), ("process.gc.gen2", Gen2), ("process.gc.pause (ms)", PauseMs),
            ("process.allocated (MB)", AllocatedMb), ("process.cpu (ms)", CpuMs), ("process.wall (ms)", wallMs),
            ("process.cpu_cores_busy", CpuMs / wallMs),
        ];
        return values.Select(value => MetricSummary.Single($"{phase}: {value.Name}", value.Value));
    }
}

/// <summary>The managed heap and the process's working set after a full collection.</summary>
internal readonly record struct MemorySnapshot(double ManagedMb, double WorkingSetMb)
{
    public static MemorySnapshot Take()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var managed = GC.GetTotalMemory(forceFullCollection: true) / 1048576.0;
        using var process = Process.GetCurrentProcess();
        return new MemorySnapshot(managed, process.WorkingSet64 / 1048576.0);
    }

    /// <summary>What was added to memory since <paramref name="before"/>, as single-value summaries named after a phase.</summary>
    public IEnumerable<MetricSummary> ToSummaries(string phase, MemorySnapshot before) =>
    [
        MetricSummary.Single($"{phase}: memory.managed (MB)", ManagedMb - before.ManagedMb),
        MetricSummary.Single($"{phase}: memory.working_set (MB)", WorkingSetMb - before.WorkingSetMb),
    ];
}
