namespace Runesmith.LanguageServices;

/// <summary>The lanes a request runs in, from most to least urgent.</summary>
public enum RequestLane
{
    /// <summary>Completion and signature help: the user is typing and waits for them.</summary>
    Interactive,

    /// <summary>Hover and go to definition: the user waits, but not while typing.</summary>
    Navigation,

    /// <summary>Diagnostics, indexing and warming up: nobody waits for them.</summary>
    Background,
}

/// <summary>Runs work in three lanes: interactive work runs at once, navigation work waits for interactive work, and background work runs
/// only while nothing else does, pausing at its next <see cref="YieldAsync"/> when something more urgent arrives.</summary>
internal sealed class RequestScheduler : IDisposable
{
    private readonly Lock gate = new();
    private int interactive;
    private int navigation;
    private TaskCompletionSource interactiveIdle = Completed();
    private TaskCompletionSource allIdle = Completed();
    private CancellationTokenSource preemption = new();

    /// <summary>Gets whether interactive or navigation work is running or waiting.</summary>
    public bool IsBusy
    {
        get
        {
            lock (gate)
                return interactive + navigation > 0;
        }
    }

    public void Dispose()
    {
        lock (gate)
            preemption.Dispose();
    }

    public async Task<T> RunAsync<T>(RequestLane lane, Func<Task<T>> work, CancellationToken cancellationToken)
    {
        switch (lane)
        {
            case RequestLane.Interactive:
                Enter(ref interactive);
                try
                {
                    return await Task.Run(work, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Leave(ref interactive);
                }

            case RequestLane.Navigation:
                Enter(ref navigation);
                try
                {
                    await WaitAsync(() => interactiveIdle.Task, cancellationToken).ConfigureAwait(false);
                    return await Task.Run(work, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Leave(ref navigation);
                }

            default:
                await YieldAsync(cancellationToken).ConfigureAwait(false);
                return await Task.Run(work, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Gets a token that is cancelled when interactive work starts, for background work that cannot pause on its own, such as one
    /// long compiler call; it is then cancelled and runs again later.</summary>
    public CancellationToken PreemptionToken
    {
        get
        {
            lock (gate)
                return preemption.Token;
        }
    }

    /// <summary>Completes at once when no interactive or navigation work runs, and otherwise when it all finished.</summary>
    public ValueTask YieldAsync(CancellationToken cancellationToken)
    {
        Task idle;
        lock (gate)
            idle = allIdle.Task;
        return idle.IsCompleted ? ValueTask.CompletedTask : new ValueTask(idle.WaitAsync(cancellationToken));
    }

    private void Enter(ref int count)
    {
        CancellationTokenSource? preempted = null;
        lock (gate)
        {
            count++;
            if (interactive > 0 && !preemption.IsCancellationRequested)
            {
                preempted = preemption;
                preemption = new CancellationTokenSource();
            }

            if (allIdle.Task.IsCompleted)
                allIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (interactive > 0 && interactiveIdle.Task.IsCompleted)
                interactiveIdle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        if (preempted is not null)
        {
            preempted.Cancel();
            preempted.Dispose();
        }
    }

    private void Leave(ref int count)
    {
        lock (gate)
        {
            count--;
            if (interactive == 0)
                interactiveIdle.TrySetResult();
            if (interactive + navigation == 0)
                allIdle.TrySetResult();
        }
    }

    private async Task WaitAsync(Func<Task> idle, CancellationToken cancellationToken)
    {
        Task task;
        lock (gate)
            task = idle();
        await task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TaskCompletionSource Completed()
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult();
        return source;
    }
}
