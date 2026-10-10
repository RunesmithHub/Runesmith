# Runesmith.Sdk

The API for Runesmith plugins. A plugin is a contracts project and an implementation project that reference this package, and a
`plugin.json` manifest. The implementation exports parts with the `System.Composition` attributes: commands, tool windows, languages,
language servers, completion and hover providers, build providers, test providers, settings, and color themes, syntax colors and file
icons.

Create a plugin from the template:

```bash
dotnet new install Runesmith.Templates
dotnet new runesmith-plugin --name MyPlugin --id publisher.myplugin
```

Reference the package without copying it next to the plugin, because Runesmith supplies it:

```xml
<PackageReference Include="Runesmith.Sdk" Version="0.1.1" ExcludeAssets="runtime" />
```

The package's build targets add `InstallPlugin`: `dotnet build <implementation project> -t:InstallPlugin` lays the plugin out as Runesmith
loads it and copies it into your plugins folder.

The documentation explains the manifest, where plugins are installed and every extension point:
https://github.com/RunesmithHub/Runesmith/tree/main/website/content/plugins
