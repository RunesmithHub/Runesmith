using Avalonia.Controls;

namespace Runesmith.Sdk.Settings;

/// <summary>Puts a section of a plugin's own, such as an account, at the top of a settings category, above the category's settings. Export it
/// with <c>[Export(typeof(ISettingsSectionProvider))]</c>.</summary>
public interface ISettingsSectionProvider
{
    /// <summary>Gets the category the section belongs to, such as "GitHub"; the settings page lists it even when it has no settings.</summary>
    string Category { get; }

    /// <summary>Creates the section; called on the UI thread each time the category shows, and when a search matches the category's name.</summary>
    Control CreateSection();
}
