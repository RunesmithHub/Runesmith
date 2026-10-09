# Registering an OAuth2 application on Forgejo and Gitea

Runesmith's Gitea and Forgejo plugin signs in through the browser with OAuth2 where an OAuth2 application for Runesmith is registered on
the server, and with an access token everywhere else. This page explains how to register that application on one server, such as
Codeberg, gitea.com or a server of your own, and where its client ID goes.

The application is a **public client**: Runesmith uses the authorization code flow with PKCE (S256) and a loopback redirect to
`http://127.0.0.1` on a port it picks for each sign-in (RFC 8252). It needs no client secret, so nothing secret ships with Runesmith or
sits in its settings.

## 1. Who registers it

| Who | Where | Who can sign in with it |
| --- | --- | --- |
| Any account | **Settings › Applications** of that account, at `https://<server>/user/settings/applications` | Every account on the server; the application only names Runesmith |
| An organization's owner | The organization's **Settings › Applications** | Every account on the server |
| A server's administrator | The site administration's **Applications** page | Every account on the server |

An application registered by one account works for everyone on that server: whoever signs in approves Runesmith for their own account.

## 2. Create the application

Open the **Applications** page and find **Manage OAuth2 applications** (Forgejo) or **Manage OAuth2 Applications** (Gitea). Fill in the
form:

| Field | Value |
| --- | --- |
| Application name | `Runesmith`, or a name of your own; people see it on the approval page |
| Redirect URIs | `http://127.0.0.1/`, exactly, on one line |
| Confidential client | **Off**. A confidential client must prove itself with its secret, which a desktop app cannot keep |

Leave any other option at its default. Select **Create application**.

The server accepts any port for a loopback redirect URI of a public client, so `http://127.0.0.1/` covers every sign-in. Use
`127.0.0.1`, not `localhost`: the server matches the address as written, and Runesmith listens on `127.0.0.1` only.

## 3. Copy the client ID

The next page shows the **Client ID** and the **Client secret**. Copy the client ID. Runesmith does not use the secret; you can leave the
page without saving it.

## 4. Give Runesmith the client ID

Either:

- In Runesmith, start signing in to the server. Without a client ID, the dialog asks for an access token and offers **Set up browser
  sign-in**; select it, paste the client ID and select **Save and Sign In**.
- Or set it in **Settings › Forgejo** or **Settings › Gitea**, as a `server=ID` pair in **OAuth2 client IDs**:

| Service | Setting | Example |
| --- | --- | --- |
| Forgejo | `forgejo.oauthClientIds` | `codeberg.org=1a2b3c4d-..., git.example.com:3000=5e6f7a8b-...` |
| Gitea | `gitea.oauthClientIds` | `gitea.com=1a2b3c4d-..., example.com/git=5e6f7a8b-...` |

The server is written the way Runesmith shows it: the host, then `:port` when the server is not on the usual port, then the path when it
lives under one. An ID without a server is for the service's default server, Codeberg or gitea.com.

## 5. Sign in

Select **Sign In**. Runesmith opens the server's approval page in the browser; approve Runesmith, and the browser shows **Signed in**.
The access token lasts an hour, and Runesmith renews it with the refresh token before it expires. To take Runesmith's access away, revoke
it under **Settings › Applications › Authorized OAuth2 applications** on the server, or select **Sign Out** in Runesmith.

## When it does not work

| What Runesmith or the browser says | What it means | What to do |
| --- | --- | --- |
| The server's page says the client ID is invalid, or Runesmith says the server refused the OAuth2 application | The client ID is wrong, or belongs to another server | Copy the client ID again, and check the server part of the setting |
| The server's page says the redirect URI is invalid | The application's redirect URI is not `http://127.0.0.1/`, or it is a confidential client, for which the server does not ignore the port | Edit the application: set the redirect URI exactly and clear **Confidential client** |
| Runesmith says the sign-in did not finish, after you approved | The server refused to exchange the code, such as for a confidential client without its secret | Check the application as above, then sign in again |
| The sign-in was cancelled | You selected **Cancel** on the approval page | Sign in again |
| Runesmith could not open a local port | Something blocks listening on `127.0.0.1` | Use an access token instead |
| Signed out, the sign-in expired | The server refused the refresh token, for instance after the application was deleted or revoked | Sign in again |

## Access tokens instead

Without an application, sign in with an access token: on the server's **Settings › Applications**, under **Manage access tokens**, create
a token with access to **All** repositories and organizations, and these permissions:

| Permission | Access | What Runesmith does with it |
| --- | --- | --- |
| `repository` | Read and write | Clone, fetch, pull and push, list pull requests and create them |
| `user` | Read | The login and avatar, and the account's repositories |
| `organization` | Read | The organizations whose repositories the clone dialog lists |

Paste it into Runesmith's sign-in dialog. Runesmith checks it with `GET /api/v1/user` before keeping it in the system's secret store.

## References

- Forgejo: [OAuth2 provider](https://forgejo.org/docs/latest/user/oauth2-provider/), [API
  usage](https://forgejo.org/docs/latest/user/api-usage/)
- Gitea: [OAuth2 provider](https://docs.gitea.com/development/oauth2-provider), [API usage](https://docs.gitea.com/development/api-usage)
- [RFC 7636, PKCE](https://www.rfc-editor.org/rfc/rfc7636) and [RFC 8252, OAuth 2.0 for native apps](https://www.rfc-editor.org/rfc/rfc8252)
