using System.Composition;
using Runesmith.Composition;

namespace Runesmith.Shell.Services;

/// <summary>What happened while Runesmith started: the plugins found and how long each step took.</summary>
[Export]
[Shared]
public sealed class RuntimeInfo
{
    public IReadOnlyList<PluginInfo> Plugins { get; set; } = [];

    /// <summary>Gets or sets the problems composition found in parts.</summary>
    public IReadOnlyList<string> CompositionErrors { get; set; } = [];

    /// <summary>Gets or sets whether Runesmith runs with <c>--diagnostics</c>, which logs timings to the Diagnostics output channel.</summary>
    public bool IsDiagnosticsEnabled { get; set; }

    /// <summary>Gets the startup steps and how long each took, in order.</summary>
    public List<(string Step, TimeSpan Duration)> Timings { get; } = [];

    /// <summary>Gets or sets whether Runesmith runs in safe mode, which loads no plugins.</summary>
    public bool IsSafeMode { get; set; }

    /// <summary>Gets or sets what the plugin hub decided before plugins loaded.</summary>
    public Runesmith.Hub.Enforcement.HubStartupReport HubStartup { get; set; } = Runesmith.Hub.Enforcement.HubStartupReport.Empty;
}
