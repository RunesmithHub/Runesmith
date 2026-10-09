using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;

namespace Runesmith.Shell.Running;

/// <summary>The run configurations of the open folder: the ones the configuration types detect, merged with the ones the user saved, and the
/// one selected in the run widget.</summary>
/// <remarks><see cref="Changed"/> is raised on the thread that caused the change: the UI thread when the user or a folder change caused it.</remarks>
public interface IRunConfigurationService
{
    /// <summary>Gets the configuration types plugins export, in the order the Add menu lists them.</summary>
    IReadOnlyList<IRunConfigurationType> Types { get; }

    /// <summary>Gets the configurations of the open folder.</summary>
    IReadOnlyList<RunConfiguration> Configurations { get; }

    /// <summary>Gets the selected configuration, or null when the folder has none.</summary>
    RunConfiguration? Selected { get; }

    /// <summary>Gets the names of the configurations run last, most recent first.</summary>
    IReadOnlyList<string> Recent { get; }

    /// <summary>Gets whether the types are still looking for configurations in the folder.</summary>
    bool IsDetecting { get; }

    /// <summary>Gets the settings of each configuration type for the open folder, by type id; a type that fails has none.</summary>
    Task<IReadOnlyDictionary<string, OptionSet>> GetOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds a configuration type by id.</summary>
    IRunConfigurationType? FindType(string typeId);

    /// <summary>Finds a configuration by name.</summary>
    RunConfiguration? Find(string name);

    /// <summary>Selects a configuration for the run widget and remembers it for the folder.</summary>
    void Select(RunConfiguration configuration);

    /// <summary>Remembers that a configuration ran, so the run widget lists it first.</summary>
    void MarkRun(RunConfiguration configuration);

    /// <summary>Replaces the folder's configurations, such as after the configurations dialog, and saves the ones that are not detected.</summary>
    /// <param name="selected">The name of the configuration to select, or null to keep the selection when it still exists.</param>
    void Save(IReadOnlyList<RunConfiguration> configurations, string? selected = null);

    /// <summary>Raised when the configurations, the selection or <see cref="IsDetecting"/> change.</summary>
    event EventHandler? Changed;
}
