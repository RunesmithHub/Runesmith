using Runesmith.Shell.Services;

namespace Runesmith.Shell.Tests.Services;

public sealed class BackgroundTaskServiceTests
{
    [Fact]
    public void ShowsATaskFromItsStartUntilItIsDisposed()
    {
        var service = new BackgroundTaskService();
        var changes = 0;
        service.Changed += (_, _) => changes++;

        var first = service.Start("Loading C# projects");
        var second = service.Start("Building");
        Assert.Equal([first, second], service.Running);
        Assert.Same(second, service.Latest);

        second.Dispose();
        second.Dispose();
        Assert.Same(first, service.Latest);

        first.Dispose();
        Assert.Empty(service.Running);
        Assert.Null(service.Latest);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void KeepsTheLastReportWithItsFractionWithinZeroAndOne()
    {
        var service = new BackgroundTaskService();
        var changes = 0;
        service.Changed += (_, _) => changes++;
        using var task = service.Start("Loading Java projects");

        task.Report("12 of 31", 12 / 31.0);
        Assert.Equal("12 of 31", task.Detail);
        Assert.Equal(12 / 31.0, task.Fraction!.Value, 6);

        task.Report(null, 1.5);
        Assert.Null(task.Detail);
        Assert.Equal(1, task.Fraction);

        task.Report("Indexing");
        Assert.Null(task.Fraction);
        Assert.Equal(4, changes);
    }

    [Fact]
    public void CancelsOnlyTasksStartedWithAWayToCancel()
    {
        var service = new BackgroundTaskService();
        var cancelled = 0;
        using var build = (BackgroundTaskService.BackgroundTask)service.Start("Building", () => cancelled++);
        using var load = (BackgroundTaskService.BackgroundTask)service.Start("Loading");

        Assert.True(build.CanCancel);
        Assert.False(load.CanCancel);
        build.Cancel();
        load.Cancel();

        Assert.Equal(1, cancelled);
        Assert.Equal(2, service.Running.Count);
    }

    [Fact]
    public void RejectsATaskWithoutATitle() => Assert.Throws<ArgumentException>(() => new BackgroundTaskService().Start(" "));
}
