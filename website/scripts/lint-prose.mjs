// Fails on punctuation, emoji and phrases that STYLE.md rules out. Code blocks and inline code are not checked.
import {readdirSync, readFileSync} from 'node:fs';
import {extname, join, relative} from 'node:path';

const site = join(import.meta.dirname, '..');
const roots = ['content', 'src/pages'];
const extensions = new Set(['.md', '.mdx', '.ts', '.tsx', '.json']);

const phrases = [
  'delve',
  'seamless',
  'seamlessly',
  'leverage',
  'leverages',
  'leveraging',
  'robust',
  "it's worth noting",
  'it is worth noting',
  'in conclusion',
  'unlock the power',
  'unlock your',
  'unlocks the',
  'elevate',
  'game-changer',
  'game changer',
  'dive into',
  'deep dive',
  "in today's",
  "whether you're",
  'whether you are a',
  'empower',
  'supercharge',
  'cutting-edge',
  'effortless',
  'effortlessly',
  'harness the',
  'look no further',
];

const rules = [
  {pattern: /—/g, message: 'em dash; use a comma, colon, parentheses or two sentences'},
  {pattern: /(?<!\d)–|–(?!\d)/g, message: 'en dash used as a dash; only number ranges such as 1–5 may use it'},
  {pattern: / -- /g, message: '" -- " used as a dash'},
  {pattern: /(?![©®™←-⇿])\p{Extended_Pictographic}/gu, message: 'emoji'},
  {pattern: new RegExp(`\\b(${phrases.map((p) => p.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')).join('|')})\\b`, 'gi'), message: 'phrase on the STYLE.md list'},
];

function* files(folder) {
  for (const entry of readdirSync(folder, {withFileTypes: true})) {
    const path = join(folder, entry.name);
    if (entry.isDirectory()) yield* files(path);
    else if (extensions.has(extname(entry.name))) yield path;
  }
}

/** Blanks code so line and column numbers stay correct. */
function withoutCode(text, extension) {
  const blank = (match) => match.replace(/[^\n]/g, ' ');
  if (extension !== '.md' && extension !== '.mdx') return text;
  return text.replace(/^(```|~~~)[^\n]*\n[\s\S]*?^\1[^\n]*$/gm, blank).replace(/`[^`\n]+`/g, blank);
}

const problems = [];
for (const root of roots) {
  for (const file of files(join(site, root))) {
    const lines = withoutCode(readFileSync(file, 'utf8'), extname(file)).split('\n');
    lines.forEach((line, index) => {
      for (const {pattern, message} of rules) {
        for (const match of line.matchAll(pattern)) {
          problems.push(`${relative(site, file)}:${index + 1}:${match.index + 1}  ${message}: "${match[0]}"`);
        }
      }
    });
  }
}

if (problems.length > 0) {
  console.error(problems.join('\n'));
  console.error(`\n${problems.length} prose problem(s). See STYLE.md.`);
  process.exit(1);
}
console.log('Prose lint passed.');
