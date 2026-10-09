# MyPlugin

A Runesmith plugin. `MyPlugin/MyPluginCommands.cs` adds a **Say Hello** command to the command palette and the Help menu.

## Layout

| Path | What it is |
| --- | --- |
| `plugin.json` | The manifest: the plugin's id, name, version, the Runesmith API versions it works with, its dependencies and capabilities |
| `MyPlugin.Contracts/` | The plugin's public API: interfaces and data types other plugins use when they depend on this one. It may stay empty |
| `MyPlugin/` | The implementation: the parts the plugin exports, its views and its logic. Other plugins never see it |
| `icon.png` | The plugin's icon, 256 to 1024 pixels square. Replace the placeholder |
| `nuget.config` | Restores packages from nuget.org and the plugin hub's feed, and always takes Runesmith's packages and plugin contracts from the hub's feed |

Both projects keep a `packages.lock.json`; commit them with the code.

## Build and install

```bash
dotnet build MyPlugin -t:InstallPlugin
```

This builds the plugin, lays it out as Runesmith loads it (`plugin.json`, and the assemblies in `lib/`) and copies it into the folder
Runesmith loads your plugins from, in a subfolder named after the plugin's id:

| System | Folder |
| --- | --- |
| Windows | `%APPDATA%\Runesmith\plugins` |
| Linux | `~/.config/runesmith/plugins` |
| macOS | `~/Library/Application Support/Runesmith/plugins` |

When the `RUNESMITH_HOME` environment variable is set, the folder is `$RUNESMITH_HOME/plugins`. Restart Runesmith to load the new build,
then press Ctrl+K and run **Say Hello**.

## Debug

1. Install the plugin as above.
2. Start Runesmith, then attach your debugger to its process (`runesmith`), or start Runesmith from your IDE as the program to debug.
3. Set breakpoints in your plugin; they bind once Runesmith loads it.

Start Runesmith with `--diagnostics` to see which plugins loaded, and why one did not.

## Capabilities

List in `capabilities` what the plugin does beyond working inside Runesmith through the SDK, each with a sentence users read before they
install it: `network`, `process`, `filesystem`, `environment`, `credentials`, `native` or `dynamic-code`. With `network`, list the hosts
in `networkHosts`. Runesmith's secret store needs `credentials` and its launcher needs `process`; without them, they refuse the plugin.

## Dependencies

To use another plugin, add its contracts package (`RunesmithHub.<its contracts assembly name>`) to the project that needs it, and add the
plugin to `dependencies` with a range such as `^1.2.0`, starting at the version you reference. A plugin can use only the contracts of the
plugins it depends on.

Runesmith loads each plugin apart from the others, so plugins can use different versions of the same library. The SDK, Avalonia, HammerUI
and the other libraries Runesmith shares always come from Runesmith. To use another NuGet package, add it and set
`<EnableDynamicLoading>true</EnableDynamicLoading>` in `MyPlugin.csproj`, so the build copies the package next to the plugin.

## Release

1. Put the plugin in a public GitHub repository with a `LICENSE` that matches `license` in `plugin.json`, and set `repository` to its
   address.
2. Set `version` in `plugin.json`, commit, and tag the commit `v` followed by the version, such as `v0.1.0`.
3. Create a GitHub release from the tag.
4. Register the plugin at the Runesmith plugin hub once. The hub builds every release you publish from its tag.
