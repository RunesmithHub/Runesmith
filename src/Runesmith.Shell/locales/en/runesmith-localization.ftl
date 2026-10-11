### The language Runesmith shows: the language picker and the Language settings, the suggestion to install a
### language pack, and what the plugin manager says about translations.

## Settings

# The name of the settings category that holds the settings below.
localization-category = Language

localization-language-title = Language
localization-language-description = The language Runesmith shows. Language packs from the plugin hub add languages, and the change shows at once.

# The first entry of the language picker; the language it gives shows beside it.
localization-picker-system = Follow the system
# A language in the language picker.
# $name (String) - the language's name in that language, such as Nederlands
# $englishName (String) - the language's name in English, such as Dutch
# $coverage (Number) - the share of Runesmith's text the language translates, from 0 to 1
localization-picker-language = { $name } ({ $englishName }), { NUMBER($coverage, style: "percent") }
# A language in the language picker whose own name is its English name.
# $name (String) - the language's name
# $coverage (Number) - the share of Runesmith's text the language translates, from 0 to 1
localization-picker-language-plain = { $name }, { NUMBER($coverage, style: "percent") }
# Shown beside a language that is chosen and has no language pack installed.
localization-picker-not-installed = Not installed

localization-untranslated-title = Partly translated plugins
localization-untranslated-description = What a plugin shows when its translation into your language is incomplete: every translated text translated and the rest in English, or the whole plugin in English when it translates less than the minimum.
# A choice of the setting "Partly translated plugins".
localization-untranslated-mix = Mix both languages
# A choice of the setting "Partly translated plugins".
localization-untranslated-english = Show in English

localization-minimum-coverage-title = Minimum translation
localization-minimum-coverage-description = The percentage of its text a plugin's translation must cover to show translated while partly translated plugins show in English.

localization-pseudo-title = Pseudo-localization
localization-pseudo-description = For developers: shows every translatable text accented, longer and in brackets, so text that can't be translated and layouts that cut text off stand out.

## The line that suggests the official language pack of the system's language

# $name (String) - the language's name in that language, such as Nederlands
# $englishName (String) - the language's name in English, such as Dutch
localization-suggestion = Runesmith is available in { $name }. Install the official { $englishName } language pack to use it.
localization-suggestion-install = Install
localization-suggestion-decline = Don't suggest again
# The tooltip of the button that closes the line.
localization-suggestion-close = Close

## The plugin manager

# How much of a plugin the language Runesmith shows translates.
# $language (String) - the language's name in that language, such as Nederlands
# $coverage (Number) - the share of the plugin's text that is translated, from 0 to 1
localization-coverage = { $language }, { NUMBER($coverage, style: "percent") }
# A plugin from the hub that has a translation whose size is not known until it is installed.
# $language (String) - the language's name in that language, such as Nederlands
localization-coverage-available = { $language }
# A plugin without a translation into the language Runesmith shows.
localization-coverage-english-only = English only
# The tooltip of the three labels above.
localization-coverage-tip = How much of this plugin your language translates

# The language a language pack adds.
# $name (String) - the language's name in that language, such as Nederlands
# $englishName (String) - the language's name in English, such as Dutch
localization-pack-language = { $name } ({ $englishName })
# What a language pack translates, after its language.
# $language (String) - the language, such as Nederlands (Dutch)
# $targets (String) - what it translates, such as Runesmith 94%, Git 100%
localization-pack-summary = { $language }: { $targets }
# The title of the card on a language pack's page that lists what it translates.
localization-pack-title = Translations
# The name of Runesmith itself in the list of what a language pack translates.
localization-pack-runesmith = Runesmith
# One thing a language pack translates, with how much of it.
# $target (String) - Runesmith, or a plugin's name
# $detail (String) - how much of it, such as 94% or 12 messages
localization-pack-target = { $target } { $detail }
# What stands between the things a language pack translates when they are listed on one line.
localization-pack-separator = ,{" "}
# The share of something's text a language pack translates.
# $coverage (Number) - the share, from 0 to 1
localization-pack-share = { NUMBER($coverage, style: "percent") }
# How many messages a language pack has for a plugin that is not installed, so their share is not known.
# $count (Number) - how many messages
localization-pack-messages =
    { $count ->
        [one] one message
       *[other] { $count } messages
    }
# The title of the notification after language packs were installed; they need no restart.
# $names (String) - the names of the language packs, such as Dutch Language Pack
localization-pack-installed-title = { $names } installed
# What to do with a language pack that was just installed.
localization-pack-installed = Choose its language in Settings, under Appearance. No restart is needed.
# The badge on the page of a language pack that is installed.
localization-pack-installed-badge = Installed
