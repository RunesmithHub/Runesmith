using Microsoft.VisualStudio.Composition;

namespace Runesmith.Composition;

/// <summary>The composed parts and what happened while composing them.</summary>
public sealed class CompositionResult : IDisposable
{
    internal CompositionResult(ExportProvider exports, IReadOnlyList<PluginInfo> plugins, IReadOnlyList<string> errors, bool fromCache, TimeSpan duration)
    {
        Exports = exports;
        Plugins = plugins;
        Errors = errors;
        IsFromCache = fromCache;
        Duration = duration;
    }

    /// <summary>Gets the parts; get services and extensions from it.</summary>
    public ExportProvider Exports { get; }

    /// <summary>Gets every plugin found, including ones that are turned off or failed.</summary>
    public IReadOnlyList<PluginInfo> Plugins { get; }

    /// <summary>Gets the problems found in parts, such as an import nothing exports; the parts concerned are left out.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Gets whether the composition was read from the cache.</summary>
    public bool IsFromCache { get; }

    /// <summary>Gets how long composing took.</summary>
    public TimeSpan Duration { get; }

    public void Dispose() => Exports.Dispose();
}
