using System.Composition;
using Runesmith.Git.Git;

namespace Runesmith.Git.Views.History;

/// <summary>A request to show the log with a filter, such as one file's history or two branches compared.</summary>
/// <param name="Query">The commits to show.</param>
/// <param name="Title">What the filter shows, such as "History of Program.cs", shown above the log with a button that clears it.</param>
internal sealed record HistoryRequest(LogQuery Query, string Title);

/// <summary>Takes requests to show a filtered log to the Git tool window, which may not have been created yet.</summary>
[Export]
[Shared]
internal sealed class HistoryNavigator
{
    /// <summary>Gets the last request, for a Git tool window created after it was made.</summary>
    public HistoryRequest? Pending { get; private set; }

    /// <summary>Raised on the UI thread when the log should show a filtered history.</summary>
    public event EventHandler<HistoryRequest>? Requested;

    /// <summary>Asks the log to show a filtered history; the caller shows the Git tool window.</summary>
    public void Show(HistoryRequest request)
    {
        Pending = Requested is null ? request : null;
        Requested?.Invoke(this, request);
    }

    /// <summary>Takes the request made before the log existed, if any.</summary>
    public HistoryRequest? TakePending()
    {
        var pending = Pending;
        Pending = null;
        return pending;
    }
}
