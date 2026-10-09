import type {ReactElement} from 'react';
import Layout from '@theme/Layout';
import Link from '@docusaurus/Link';
import {Card, CardGrid} from '@site/src/components/CardGrid';

export default function Home(): ReactElement {
  return (
    <Layout
      title="Code editor"
      description="Documentation for Runesmith, a code editor on .NET 10 and Avalonia with docking, language servers and plugins.">
      <main className="container margin-vert--xl">
        <h1>Runesmith</h1>
        <p className="hero__subtitle">
          A code editor built on .NET 10 and Avalonia: C# editing with completion, hover and problems from a language server, a docking
          workspace with floating windows, light and dark themes, and plugins for everything else.
        </p>
        <div className="margin-vert--lg">
          <Link className="button button--primary button--lg" to="/guide/install">
            Get started
          </Link>
        </div>
        <CardGrid columns={3}>
          <Card title="The editor" icon="code" to="/guide/editor">
            Highlighting, completion, signature help, find and replace, and typing aids for code.
          </Card>
          <Card title="Keyboard shortcuts" icon="keyboard" to="/guide/keyboard-shortcuts">
            Every command has a key, and every key can be changed.
          </Card>
          <Card title="Plugins" icon="plug" to="/plugins">
            Add languages, language servers, commands, tool windows and build providers.
          </Card>
        </CardGrid>
      </main>
    </Layout>
  );
}
