using Avalonia.Threading;

namespace Runesmith.Shell;

/// <summary>Runs work on the UI thread from wherever the shell's services are called.</summary>
internal static class UiThread
{
    /// <summary>Runs an action now when on the UI thread, and otherwise posts it there.</summary>
    public static void Run(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
