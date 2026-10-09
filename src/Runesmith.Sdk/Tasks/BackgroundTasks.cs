namespace Runesmith.Sdk.Tasks;

/// <summary>A long task shown in the status bar's task indicator until it is disposed.</summary>
public interface IBackgroundTask : IDisposable
{
    /// <summary>Gets what the task does, such as "Loading C# projects".</summary>
    string Title { get; }

    /// <summary>Gets the current step, such as "12 of 31", or null.</summary>
    string? Detail { get; }

    /// <summary>Gets how much is done, from 0 to 1, or null when unknown.</summary>
    double? Fraction { get; }

    /// <summary>Reports progress; any thread may call it.</summary>
    void Report(string? detail, double? fraction = null);
}

/// <summary>Shows long tasks, such as loading projects or installing an SDK, in the status bar.</summary>
public interface IBackgroundTasks
{
    /// <summary>Gets the tasks that run now.</summary>
    IReadOnlyList<IBackgroundTask> Running { get; }

    /// <summary>Starts showing a task; disposing the result ends it.</summary>
    /// <param name="title">What the task does.</param>
    /// <param name="cancel">Cancels the task from the indicator's popup, or null when it cannot be cancelled.</param>
    IBackgroundTask Start(string title, Action? cancel = null);

    /// <summary>Raised when a task starts, reports or ends; any thread may raise it.</summary>
    event EventHandler? Changed;
}
