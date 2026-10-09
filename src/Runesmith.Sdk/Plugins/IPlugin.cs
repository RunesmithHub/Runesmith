namespace Runesmith.Sdk.Plugins;

/// <summary>A plugin's entry point, for the work it does once when Runesmith starts, such as registering icons or subscribing to messages.</summary>
/// <remarks>
/// Export it with <c>[Export(typeof(IPlugin))]</c>. Runesmith calls <see cref="InitializeAsync"/> once the main window is on screen, so a plugin
/// never delays the window, and disposes the plugin when Runesmith closes. Most plugins need no entry point: their commands, tool windows and
/// language features are exports that Runesmith creates when they are first used.
/// </remarks>
public interface IPlugin : IDisposable
{
    /// <summary>Does the plugin's startup work; runs on the UI thread, and the cancellation token fires when Runesmith closes first.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);
}
