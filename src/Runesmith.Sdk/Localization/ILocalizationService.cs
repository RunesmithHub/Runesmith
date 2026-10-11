using System.Globalization;

namespace Runesmith.Sdk.Localization;

/// <summary>The language Runesmith shows, the languages it can show, and the localizer of the calling plugin, for code that formats text
/// itself.</summary>
/// <remarks>Added in plugin API 0.1.2. Its members can be called from any thread.</remarks>
public interface ILocalizationService
{
    /// <summary>Gets the language the window shows, such as <c>nl-BE</c>, as a culture; format numbers and dates with it.</summary>
    CultureInfo CurrentLanguage { get; }

    /// <summary>Gets English and the languages the installed language packs add, with how much of Runesmith each translates.</summary>
    IReadOnlyList<LanguageInfo> AvailableLanguages { get; }

    /// <summary>Raised on the UI thread after the language changed, so text shown before should be fetched again.</summary>
    event EventHandler? Changed;

    /// <summary>Gets the localizer of the calling plugin: its messages in the current language, from its own translations, then the
    /// language packs, then its English source.</summary>
    ILocalizer GetLocalizer();
}
