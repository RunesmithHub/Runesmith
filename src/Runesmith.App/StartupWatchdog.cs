using System.Globalization;

namespace Runesmith.App;

/// <summary>Reports a start whose window has not shown in time: where it is stuck, and with which plugin, so a hang leaves a trace even
/// when nothing crashes. It never ends the start.</summary>
internal sealed class StartupWatchdog : IDisposable
{
    private readonly ITimer? timer;
    private readonly StartupGuard guard;
    private readonly TimeSpan limit;
    private readonly Action<string> report;

    /// <param name="limit">How long the window may take to show; zero or less turns the watchdog off.</param>
    /// <param name="report">Called once, on a thread-pool thread, with the report.</param>
    public StartupWatchdog(StartupGuard guard, TimeSpan limit, TimeProvider time, Action<string> report)
    {
        ArgumentNullException.ThrowIfNull(time);
        this.guard = guard;
        this.limit = limit;
        this.report = report;
        if (limit > TimeSpan.Zero)
            timer = time.CreateTimer(_ => Fire(), null, limit, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Stops the watchdog once the window shows.</summary>
    public void Dispose() => timer?.Dispose();

    /// <summary>Describes where a start is stuck.</summary>
    internal static string Describe(StartRecord progress, TimeSpan limit)
    {
        var duration = limit.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + (limit == TimeSpan.FromSeconds(1) ? " second" : " seconds");
        var step = progress.Step switch
        {
            StartupStep.PreparingPlugins => "preparing the plugins from the hub",
            StartupStep.LoadingPlugins => "loading the plugins",
            StartupStep.CreatingWindow => "creating the main window",
            _ => "starting",
        };
        var plugin = progress.Plugin is { } current ? $", at {current.Name} {current.Version} ({current.Id})" : "";
        return $"Runesmith has not shown its window after {duration}. It is {step}{plugin}. "
            + "If it does not show, close Runesmith; the next start can offer safe mode.";
    }

    private void Fire()
    {
        if (guard.Progress().ShownAt is not null)
            return;

        guard.Stuck();
        report(Describe(guard.Progress(), limit));
    }
}
