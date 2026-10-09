using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Hub.Enforcement;

/// <summary>Why an installed plugin needs the user's attention.</summary>
public enum NoticeKind
{
    /// <summary>The hub blocked it while Runesmith was running; it is off from the next start.</summary>
    Blocked,

    /// <summary>The hub blocked it and Runesmith moved it to quarantine.</summary>
    Quarantined,

    /// <summary>Its files changed since it was installed, so it is off.</summary>
    FilesChanged,

    /// <summary>New installs are paused while Runesmith looks into a report; it keeps running.</summary>
    Frozen,

    /// <summary>Its developer withdrew the installed version; it keeps running.</summary>
    Yanked,

    /// <summary>Its developer no longer maintains it; it keeps running.</summary>
    Deprecated,

    /// <summary>It is no longer on the hub; it keeps running without updates.</summary>
    Deleted,
}

/// <summary>A notice about one installed plugin, for the plugin manager.</summary>
/// <param name="Title">The notice's first sentence.</param>
public sealed record PluginNotice(NoticeKind Kind, string PluginId, string Name, string Version, string Title)
{
    /// <summary>Gets more about the notice, such as the reason a plugin was blocked.</summary>
    public string? Message { get; init; }

    /// <summary>Gets the address of the security advisory, when there is one.</summary>
    public string? Advisory { get; init; }

    /// <summary>Gets a version that fixes the problem, when the hub has one.</summary>
    public SemanticVersion? UpdateTo { get; init; }

    /// <summary>Gets the id of the plugin the developer suggests instead, for a deprecated plugin.</summary>
    public string? Replacement { get; init; }

    /// <summary>Gets the names of the installed plugins that are off with this one.</summary>
    public IReadOnlyList<string> Dependents { get; init; } = [];

    /// <summary>Gets the ids of the installed plugins that are off with this one.</summary>
    public IReadOnlyList<string> DependentIds { get; init; } = [];

    /// <summary>Gets whether the notice is about a plugin that does not run.</summary>
    public bool IsDanger => Kind is NoticeKind.Blocked or NoticeKind.Quarantined or NoticeKind.FilesChanged;
}
