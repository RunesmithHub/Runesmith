# MyLanguagePack

A Runesmith language pack: Runesmith, and plugins, in LANGUAGE_ENGLISH_NAME (LANGUAGE_NAME, `LANGUAGE_TAG`). Installing it adds the language
to the language picker in Settings, under Appearance, without a restart.

## Layout

| Path | What it is |
| --- | --- |
| `plugin.json` | The manifest: the pack's id, name and version, and in `languagePack` the language it adds |
| `MyLanguagePack/locales/LANGUAGE_TAG/runesmith/` | The translation of Runesmith: one file for each of Runesmith's English files, with the same name |
| `MyLanguagePack/locales/LANGUAGE_TAG/<plugin id>/` | The translation of a plugin, such as `runesmith.git/runesmith.git.ftl` |
| `MyLanguagePack.Contracts/` | Every plugin has a contracts project; a language pack's stays empty |
| `icon.png` | The pack's icon, 256 to 1024 pixels square. Replace the placeholder |

## Translate

The files are in [Fluent](https://projectfluent.org). Translate the text after `=` and keep the message ids, the variables, such as
`{ $count }`, and the names of variants, such as `[one]` and `*[other]`; add the variants your language needs for plurals. The comments
above the English messages say where a text shows and what its variables are.

Runesmith's English files come with the `Runesmith.Sdk` package, in its `locales/en/runesmith/` folder. Copy each one into
`MyLanguagePack/locales/LANGUAGE_TAG/runesmith/` and translate it. The starter file shows the shape; replace its English text.

Every build checks the translation against the English source: messages that are missing, messages English no longer has, variables that
differ from the English and Fluent syntax errors. A message the pack does not translate shows in English.

To translate a plugin, put its English files in the repository, such as `english/runesmith.git/`, and add them to
`MyLanguagePack/MyLanguagePack.csproj` so the build checks against them:

```xml
<RunesmithEnglishSource Include="english/runesmith.git/*.ftl" TranslationTarget="runesmith.git" />
```

## Try it

```bash
dotnet build MyLanguagePack -t:InstallPlugin
```

Then choose LANGUAGE_NAME in Settings, under Appearance. Turn on **Pseudo-localization** under Language to see which text in Runesmith is
not translatable yet.

## Release

1. Put the pack in a public GitHub repository with a `LICENSE` that matches `license` in `plugin.json`, and set `repository` to its
   address.
2. Set `version` in `plugin.json`, commit, and tag the commit `v` followed by the version, such as `v0.1.0`.
3. Create a GitHub release from the tag.
4. Register the pack at the Runesmith plugin hub once. The hub builds every release you publish from its tag, and lists how much of each
   target it translates.
