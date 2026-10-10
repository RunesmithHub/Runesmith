# Runesmith documentation site

The guide, the plugin documentation and the developer reference for Runesmith, built with Docusaurus.

## Commands

| Command | Purpose |
| --- | --- |
| `npm ci` | Install dependencies (Node.js 20 or later). |
| `npm start` | Run the site locally with live reload. |
| `npm run build` | Check the prose, then build the static site into `build/`. Broken links and anchors fail the build. |
| `npm run serve` | Serve the production build locally. |
| `npm run typecheck` | Type-check the TypeScript sources. |
| `npm run lint:prose` | Check content for the punctuation, emoji and phrases [STYLE.md](STYLE.md) rules out. Runs before every build. |

## Publishing

`.github/workflows/website.yml` builds the site on every pull request that changes `website/` and publishes it to GitHub Pages from `main`.
The build reads its address from `SITE_URL` and `BASE_URL`; without them it builds for `https://docs.runesmith.dev/`.

## Structure

```text
content/
  guide/           Using Runesmith (served at /guide)
  plugins/         Writing plugins and the built-in ones (served at /plugins)
  developers/      Architecture, building, contributing and releasing (served at /developers)
sidebars/          One sidebar file per section: guide.ts, plugins.ts, developers.ts
src/
  components/      MDX components (Keys, CardGrid, Steps, Icon)
  pages/           The homepage
  theme/           MDXComponents: the components every MDX page can use without importing them
  types/           Shared TypeScript types
  css/             Theme tokens (Runesmith's palette) and Infima overrides
scripts/
  lint-prose.mjs   The prose lint
```

Each section is its own docs plugin with its own content folder and sidebar file, so work on one section never touches another section's
files.

## Writing pages

Pages are MDX files in their section's folder. Add a new page to the section's sidebar file, or it will not appear in the navigation.
Read [STYLE.md](STYLE.md) before writing.

Every MDX page can use these components without importing them:

| Component | Use |
| --- | --- |
| `<Keys>Ctrl+Shift+N</Keys>` | Key caps. Also takes `combo="..."`. `Up`, `Down`, `Left` and `Right` render as arrows. |
| `<Steps>` | Wraps a Markdown ordered list and numbers it as steps. Leave a blank line after `<Steps>` and before `</Steps>`. |
| `<CardGrid columns={2}>` and `<Card title="" icon="" to="">` | A grid of link cards. Icons are the names in `src/components/Icon/icons.ts`. |
| `<Icon name="plug" />` | An inline icon. Use sparingly. |

Use Docusaurus admonitions (`:::tip`, `:::note`, `:::warning`, `:::danger`) for asides, and fenced code blocks with a title for files:

````md
```json title="plugin.json"
...
```
````

### Links

- Within a section, link to the file: `[Settings](./settings.mdx)`.
- Across sections, link to the route: `[Create a plugin](/plugins/create-a-plugin)`, `[The editor](/guide/editor)`.

Broken links and anchors fail the build either way.

## Style

[STYLE.md](STYLE.md) describes the voice, page structure and conventions for code samples. `npm run lint:prose` enforces the mechanical
parts and runs before every build.
