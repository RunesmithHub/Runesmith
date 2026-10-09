import type {Config} from '@docusaurus/types';
import type * as Preset from '@docusaurus/preset-classic';
import {themes as prismThemes} from 'prism-react-renderer';

const github = 'https://github.com/RunesmithHub/Runesmith';

const config: Config = {
  title: 'Runesmith',
  tagline: 'A fast, extensible code editor.',
  favicon: 'img/favicon.ico',
  url: process.env.SITE_URL || 'https://runesmithhub.github.io',
  baseUrl: process.env.BASE_URL || '/Runesmith/',
  trailingSlash: false,
  onBrokenLinks: 'throw',
  onBrokenAnchors: 'throw',

  future: {
    v4: true,
    faster: true,
  },

  i18n: {
    defaultLocale: 'en',
    locales: ['en'],
  },

  markdown: {
    mermaid: true,
    hooks: {
      onBrokenMarkdownLinks: 'throw',
    },
  },

  themes: [
    '@docusaurus/theme-mermaid',
    [
      '@easyops-cn/docusaurus-search-local',
      {
        hashed: true,
        docsDir: ['content/guide', 'content/plugins', 'content/developers'],
        docsRouteBasePath: ['guide', 'plugins', 'developers'],
        docsPluginIdForPreferredVersion: 'default',
        indexBlog: false,
        highlightSearchTermsOnTargetPage: true,
        explicitSearchResultPath: true,
        searchBarShortcutHint: true,
      },
    ],
  ],

  presets: [
    [
      'classic',
      {
        docs: {
          path: 'content/guide',
          routeBasePath: 'guide',
          sidebarPath: './sidebars/guide.ts',
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
        sitemap: {
          changefreq: 'weekly',
          priority: 0.5,
        },
      } satisfies Preset.Options,
    ],
  ],

  plugins: [
    [
      '@docusaurus/plugin-content-docs',
      {
        id: 'plugins',
        path: 'content/plugins',
        routeBasePath: 'plugins',
        sidebarPath: './sidebars/plugins.ts',
      },
    ],
    [
      '@docusaurus/plugin-content-docs',
      {
        id: 'developers',
        path: 'content/developers',
        routeBasePath: 'developers',
        sidebarPath: './sidebars/developers.ts',
      },
    ],
  ],

  headTags: [
    {tagName: 'meta', attributes: {name: 'theme-color', content: '#5B5BEF'}},
    {tagName: 'link', attributes: {rel: 'apple-touch-icon', href: `${process.env.BASE_URL || '/Runesmith/'}img/apple-touch-icon.png`}},
  ],

  themeConfig: {
    colorMode: {
      defaultMode: 'dark',
      respectPrefersColorScheme: true,
    },
    docs: {
      sidebar: {
        hideable: true,
        autoCollapseCategories: true,
      },
    },
    tableOfContents: {
      minHeadingLevel: 2,
      maxHeadingLevel: 3,
    },
    navbar: {
      title: 'Runesmith',
      hideOnScroll: false,
      logo: {
        alt: 'Runesmith',
        src: 'img/logo.png',
      },
      items: [
        {type: 'docSidebar', sidebarId: 'guide', position: 'left', label: 'Guide'},
        {type: 'docSidebar', sidebarId: 'plugins', docsPluginId: 'plugins', position: 'left', label: 'Plugins'},
        {type: 'docSidebar', sidebarId: 'developers', docsPluginId: 'developers', position: 'left', label: 'Developers'},
        {href: github, label: 'GitHub', position: 'right'},
      ],
    },
    footer: {
      style: 'dark',
      links: [
        {
          title: 'Guide',
          items: [
            {label: 'Introduction', to: '/guide'},
            {label: 'Install and run', to: '/guide/install'},
            {label: 'The editor', to: '/guide/editor'},
            {label: 'Keyboard shortcuts', to: '/guide/keyboard-shortcuts'},
          ],
        },
        {
          title: 'Plugins',
          items: [
            {label: 'Plugins', to: '/plugins'},
            {label: 'Create a plugin', to: '/plugins/create-a-plugin'},
            {label: 'C# support', to: '/plugins/csharp'},
            {label: 'Java support', to: '/plugins/java'},
          ],
        },
        {
          title: 'More',
          items: [
            {label: 'Building and testing', to: '/developers/building'},
            {label: 'Contributing', to: '/developers/contributing'},
            {label: 'GitHub', href: github},
          ],
        },
      ],
      copyright: `Copyright ${new Date().getFullYear()} Dylan de Beer. Built with Docusaurus.`,
    },
    prism: {
      theme: prismThemes.oneLight,
      darkTheme: prismThemes.oneDark,
      additionalLanguages: ['csharp', 'bash', 'json'],
    },
  } satisfies Preset.ThemeConfig,
};

export default config;
