using System.Composition;
using System.Globalization;
using Runesmith.Localization;
using Runesmith.Sdk.Settings;

namespace Runesmith.Shell.Localization;

/// <summary>The settings of the language Runesmith shows and of how partly translated plugins show.</summary>
[Export(typeof(ISettingContributor))]
public sealed class LocalizationSettings : ISettingContributor
{
    /// <summary>The settings page category of the settings about translations; <see cref="CategoryTitle"/> is its name.</summary>
    public const string Category = "Language";

    /// <summary>The value of <see cref="SettingKeys.UntranslatedPlugins"/> that shows each translated message translated.</summary>
    public const string Mix = "mix";

    /// <summary>The value of <see cref="SettingKeys.UntranslatedPlugins"/> that shows a plugin below the minimum entirely in English.</summary>
    public const string English = "english";

    private const string Appearance = "Appearance";

    /// <summary>Gets the name of <see cref="Category"/> in the language Runesmith shows.</summary>
    public static string CategoryTitle => Strings.LocalizationCategory;

    public IEnumerable<SettingDefinition> Settings =>
    [
        new(SettingKeys.Language, Strings.LocalizationLanguageTitle, Appearance, LocalizationOptions.SystemLanguage) { Description = Strings.LocalizationLanguageDescription },
        new(SettingKeys.UntranslatedPlugins, Strings.LocalizationUntranslatedTitle, Category, Mix)
        {
            Description = Strings.LocalizationUntranslatedDescription,
            Choices = [Mix, English],
            ChoiceTitles = [Strings.LocalizationUntranslatedMix, Strings.LocalizationUntranslatedEnglish],
        },
        new(SettingKeys.MinimumCoverage, Strings.LocalizationMinimumCoverageTitle, Category, 80)
        {
            Description = Strings.LocalizationMinimumCoverageDescription,
            Minimum = 0,
            Maximum = 100,
        },
        new(SettingKeys.PseudoLocalization, Strings.LocalizationPseudoTitle, Category, false) { Description = Strings.LocalizationPseudoDescription },
    ];

    /// <summary>Reads the settings localization follows.</summary>
    /// <param name="system">The system's culture, which the language <c>system</c> follows.</param>
    public static LocalizationOptions Read(ISettingsService settings, CultureInfo system)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new LocalizationOptions
        {
            Language = settings.Get<string>(SettingKeys.Language) is { Length: > 0 } language ? language.Trim() : LocalizationOptions.SystemLanguage,
            SystemCulture = system,
            UntranslatedPlugins = string.Equals(settings.Get<string>(SettingKeys.UntranslatedPlugins), English, StringComparison.OrdinalIgnoreCase)
                ? UntranslatedPlugins.English
                : UntranslatedPlugins.Mix,
            MinimumCoverage = settings.Get<int>(SettingKeys.MinimumCoverage),
            IsPseudo = settings.Get<bool>(SettingKeys.PseudoLocalization),
        };
    }
}
