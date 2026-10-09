using System.Composition;
using Runesmith.Sdk.Tasks;

namespace Runesmith.Shell.Services;

/// <summary>Keeps the long tasks that run now, oldest first, for the status bar's task indicator.</summary>
[Export(typeof(IBackgroundTasks))]
[Export]
[Shared]
public sealed class BackgroundTaskService : IBackgroundTasks
{
    private readonly List<BackgroundTask> running = [];

    public IReadOnlyList<IBackgroundTask> Running
    {
        get
        {
            lock (running)
                return [.. running];
        }
    }

    /// <summary>Gets the task started last that still runs, or null.</summary>
    public BackgroundTask? Latest
    {
        get
        {
            lock (running)
                return running.Count > 0 ? running[^1] : null;
        }
    }

    public event EventHandler? Changed;

    public IBackgroundTask Start(string title, Action? cancel = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var task = new BackgroundTask(this, title, cancel);
        lock (running)
            running.Add(task);
        Changed?.Invoke(this, EventArgs.Empty);
        return task;
    }

    private void End(BackgroundTask task)
    {
        bool removed;
        lock (running)
            removed = running.Remove(task);
        if (removed)
            Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Reported() => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>A task the indicator shows, and cancels when it was started with a way to.</summary>
    public sealed class BackgroundTask : IBackgroundTask
    {
        private readonly BackgroundTaskService owner;
        private readonly Action? cancel;
        private readonly Lock gate = new();
        private volatile string? detail;
        private double? fraction;

        internal BackgroundTask(BackgroundTaskService owner, string title, Action? cancel)
        {
            this.owner = owner;
            this.cancel = cancel;
            Title = title;
        }

        public string Title { get; }

        public string? Detail => detail;

        public double? Fraction
        {
            get
            {
                lock (gate)
                    return fraction;
            }
        }

        /// <summary>Gets whether the task can be cancelled from the indicator.</summary>
        public bool CanCancel => cancel is not null;

        public void Report(string? detail, double? fraction = null)
        {
            this.detail = detail;
            lock (gate)
                this.fraction = fraction is { } value ? Math.Clamp(value, 0, 1) : null;
            owner.Reported();
        }

        /// <summary>Asks the task to stop; it shows until its owner disposes it.</summary>
        public void Cancel() => cancel?.Invoke();

        public void Dispose() => owner.End(this);
    }
}
