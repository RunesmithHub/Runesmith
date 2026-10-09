using Avalonia.Threading;

namespace Runesmith.Workspace;

internal static class UiThread
{
    /// <summary>Runs the action now when called on the UI thread, and otherwise queues it there.</summary>
    public static void Run(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
