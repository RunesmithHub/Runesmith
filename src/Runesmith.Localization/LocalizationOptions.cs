using System.Globalization;

namespace Runesmith.Localization;

/// <summary>How a plugin shows whose translation into the current language is incomplete.</summary>
public enum UntranslatedPlugins
{
    /// <summary>Each translated message shows translated, and the rest in English.</summary>
    Mix,

    /// <summary>A plugin whose translation covers less than <see cref="LocalizationOptions.MinimumCoverage"/> shows entirely in English.</summary>
    English,
}

/// <summary>The settings localization follows.</summary>
public sealed record LocalizationOptions
{
    /// <summary>The value of <see cref="Language"/> that follows the system's language.</summary>
    public const string SystemLanguage = "system";

    /// <summary>Gets the language: <see cref="SystemLanguage"/>, or a BCP 47 tag such as <c>nl</c>.</summary>
    public string Language { get; init; } = SystemLanguage;

    /// <summary>Gets the system's culture, which <see cref="SystemLanguage"/> uses when a language pack covers its language.</summary>
    public CultureInfo SystemCulture { get; init; } = CultureInfo.CurrentUICulture;

    public UntranslatedPlugins UntranslatedPlugins { get; init; } = UntranslatedPlugins.Mix;

    /// <summary>Gets the percentage of a plugin's messages its translation must cover to show with <see cref="UntranslatedPlugins.English"/>.</summary>
    public int MinimumCoverage { get; init; } = 80;

    /// <summary>Gets whether every message shows pseudo-localized: accented, longer and in brackets.</summary>
    public bool IsPseudo { get; init; }

    /// <summary>Gets whether the values in a message, such as a file name, are wrapped in Unicode isolation marks, so right-to-left and
    /// left-to-right text do not mix up; off by default.</summary>
    public bool IsolateArguments { get; init; }
}
