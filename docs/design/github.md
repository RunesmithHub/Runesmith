# Git and GitHub

The `runesmith.git` plugin brings version control into Runesmith: everything a developer does with Git every day. The `runesmith.github`
plugin adds the GitHub account features around it: signing in, choosing what Runesmith may access, cloning from the account and its
organizations, and pull requests.

## Goals

- **The everyday loop takes no terminal:** see what changed, stage it, write or amend a commit message, commit, pull, push, switch and
  create branches, fetch, and read the history.
- **Signing in is safe and simple:** no passwords or hand-made tokens; the user approves Runesmith in the browser and chooses which
  repositories and organizations it may use.
- **It respects the user's Git:** their configuration, hooks, signing keys, SSH setup and credential helpers all keep working.
- **It stays fast:** status refreshes never block typing; large histories scroll without loading everything.

## How Git is driven

The plugin runs the `git` command line. A library implementation of Git would skip the user's hooks, signing, SSH agents, credential
helpers and newer features; the command line has them all, and every command Runesmith runs is one the user can run themselves.

- **Machine-readable output only:** `status --porcelain=v2 -z --branch`, `log` with a `-z` and `%x00`-separated format, `diff
  --numstat -z` and `for-each-ref --format`. Nothing parses text meant for people.
- **One queue per repository:** commands that change the repository run one at a time; reads run beside them.
- **Watching:** a file watcher on the working tree and `.git` (index, HEAD, refs) schedules a status refresh, debounced by 300 ms and
  never more than one at a time.
- **Credentials:** for `https://github.com` remotes, Runesmith passes the signed-in token to Git through a credential helper that it
  adds for that one command (`-c credential.https://github.com.helper=...`) and an environment variable, so the token never appears in
  command lines, logs or Git's own configuration. Other remotes use the user's own setup.
- **Errors:** a failing command's message is shown as Git wrote it, with an action where one helps, such as Pull before a rejected push.

## Signing in

Runesmith signs in as its own public **GitHub App**, Runesmith Editor (`runesmith-editor`), using GitHub's **device flow**, so signing in
works without registering anything:

1. **Sign in to GitHub** shows a short code and opens `https://github.com/login/device`. The user enters the code and approves Runesmith.
2. The plugin polls for the result, then stores the user access token and refresh token in the system's secret store.
3. **Manage access** opens the app's installation page, where the user installs it on their account and on organizations and picks
   which repositories it may use. The repository list in Runesmith shows exactly those.

GitHub Apps give short-lived tokens (8 hours) with a refresh token, fine-grained permissions, and access the user or organization owner
controls per repository, which is why they are the right kind of app here. The app asks for: Contents (read and write), Metadata (read),
Pull requests (read and write), Workflows (read and write, so commits that change workflows can be pushed) and the user's email address.

The app's identity (app ID, client ID, slug and name) ships in `github-app.json` next to the plugin, read once at startup; a build of
Runesmith replaces that file to ship its own app. The `github.clientId` and `github.appSlug` settings, empty by default, override it for
users who register their own app (**Settings > GitHub > Advanced > Use your own GitHub App**, with **Back to Runesmith's app** to clear
them). When neither names a client ID, the sign-in dialog shows how to register an app. Client and app IDs are public by design: the
device flow uses no secret, so nothing secret ships. Signing out deletes the tokens. A token the GitHub CLI already has
(`gh auth token`) can be used instead, for people who prefer it.

### Organization access

An organization's repositories reach Runesmith through an installation of the app on that organization; GitHub decides who may install
it, and members' installs become requests that owners approve. Runesmith's side:

- **Repository access** in **Settings > GitHub** shows `GET /user/installations`: each account or organization with its avatar, **All
  repositories** or **N selected** (from the installation's repository list), and **Configure**, which opens
  `github.com/settings/installations/<id>` for the user or `github.com/organizations/<org>/settings/installations/<id>` for an
  organization. The account menu shows the organizations in an **Organizations** section.
- **Add an organization...** (that card, the account menu, a **+ Organization** chip among the owners in the clone dialog, and the clone
  dialog's empty state) shows a two-sentence dialog, then **Continue on GitHub** opens `github.com/apps/<slug>/installations/new`, GitHub's
  own picker of accounts and organizations.
- Runesmith notes the installations before opening GitHub. Each time its window is activated in the next 30 minutes, it loads the
  installations and repositories again; when a new installation appears it says "Runesmith can now use repositories of *org*" and stops
  watching. When none appears, it says nothing.
- The clone dialog finds the action through a command named `<host id>.addOrganization` (`github.addOrganization`), so the Git plugin
  needs no GitHub-specific code, and the host's `Changed` event, raised when the installations change, makes it load the list again.
- With a token or the GitHub CLI there are no installations: the card explains that organization repositories come from the token's own
  access, and offers nothing app-specific.

## The experience

### Toolbar: the branch widget

The toolbar's branch widget shows the current branch, with an arrow badge for commits to pull and push. Its popup:

- a search field over branches;
- actions: Update Project (pull), Commit..., Push..., New Branch..., Fetch;
- Recent, Local and Remote branch groups; each branch opens a submenu: Checkout, New Branch from Here, Compare with Current, Rename,
  Delete, and Push or Track for local and remote ones.

### The Commit tool window

On the left stripe, with a count badge of changed files:

- **Changes:** files grouped as staged and not staged, each with its status letter and color; check boxes stage and unstage files or
  folders; a context menu has Show Diff, Rollback, Open, and Ignore.
- **Message:** a multi-line box with the subject's length shown past 72 characters, the last messages one click away, and Amend, which
  loads the previous commit's message and keeps its changes when the box is cleared.
- **Commit** and **Commit and Push**, with Signoff and Skip hooks under an options menu.

### The Git tool window: history

In the bottom area:

- **Log:** a virtualized list of commits with the branch graph drawn in lanes, the subject, branch and tag labels, author, and date;
  filters for branch, author, path, and text; it loads pages of commits as the user scrolls.
- **Details:** for the selected commit, its full message, author and committer, parents, and changed files; double-clicking a file shows
  its diff for that commit.
- **Actions** on a commit: Checkout, New Branch, Cherry-pick, Revert, Reset Current Branch to Here, Copy Hash, Open on GitHub.
- **File history:** from the editor or Explorer, the log filtered to one file, following renames.

### In the editor

- **Change markers** in the gutter for added, modified and deleted lines against HEAD; clicking one shows the old text with Rollback.
- **Diffs** open as editor tabs with the two versions side by side, changed lines and characters highlighted, and the working copy side
  editable.

### Cloning

**Clone Repository** (welcome screen, File menu, branch widget when no repository is open) opens a dialog with:

- **GitHub:** the repositories of the signed-in account and the organizations it has given access to, grouped by owner, with search,
  descriptions, privacy and stars; and
- **URL:** any Git URL.

Below, the target folder (remembered) with the hint of where the clone goes; Clone shows progress from Git's own progress output and
then opens the folder.

### GitHub features

- **Open on GitHub** for a file (with the selected lines), a commit, or the repository.
- **Create Pull Request** from the current branch: base branch, title and description (prefilled from the commits), draft, and the
  result opens in the browser.
- **Pull Requests** tool window: open pull requests of the repository, with their checks and review state; Checkout switches to a pull
  request's branch.

### Status bar

The branch and the sync state (ahead and behind) sit in the status bar too, and long operations (clone, fetch, push) show as background
tasks with Cancel.

## What Runesmith adds for it

| Contract | Lives in | For |
| --- | --- | --- |
| `IToolbarWidgetProvider` | `Runesmith.Sdk.Shell` | The branch widget; a plugin places a control in a toolbar slot |
| `IChangeBaseProvider` | `Runesmith.Sdk.Documents` | The base version of files that the gutter's change markers compare with |
| `IDiffService` | `Runesmith.Sdk.Documents` | Opening a side-by-side diff in the editor area |
| `ISecretStore` | `Runesmith.Sdk.Shell` | Storing tokens in the system's secret store |
| `ILauncher` | `Runesmith.Sdk.Shell` | Opening a link in the browser and revealing a file |
| `CommandIds.CloneRepository` | `Runesmith.Sdk.Commands` | The welcome screen's Clone button |
| Plugin menus in the main menu | `Runesmith.Sdk.Commands` | The Git menu, after Build |
| `ContextMenus` and `ContextMenuTarget` | `Runesmith.Sdk.Commands` | The Git groups of the editor's and the Explorer's context menus |
| `IToolWindowManager.SetAvailable` | `Runesmith.Sdk.ToolWindows` | Hiding the Commit, Git and Pull Requests windows where they do not apply |
| `ISettingsSectionProvider` | `Runesmith.Sdk.Settings` | The account and the GitHub App's state at the top of the GitHub settings |
| HammerUI's `IDialogService` and `IToastService` | exported by the shell | Dialogs and toasts from plugins |

The secret store uses the system's own: the Secret Service on Linux (through `secret-tool`), the Keychain on macOS (through `security`),
and the Windows Credential Manager. Where none is available it falls back to a file only the user can read, and says so.

## Developers

- **Layers:** in the Git plugin, `Git/` drives the command line and knows nothing about the UI, `Repositories/` follows the open folder's
  repository, `Hosting/` finds the host of a remote over the hosting plugins' providers, and `Views/` are the controls. In the GitHub
  plugin, `GitHub/` speaks the REST and GraphQL APIs and the device flow and implements the hosting contracts, and `Views/` holds the
  sign-in dialog, the account button and the settings section. The avatar cache and small view builders in `plugins/Shared/Views` are
  compiled into both.
- **Tests** run real Git in temporary repositories: status in every state (staged, unstaged, renamed, conflicted, untracked), commits
  with amend, branches, fetch and push against a local bare remote, log parsing with merges, and diffs; the clone dialog's and the
  Pull Requests window's models run over a fake host. The GitHub client is tested against recorded responses, and the device flow against
  a fake server.
- **No network in tests**, and nothing reaches GitHub unless the user signs in or asks for something that needs it.

## Delivery

1. The host contracts and their implementations.
2. Git: the service, status, staging, commits and amend, branches, fetch, pull, push, the Commit tool window, the branch widget.
3. History: the log with its graph, details, file history, diffs, gutter markers.
4. GitHub: signing in, access, cloning, Open on GitHub, pull requests.

## Hosting services: GitHub, Forgejo and Gitea

Git itself does not depend on where a repository is hosted, so it lives in its own plugin, `runesmith.git`: the Commit and Git windows,
the branch widget, history, diffs, change markers, the clone dialog and the Pull Requests window. Hosting services are plugins on top of it:
`runesmith.github`, `runesmith.forgejo` and `runesmith.gitea`. Each can be switched off on its own.

The contracts between them are in `Runesmith.Sdk.VersionControl`, so every plugin shares them:

| Contract | Provided by | For |
| --- | --- | --- |
| `IRepositoryService` | Git | The open folder's repository: branch, upstream, ahead and behind, remotes |
| `IGitService` | Git | Running Git the way the Git plugin does, with credentials for one command, such as a clone |
| `IGitCredentialSource` | Each hosting plugin | A signed-in account's token for its server's remotes |
| `IRepositoryHostProvider` | Each hosting plugin | Its accounts, and adding one, such as on another server |
| `IRepositoryHost` | Each hosting plugin | One account: signing in and out, its repositories, web links, the default branch, pull requests |

- **Clone Repository** shows a tab per signed-in account, an add-account entry for every provider, and a URL tab.
- **Pull Requests** lists the pull requests of the open repository's host, whichever service it is; Checkout fetches the pull request's
  head ref through `IGitService`.
- **Open on ...** and **Copy Link** use the host that owns the remote.

### Forgejo and Gitea

Forgejo started as a fork of Gitea and keeps its REST API (`/api/v1`) compatible, so both plugins share one client and differ in their
names, icons, default servers and how they recognize a server: **Forgejo** starts with Codeberg (`codeberg.org`) and **Gitea** with
`gitea.com`, and both add any self-hosted server by its address, checking `/api/v1/version` (and `/api/forgejo/v1/version` for Forgejo).

Signing in uses OAuth2 with PKCE and a loopback redirect where an OAuth2 application is registered for the server, and otherwise an access
token the user creates on the server's settings page, with the scopes Runesmith needs listed and a link to that page. Tokens live in the
system's secret store, one entry per server and account.
