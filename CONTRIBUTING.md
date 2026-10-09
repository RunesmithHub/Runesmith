# Contributing to Runesmith

Thanks for helping. The [contributor guide](website/content/developers/contributing.mdx) has the conventions for code, documentation,
commits and pull requests; this page is the short version.

## Before you start

- For a bug, open an issue with the steps to reproduce it. For a security problem, follow [SECURITY.md](SECURITY.md) instead.
- For a larger change, such as a new panel or a new project, open an issue first, so the approach is agreed on before you write the code.
- A new project in `src/` must pass the checklist in [Projects and when to add one](website/content/developers/projects.mdx).

## Setting up

Clone Runesmith. To change HammerUI or the hub library alongside it, clone those too, and the build uses the checkouts instead of the
packages; [Building and testing](website/content/developers/building.mdx) shows where they go.

```bash
git clone https://github.com/RunesmithHub/Runesmith.git
```

On NixOS, work in the development shell from `flake.nix` (`nix develop`, or direnv with `.envrc`), and start your IDE from it, such as
`nix develop -c <editor> Runesmith.slnx`; otherwise the app cannot load its font and X11 libraries. See
[Building and testing](https://runesmithhub.github.io/Runesmith/developers/building) for details.

## Working on the plugin hub client

Builds without `src/Runesmith.App/Hub/root.json` have no plugin hub. To try the hub client against a local index, generate the signed sample
index with the hub tool from a [RunesmithHub/hub](https://github.com/RunesmithHub/hub) checkout, serve it, and point a Debug build at it:

```bash
dotnet run --project ../hub/src/RunesmithHub.Tool -- dev fixture /tmp/hub-index --base-url http://127.0.0.1:8080/
python3 -m http.server 8080 --bind 127.0.0.1 --directory /tmp/hub-index
RUNESMITH_HOME=/tmp/runesmith-hub RUNESMITH_HUB_ROOT=/tmp/hub-index/root/1.json RUNESMITH_HUB_INDEX_URL=http://127.0.0.1:8080/ \
  dotnet run --project src/Runesmith.App -- --new-instance
```

`RUNESMITH_HUB_ROOT` names the trust root and `RUNESMITH_HUB_INDEX_URL` the index. They are for development only: Release builds ignore
both. The sample index expires three days after it is written; write a new one then.

## Making a change

1. Fork the repository and create a branch from `main`.
2. Build and test:

   ```bash
   dotnet build Runesmith.slnx -warnaserror
   dotnet test --solution Runesmith.slnx -- --ignore-exit-code 8
   ```

3. Keep the formatting: `dotnet format whitespace Runesmith.slnx` and `dotnet format style Runesmith.slnx` fix it.
4. When behavior, a setting, a command, a key binding or the plugin API changes, update the documentation in `website/` in the same commit;
   `website/STYLE.md` covers how it is written.
5. Write each commit message as a [Conventional Commit](https://www.conventionalcommits.org/en/v1.0.0/), such as
   `fix(editor): keep the caret in view after a paste`. The types are `feat`, `fix`, `perf`, `refactor`, `test`, `docs`, `ci`, `chore`
   and `revert`; the scope names the part of Runesmith, such as `editor`, `shell`, `languages` or `sdk`.
6. Open a pull request against `main` and fill in the template.

## Code

- The solution builds without warnings; CI builds with `-warnaserror`.
- Public types and members have XML documentation that says what they do for the caller.
- Comments explain only what the code cannot say, in one line where possible.
- `BannedSymbols.txt` lists calls that are easy to get wrong, with what to use instead.

## License

Runesmith is licensed under the [Apache License 2.0](LICENSE). Contributions you submit are licensed under the same terms, as section 5 of
the license describes.
