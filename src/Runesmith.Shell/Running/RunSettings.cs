using System.Composition;
using Runesmith.Sdk.Settings;

namespace Runesmith.Shell.Running;

/// <summary>The settings of running configurations.</summary>
[Export(typeof(ISettingContributor))]
public sealed class RunSettings : ISettingContributor
{
    /// <summary>Whether a configuration's program runs in a tab of the Terminal panel instead of the Run panel's console.</summary>
    public const string UseTerminal = "run.useTerminal";

    public IEnumerable<SettingDefinition> Settings =>
    [
        new(UseTerminal, Strings.SettingsRunInTheTerminal, "Run", false)
        {
            Description = Strings.SettingsRunInTheTerminalDescription,
        },
    ];
}
