using System.Composition;
using Runesmith.Sdk.Settings;

namespace Runesmith.Shell.Running;

/// <summary>The settings of running configurations.</summary>
[Export(typeof(ISettingContributor))]
public sealed class RunSettings : ISettingContributor
{
    /// <summary>Whether a configuration's program runs in a tab of the Terminal panel instead of the Run panel's console.</summary>
    public const string UseTerminal = "run.useTerminal";

    public IEnumerable<SettingDefinition> Settings { get; } =
    [
        new(UseTerminal, "Run in the terminal", "Run", false)
        {
            Description = "Run a configuration's program in a tab of the Terminal panel, where it can use colors, the cursor and full-screen input, instead of the Run panel's console. Debugging still uses the Run panel.",
        },
    ];
}
