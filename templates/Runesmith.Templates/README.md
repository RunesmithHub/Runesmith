# Runesmith.Templates

Templates for building Runesmith plugins and language packs.

```bash
dotnet new install Runesmith.Templates
dotnet new runesmith-plugin --name MyPlugin --id publisher.myplugin
dotnet new runesmith-language-pack --name RunesmithDutch --id publisher.dutch --languageTag nl --languageName Nederlands --englishName Dutch
```

| Template | Short name | What it creates |
| --- | --- | --- |
| Runesmith plugin | `runesmith-plugin` | A plugin: its contracts and implementation projects, a `plugin.json` manifest, an icon, a sample command and its English text in `locales/en`. |
| Runesmith language pack | `runesmith-language-pack` | A language pack: translations of Runesmith and of plugins into one language, a `plugin.json` manifest and an icon. |
