namespace Runesmith.App.Tests;

public sealed class StartupWatchdogTests : IDisposable
{
    private static readonly SuspectPlugin Lumen = new("lumen.todo", "Lumen Todo", "1.0.0");

    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-watchdog-").FullName;
    private readonly ManualTimers time = new();
    private readonly List<string> reports = [];

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void AStartWhoseWindowHasNotShownIsReportedOnceWithWhereItIs()
    {
        var guard = Begin();
        using var watchdog = new StartupWatchdog(guard, TimeSpan.FromSeconds(30), time, reports.Add);
        guard.Reached(StartupStep.LoadingPlugins);
        guard.Starting(Lumen);

        time.Fire();

        var report = Assert.Single(reports);
        Assert.Equal("Runesmith has not shown its window after 30 seconds. It is loading the plugins, at Lumen Todo 1.0.0 (lumen.todo). "
            + "If it does not show, close Runesmith; the next start can offer safe mode.", report);
        Assert.NotNull(guard.Progress().StuckAt);
        Assert.Equal(TimeSpan.FromSeconds(30), time.DueTime);
    }

    [Fact]
    public void AStartWhoseWindowShowedIsNotReported()
    {
        var guard = Begin();
        using var watchdog = new StartupWatchdog(guard, TimeSpan.FromSeconds(30), time, reports.Add);
        guard.Shown();

        time.Fire();

        Assert.Empty(reports);
        Assert.Null(guard.Progress().StuckAt);
    }

    [Fact]
    public void ALimitOfZeroTurnsTheWatchdogOff()
    {
        using var watchdog = new StartupWatchdog(Begin(), TimeSpan.Zero, time, reports.Add);

        Assert.False(time.Created);
    }

    [Fact]
    public void ShowingTheWindowStopsTheTimer()
    {
        var watchdog = new StartupWatchdog(Begin(), TimeSpan.FromSeconds(5), time, reports.Add);

        watchdog.Dispose();

        Assert.True(time.Disposed);
    }

    [Fact]
    public void TheReportCountsOneSecondAsOne() =>
        Assert.StartsWith("Runesmith has not shown its window after 1 second. ", StartupWatchdog.Describe(new StartRecord(), TimeSpan.FromSeconds(1)), StringComparison.Ordinal);

    [Theory]
    [InlineData(null, "starting")]
    [InlineData(nameof(StartupStep.PreparingPlugins), "preparing the plugins from the hub")]
    [InlineData(nameof(StartupStep.CreatingWindow), "creating the main window")]
    public void TheReportSaysWhichStepTheStartIsAt(string? step, string described)
    {
        var progress = new StartRecord { Step = step is null ? null : Enum.Parse<StartupStep>(step) };

        Assert.Contains($"It is {described}.", StartupWatchdog.Describe(progress, TimeSpan.FromSeconds(10)), StringComparison.Ordinal);
    }

    private StartupGuard Begin() => StartupGuard.Begin(Path.Combine(folder, "startups.json"), time, safeMode: false);

    private sealed class ManualTimers : TimeProvider
    {
        private TimerCallback? callback;

        public bool Created => callback is not null;

        public bool Disposed { get; private set; }

        public TimeSpan DueTime { get; private set; }

        public void Fire() => callback?.Invoke(null);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            DueTime = dueTime;
            return new Timer(this);
        }

        private sealed class Timer(ManualTimers owner) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() => owner.Disposed = true;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
