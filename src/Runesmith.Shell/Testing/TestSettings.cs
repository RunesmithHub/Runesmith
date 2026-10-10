using System.Composition;
using Runesmith.Sdk.Settings;

namespace Runesmith.Shell.Testing;

/// <summary>The settings of the Test Explorer.</summary>
[Export(typeof(ISettingContributor))]
public sealed class TestSettings : ISettingContributor
{
    /// <summary>Whether saving a file runs its tests, or the last run's tests when it has none.</summary>
    public const string AutoRunOnSave = "testing.autoRunOnSave";

    public IEnumerable<SettingDefinition> Settings { get; } =
    [
        new(AutoRunOnSave, "Run tests on save", "Testing", false)
        {
            Description = "Run a file's tests when you save it. Saving a file without tests runs the last run's tests again.",
        },
    ];
}
