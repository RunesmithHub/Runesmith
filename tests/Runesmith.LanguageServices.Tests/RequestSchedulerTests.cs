namespace Runesmith.LanguageServices.Tests;

public sealed class RequestSchedulerTests
{
    [Fact]
    public async Task BackgroundWorkWaitsAtItsYieldWhileInteractiveWorkRuns()
    {
        using var scheduler = new RequestScheduler();
        var release = new TaskCompletionSource();
        var interactive = scheduler.RunAsync(RequestLane.Interactive, async () =>
        {
            await release.Task;
            return 1;
        }, TestContext.Current.CancellationToken);

        var yielded = scheduler.YieldAsync(TestContext.Current.CancellationToken).AsTask();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(yielded.IsCompleted);
        Assert.True(scheduler.IsBusy);

        release.SetResult();
        await interactive;
        await yielded.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(scheduler.IsBusy);
    }

    [Fact]
    public async Task NavigationWaitsForInteractiveWork()
    {
        using var scheduler = new RequestScheduler();
        var order = new List<string>();
        var release = new TaskCompletionSource();
        var interactive = scheduler.RunAsync(RequestLane.Interactive, async () =>
        {
            await release.Task;
            lock (order)
                order.Add("interactive");
            return 0;
        }, TestContext.Current.CancellationToken);
        var navigation = scheduler.RunAsync(RequestLane.Navigation, () =>
        {
            lock (order)
                order.Add("navigation");
            return Task.FromResult(0);
        }, TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Empty(order);
        release.SetResult();
        await Task.WhenAll(interactive, navigation);

        Assert.Equal(["interactive", "navigation"], order);
    }

    [Fact]
    public async Task YieldIsFreeWhenIdle()
    {
        using var scheduler = new RequestScheduler();
        var yielded = scheduler.YieldAsync(TestContext.Current.CancellationToken).AsTask();
        Assert.True(yielded.IsCompleted);
        Assert.Equal(7, await scheduler.RunAsync(RequestLane.Background, () => Task.FromResult(7), TestContext.Current.CancellationToken));
    }
}

public sealed class PreemptionTests
{
    [Fact]
    public async Task InteractiveWorkCancelsThePreemptionTokenAndRenewsIt()
    {
        using var scheduler = new RequestScheduler();
        var before = scheduler.PreemptionToken;
        Assert.False(before.IsCancellationRequested);

        await scheduler.RunAsync(RequestLane.Interactive, () => Task.FromResult(0), TestContext.Current.CancellationToken);

        Assert.True(before.IsCancellationRequested);
        Assert.False(scheduler.PreemptionToken.IsCancellationRequested);
    }

    [Fact]
    public async Task NavigationWorkDoesNotPreempt()
    {
        using var scheduler = new RequestScheduler();
        var before = scheduler.PreemptionToken;
        await scheduler.RunAsync(RequestLane.Navigation, () => Task.FromResult(0), TestContext.Current.CancellationToken);
        Assert.False(before.IsCancellationRequested);
    }
}
