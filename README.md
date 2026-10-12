# Runesmith

Runesmith is a code editor built on .NET 10 and [Avalonia](https://avaloniaui.net), for Windows, Linux and macOS. It is the editor and
the tools plugins build on, and nothing else: it knows no language, no build tool and no version control system by itself. Every language,
with its highlighting, completion, builds, runs and debugger, comes from a plugin, and so do Git, themes and viewers for files that are
not text.

Runesmith is a private MVP for now.

## Features

- **A docking workspace.** Editors and panels are tabs that drag to any edge, into another group or out into a window of their own, in
  light and dark.
- **An editor for text.** Brackets and quotes that close as you type, indentation that follows the text, line commands, folding, and find
  and replace with regular expressions.
- **A highlighting engine.** A TextMate engine colors a language with the grammar its plugin brings, and semantic tokens over it.
- **Language services.** Plugins bring language analyzers that run inside Runesmith, or Language Server Protocol servers; Runesmith turns
  either into completion, signature help, hover, navigation, problems, quick fixes, rename and formatting.
- **Navigation.** Quick Open, a command palette for every command, go to line, back and forward, and find in files with globs.
- **Building, running, debugging and testing.** The **Problems** panel, run configurations, a Debug Adapter Protocol client, the Test
  Explorer and the SDKs page, each filled by the plugins of your languages.
- **A terminal.** Your shell in a panel, with links to the files its output names.
- **Settings and keys.** Settings for you and for each folder, and key bindings you can change.
- **Plugins.** Languages, grammars, analyzers, language servers, commands, panels, editors, settings, build providers and more, with a
  `dotnet new` template to start from.

## Quick start

Clone Runesmith and run it with the [.NET 10 SDK](https://dot.net):

```bash
git clone https://github.com/RunesmithHub/Runesmith.git
cd Runesmith
dotnet run --project src/Runesmith.App
```

Its UI library, [HammerUI](https://github.com/RunesmithHub/HammerUI), comes from the Runesmith Hub feed, with no sign-in. Runesmith ships
with no plugins, so a new install edits every file as plain text. The official plugins live in their own repositories and are installed
from the plugin hub by those who want them: open **Tools › Plugins**, or start from [hub.runesmith.dev](https://hub.runesmith.dev).
When you open a file no installed plugin knows, Runesmith suggests the official plugin whose keywords name it.

## Documentation

The documentation site's sources are in [`website/content`](website/content):

- [Guide](website/content/guide): installing, the window, the editor, navigation, language features, settings, keyboard shortcuts, the
  command line and troubleshooting.
- [Plugins](website/content/plugins): how plugins load, creating one with the template and the API.
- [Developers](website/content/developers): the architecture, the language services, building and testing, running and debugging,
  contributing and releasing.

To read it as a site, run `npm ci` and `npm start` in `website/`.

## Building and testing

```bash
dotnet build Runesmith.slnx -warnaserror
dotnet test --solution Runesmith.slnx -- --ignore-exit-code 8
```

To change HammerUI and Runesmith together, see [Building and testing](website/content/developers/building.mdx).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Report security problems as [SECURITY.md](SECURITY.md) describes.

## License

Runesmith is licensed under the [Apache License 2.0](LICENSE). [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) lists the software it uses
and their licenses.
