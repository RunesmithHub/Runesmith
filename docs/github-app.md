# Runesmith's GitHub App

Runesmith signs in to GitHub as a **GitHub App** with GitHub's **device flow**: the user approves Runesmith on github.com and installs the
app on their account and organizations, choosing which repositories it may use. Runesmith ships with its own public app, so signing in
works without registering anything. Forks and companies can register an app of their own instead; the second half of this page explains
how.

## The app Runesmith ships

| | |
| --- | --- |
| Name | Runesmith Editor |
| Public page | <https://github.com/apps/runesmith-editor> |
| App ID | `5245881` |
| Client ID | `Iv23li1DdS0IT4aRfQxi` |
| Owner | `DutchyD` |

The app's identity is not in the code. It is in `plugins/Runesmith.GitHub/Runesmith.GitHub/github-app.json`, which is copied next to the
plugin's assembly and read once when Runesmith starts:

```json
{
  "appId": 5245881,
  "clientId": "Iv23li1DdS0IT4aRfQxi",
  "slug": "runesmith-editor",
  "name": "Runesmith Editor"
}
```

Runesmith picks the app in this order:

1. The `github.clientId` setting, with `github.appSlug`, when the user set their own app (**Settings > GitHub > Advanced**).
2. `github-app.json` next to the plugin's assembly, in `plugins/runesmith.github/lib`.

When neither gives a client ID, because the file is missing, unreadable or has no `clientId`, signing in through the browser shows how
to register an app, and the GitHub CLI's sign-in and tokens still work.

### Why the IDs are safe in the repository

A GitHub App's client ID and app ID are public by design. GitHub shows both on the app's public page and in its API
(`https://api.github.com/apps/runesmith-editor`), and every device flow sign-in sends the client ID in the clear. The device flow and its
token refreshes need no client secret, so Runesmith has none: the app has no client secret, no private key, no webhook and no server.

Knowing the client ID lets someone start a sign-in to the app, which is what it is for. It gives no access to anything: every token comes
from a user approving the app on github.com, and reaches only the repositories that user or their organization owners installed it on.

## Adding organizations

Installing the app is what gives Runesmith a repository. Each account and organization has its own installation, with either all of its
repositories or the ones chosen.

- **Settings > GitHub > Repository access** lists the installations: each account or organization with its avatar, **All repositories**
  or **N selected**, and **Configure**, which opens the installation's settings page on GitHub
  (`https://github.com/settings/installations/<id>` for your account, `https://github.com/organizations/<org>/settings/installations/<id>`
  for an organization).
- **Add an organization...** in that card, in the account menu's **Organizations** section, as **+ Organization** among the owners in the
  clone dialog, or in the clone dialog when it has no repositories, explains what happens and opens
  `https://github.com/apps/runesmith-editor/installations/new`. There GitHub asks which account or organization to install the app on and
  which repositories it may use. Organization owners install it right away; members send a request that an owner approves.
- Coming back to Runesmith loads the installations and repositories again. When a new installation appeared, a notification says
  "Runesmith can now use repositories of *organization*".

**Manage Access** still opens the install page, where an existing installation's repositories can be changed too.

## Using your own app

A fork, a company build or a single user can sign in with a GitHub App of their own. Register it once as below, then either:

- **For yourself:** open **Settings > GitHub**, expand **Advanced** under the app, and put the client ID into **Client ID** and the
  app's name in its address, `github.com/apps/<name>`, into **App name in URLs**. In `settings.json` they are:

  ```json
  {
    "github.clientId": "Iv23liAbCdEf123456",
    "github.appSlug": "acme-runesmith"
  }
  ```

  **Back to Runesmith's app** clears both. Sign in again after switching apps.

- **For a build of Runesmith:** replace `plugins/Runesmith.GitHub/Runesmith.GitHub/github-app.json` with your app's values, so every
  user of the build signs in with it without setting anything.

The app name is optional with the settings: once the app is installed anywhere, Runesmith learns it from the installation. Until then,
**Add an organization** is not offered and **Manage Access** opens GitHub's list of installed apps.

### 1. Create the app

Open **Settings > Developer settings > GitHub Apps > New GitHub App** on github.com, or go straight to
<https://github.com/settings/apps/new>. To own the app with an organization instead, use the organization's **Settings > Developer settings
> GitHub Apps > New GitHub App**.

Fill in the form from top to bottom:

| Field | Value |
| --- | --- |
| GitHub App name | A name of your own; at most 34 characters, and unique on GitHub |
| Description | `Clone repositories, push changes and work with pull requests from the Runesmith editor.` |
| Homepage URL | `https://github.com/RunesmithHub/Runesmith`, or your build's page |
| Callback URL | Leave it empty; the device flow does not use it |
| Expire user authorization tokens | **On** (the default); see [Token expiration](#token-expiration) |
| Request user authorization (OAuth) during installation | Off |
| Enable Device Flow | **On**; without it, signing in stops with "The GitHub App does not have the device flow turned on." |
| Setup URL | Leave it empty |
| Redirect on update | Off |
| Webhook: Active | **Off**; Runesmith receives no events |

### 2. Permissions

Under **Permissions**, set these and leave everything else at **No access**:

| Kind | Permission | Access | What Runesmith does with it |
| --- | --- | --- | --- |
| Repository | Contents | Read and write | Clone, fetch, pull and push |
| Repository | Metadata | Read-only | Required by GitHub for every app; lists repositories |
| Repository | Pull requests | Read and write | Lists pull requests and creates them |
| Repository | Workflows | Read and write | Pushes commits that change files in `.github/workflows` |
| Repository | Checks | Read-only | Shows the checks state of pull requests |
| Repository | Commit statuses | Read-only | Shows the status state of pull requests that use commit statuses |
| Account | Email addresses | Read-only | Reads the account's email address |

Checks and Commit statuses only add the checks column of the Pull Requests tool window; without them the window shows pull requests
without their checks.

Leave **Subscribe to events** empty.

### 3. Where it can be installed

Choose **Any account**, so every user can install the app on their own account and on the organizations they manage. **Only on this
account** limits it to the account that owns the app, which suits a private company build.

Select **Create GitHub App**. Do not generate a client secret or a private key; Runesmith uses neither.

### 4. Try it

The app's settings page shows its **Client ID**, such as `Iv23liAbCdEf123456`, and its public page is `https://github.com/apps/<name>`.
Give both to Runesmith as described above, then:

1. In Runesmith, select the GitHub button in the toolbar, then **Continue with GitHub**.
2. Copy the code, open GitHub, enter it and approve the app.
3. Select **Add an organization...** or **Manage Access** in the account menu, install the app on your account and organizations, and
   choose the repositories.
4. **File > Clone Repository...** lists exactly those repositories, grouped by owner.

## Token expiration

With **Expire user authorization tokens** on, GitHub gives Runesmith an access token that lasts 8 hours and a refresh token that lasts
6 months. Runesmith refreshes the access token a few minutes before it expires; GitHub's documentation states that refreshing a token from
the device flow does not need the client secret, so this works without shipping one. When the refresh token expires or is revoked,
Runesmith signs out and asks the user to sign in again.

With the setting off, the access token does not expire and there is no refresh token; Runesmith keeps using it until the user signs out or
revokes it on GitHub. Expiring tokens are safer, and recommended.

Tokens are kept in the system's secret store under keys that start with `runesmith.github/`, and never appear in command lines, logs or
Git's configuration: Git receives them through a credential helper for the one command that needs them.

## Without the app

Users can also sign in with the GitHub CLI's own sign-in (`gh auth login` first), or by pasting a fine-grained or classic personal access
token. The repository list then shows what the token can reach, including organization repositories the token has access to, instead of
what the app's installations allow; organizations are added on the token's side, not in Runesmith.
