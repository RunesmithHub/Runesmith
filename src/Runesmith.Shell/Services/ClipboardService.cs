using System.Composition;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using PluginClipboard = Runesmith.Sdk.Shell.IClipboard;

namespace Runesmith.Shell.Services;

/// <summary>Reads and writes the system clipboard through the main window, for plugins.</summary>
[Export(typeof(PluginClipboard))]
[Shared]
[method: ImportingConstructor]
public sealed class ClipboardService(ShellController shell) : PluginClipboard
{
    public Task<string?> GetTextAsync(CancellationToken cancellationToken = default) =>
        OnUiThread(async () => shell.Window?.Clipboard is { } clipboard ? await clipboard.TryGetTextAsync() : null, cancellationToken);

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return OnUiThread(async () =>
        {
            if (shell.Window?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(text);
            return true;
        }, cancellationToken);
    }

    private static Task<T> OnUiThread<T>(Func<Task<T>> work, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Dispatcher.UIThread.CheckAccess() ? work() : Dispatcher.UIThread.InvokeAsync(work);
    }
}
