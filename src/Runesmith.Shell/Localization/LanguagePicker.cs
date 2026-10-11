using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Runesmith.Localization;
using Runesmith.Sdk.Localization;

namespace Runesmith.Shell.Localization;

/// <summary>One entry of the language picker: the value the setting stores, and what shows for it in the language that shows.</summary>
internal sealed record LanguageChoice(string Value, Func<string> Title, Func<string>? Detail = null);

/// <summary>The picker of the language Runesmith shows: following the system, English, and the language of each installed language pack with
/// how much of Runesmith it translates, such as "Nederlands (Dutch), 94%".</summary>
internal static class LanguagePicker
{
    /// <summary>Gets the entries: following the system first, with the language that gives, then English and the installed languages.</summary>
    public static List<LanguageChoice> Choices(ShellLocalization localization) =>
    [
        new(LocalizationOptions.SystemLanguage, () => Strings.LocalizationPickerSystem, () => SystemLanguage(localization)),
        .. localization.AvailableLanguages.Select(language => new LanguageChoice(language.Tag, () => Title(language))),
    ];

    /// <summary>Creates a drop-down of the languages showing <paramref name="current"/>, which calls <paramref name="set"/> with the value
    /// of the entry picked. A language whose language pack is gone shows as not installed.</summary>
    public static ComboBox Create(ShellLocalization localization, string current, Action<string> set)
    {
        var choices = Choices(localization);
        var selected = choices.FirstOrDefault(choice => string.Equals(choice.Value, current, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            selected = new LanguageChoice(current, () => current, () => Strings.LocalizationPickerNotInstalled);
            choices.Add(selected);
        }

        var box = new ComboBox
        {
            ItemsSource = choices,
            SelectedItem = selected,
            MinWidth = 160,
            ItemTemplate = new FuncDataTemplate<LanguageChoice?>((choice, _) => choice is null ? new Panel() : Entry(choice)),
        };
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is LanguageChoice choice && choice != selected)
            {
                selected = choice;
                set(choice.Value);
            }
        };
        return box;
    }

    /// <summary>Gets a language as the picker names it: its own name, its English name and how much of Runesmith it translates.</summary>
    internal static string Title(LanguageInfo language)
    {
        if (string.Equals(language.Tag, "en", StringComparison.OrdinalIgnoreCase))
            return language.Name;

        var coverage = TranslationLabels.Share(language.Coverage);
        return string.IsNullOrWhiteSpace(language.EnglishName) || string.Equals(language.Name, language.EnglishName, StringComparison.OrdinalIgnoreCase)
            ? Strings.LocalizationPickerLanguagePlain(language.Name, coverage)
            : Strings.LocalizationPickerLanguage(language.Name, language.EnglishName, coverage);
    }

    /// <summary>Gets the name of the language that following the system shows: the system's when an installed language covers it, and
    /// otherwise English.</summary>
    internal static string SystemLanguage(ShellLocalization localization)
    {
        var languages = localization.AvailableLanguages;
        return LocalizationService.Chain(localization.SystemCulture.Name)
            .Select(tag => languages.FirstOrDefault(language => string.Equals(language.Tag, tag, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(language => language is not null)?.Name ?? languages[0].Name;
    }

    private static DockPanel Entry(LanguageChoice choice)
    {
        var row = new DockPanel();
        var title = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }.Localize(TextBlock.TextProperty, choice.Title);
        if (choice.Detail is { } detail)
        {
            DockPanel.SetDock(title, Dock.Left);
            row.Children.Add(title);
            row.Children.Add(new TextBlock
            {
                Classes = { "caption", "muted" },
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            }.Localize(TextBlock.TextProperty, detail));
        }
        else
        {
            row.Children.Add(title);
        }

        return row;
    }
}
