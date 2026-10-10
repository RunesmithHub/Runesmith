using System.Text.Json.Serialization;

namespace Runesmith.Hub.State;

/// <summary>What the hub installed: the plugins and their files, the changes waiting for a restart, and the plugins moved to quarantine.</summary>
public sealed record HubState
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    /// <summary>Gets the installed hub plugins, as they are after any pending change takes effect.</summary>
    public IReadOnlyList<InstalledHubPlugin> Plugins { get; init; } = [];

    /// <summary>Gets the change that takes effect at the next start, or null.</summary>
    public PendingChange? Pending { get; init; }

    /// <summary>Gets the plugins moved to quarantine because the hub blocked them, until the user dismisses them.</summary>
    public IReadOnlyList<QuarantinedPlugin> Quarantined { get; init; } = [];

    /// <summary>Gets an installed plugin by id, or null.</summary>
    public InstalledHubPlugin? Find(string id) => Plugins.FirstOrDefault(plugin => string.Equals(plugin.Id, id, StringComparison.Ordinal));

    /// <summary>Gets whether a plugin's installed version only takes effect at the next start.</summary>
    public bool IsPending(string id) =>
        Pending is { } pending && (pending.Installed.Any(folder => folder.Id == id) || pending.Removed.Contains(id, StringComparer.Ordinal));
}

/// <summary>A hub plugin that is installed.</summary>
/// <param name="Version">The installed version.</param>
/// <param name="PackageSha256">The SHA-256 of the package it was installed from.</param>
/// <param name="Tier">The plugin's tier when it was installed.</param>
/// <param name="Requested">Whether the user asked for it, rather than it coming in as a dependency.</param>
public sealed record InstalledHubPlugin(string Id, string Version, string PackageSha256, string Tier, bool Requested)
{
    /// <summary>Gets whether the plugin may update by itself; the user can turn this off per plugin.</summary>
    public bool AutoUpdate { get; init; } = true;

    public DateTimeOffset InstalledAt { get; init; }

    /// <summary>Gets every file of the installed plugin, to check that none changed since.</summary>
    public IReadOnlyList<InstalledFile> Files { get; init; } = [];
}

/// <summary>One file of an installed plugin.</summary>
/// <param name="Path">The path relative to the plugin's folder, with forward slashes.</param>
/// <param name="Modified">The file's last write time, which lets an unchanged file skip hashing.</param>
public sealed record InstalledFile(string Path, long Length, string Sha256, DateTime Modified);

/// <summary>Installs, updates and removals that were prepared and committed, and take effect at the next start.</summary>
/// <param name="Installed">The plugins whose prepared folders replace the installed ones.</param>
/// <param name="Removed">The plugins whose folders are removed.</param>
public sealed record PendingChange(IReadOnlyList<PendingFolder> Installed, IReadOnlyList<string> Removed);

/// <summary>A prepared plugin folder.</summary>
/// <param name="Transaction">The folder in the staging area that holds it.</param>
public sealed record PendingFolder(string Id, string Version, string Transaction);

/// <summary>A plugin the hub blocked, which Runesmith moved out of the hub's plugins folder.</summary>
/// <param name="Folder">Where its files are now.</param>
/// <param name="Reason">The moderation reason, such as <c>malicious</c>.</param>
public sealed record QuarantinedPlugin(string Id, string Name, string Version, string Folder, DateTimeOffset At, string? Reason, string? Note)
{
    public string? Advisory { get; init; }

    public string? Remediation { get; init; }

    /// <summary>Gets the ids of the installed plugins that need it and are off with it.</summary>
    public IReadOnlyList<string> Dependents { get; init; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HubState))]
internal sealed partial class HubStateJson : JsonSerializerContext;
