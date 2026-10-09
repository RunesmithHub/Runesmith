using Avalonia;
using Avalonia.Threading;

namespace Runesmith.Workspace;

internal static class UiThread
{
    /// <summary>Runs the action now when called on the UI thread or when no application runs a UI thread, such as in tests, and otherwise
    /// queues it there.</summary>
    public static void Run(Action action)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
