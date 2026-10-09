using System.Composition;
using Runesmith.Sdk.Sdks;
using Runesmith.Sdk.Settings;

namespace Runesmith.Shell.Services;

/// <summary>The default SDK of each kind the providers know; the SDKs settings page edits them.</summary>
[Export(typeof(ISettingContributor))]
[method: ImportingConstructor]
public sealed class SdkSettings([ImportMany] IEnumerable<ISdkProvider> providers) : ISettingContributor
{
    /// <summary>The settings category the SDKs page shows.</summary>
    public const string Category = "SDKs";

    public IEnumerable<SettingDefinition> Settings { get; } =
    [
        .. providers.DistinctBy(p => p.Kind, StringComparer.Ordinal).Select(p => new SettingDefinition(SdkService.DefaultKey(p.Kind), $"Default {p.Name}", Category, "")
        {
            Description = $"The {p.Name} builds and new projects use, as its version and folder separated by |; empty for the newest installed.",
        }),
    ];
}
