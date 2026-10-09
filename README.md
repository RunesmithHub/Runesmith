# Runesmith

Runesmith is a code editor built on .NET 10 and [Avalonia](https://avaloniaui.net), for Windows, Linux and macOS. It opens a folder, edits
its files with highlighting for more than 60 languages, and gets completion, signature help, hover, go to definition and problems from
its own C# analyzer and from language servers. This first version focuses on C#.

Runesmith is a private MVP for now.

## Features

- **A docking workspace.** Editors and panels are tabs that drag to any edge, into another group or out into a window of their own, in
  light and dark.
- **An editor for code.** TextMate highlighting, brackets and quotes that close as you type, indentation that follows the code, line
  commands, and find and replace with regular expressions.
- **C# analysis.** Completion, signature help, hover, go to definition and problems as you type, from Runesmith's own C# analyzer on
  the C# compiler (Roslyn). It runs inside Runesmith, supports every C# version up to C# 14, and needs no install step.
- **Language servers.** Plugins can add Language Server Protocol servers for other languages.
- **Navigation.** Quick Open, a command palette for every command, go to line, back and forward, and find in files with globs.
- **Building.** `dotnet build` with its errors and warnings in the **Problems** panel.
- **Settings and keys.** Settings for you and for each folder, and key bindings you can change.
- **Plugins.** Languages, language servers, commands, panels, settings and build providers, with a `dotnet new` template to start from.

## Quick start

Clone Runesmith and run it with the [.NET 10 SDK](https://dot.net):

```bash
git clone https://github.com/RunesmithHub/Runesmith.git
cd Runesmith
dotnet run --project src/Runesmith.App
```

Its UI library, [HammerUI](https://github.com/RunesmithHub/HammerUI), comes from the Runesmith Hub feed, with no sign-in.

C# completion, hover and problems work without installing anything else. Loading your C# projects uses MSBuild, which needs the .NET SDK,
as building does.

## Documentation

The documentation site's sources are in [`website/content`](website/content):

- [Guide](website/content/guide): installing, the window, the editor, navigation, language features, settings, keyboard shortcuts, the
  command line and troubleshooting.
- [Plugins](website/content/plugins): how plugins load, creating one with the template, the API and C# support.
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
