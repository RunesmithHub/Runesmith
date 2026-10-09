# Writing style

How pages on this site are written. `npm run lint:prose` checks the mechanical rules; the rest is up to you and the reviewer.

## Voice and tone

- Write to the reader as "you". Say what to do and what happens: "Press Ctrl+P and type part of a file name. The list shows the files
  that match."
- Plain and direct. Short sentences, common words, one idea per sentence where you can.
- Describe; do not sell. No adjectives that only praise ("powerful", "blazing fast", "beautiful"). If something is fast, give the number.
- Be exact. Name the type, the property, the class, the key. Use the labels Runesmith shows, in bold: **Close other tabs**,
  **Reset layout**.
- Present tense, active voice. "The host creates the panel's content", not "the content will be created".
- Explain why only when the reader needs it to make a choice.
- No filler openings ("In this page we will…", "Let's…") and no summaries that repeat the page.

## Punctuation and characters

The lint rejects these in `content/` and `src/pages/`:

- The em dash. Use a comma, a colon, parentheses or two sentences.
- The en dash as a dash. It is only allowed between numbers in a range, such as 1–5.
- ` -- ` as a dash.
- Emoji.
- Stock phrases: delve, seamless, leverage, robust, it's worth noting, in conclusion, unlock the power, elevate, game-changer,
  dive into, deep dive, in today's, whether you're, empower, supercharge, cutting-edge, effortless, harness the, look no further.

Code blocks and inline code are not checked.

Use › between menu levels (**View › Reset Layout**), × for sizes (24 × 24), and … only where a control's own label has it.

## Icons

Icons appear where the layout uses them: cards in a `CardGrid` and the homepage. Never put icons or emoji in headings or running text.

## Headings

- Sentence case: "Save and restore layouts", not "Save And Restore Layouts".
- The page title comes from the front matter; start the body with a paragraph, not a heading.
- Use `##` for sections and `###` for subsections. Do not go deeper.
- Headings name the task or the thing ("Open a folder", "The status bar"), not a question. A section about one panel is named after
  the panel.

## Structure of a page

1. **What it is.** The opening paragraph says what the feature is and what the page covers, in two to four sentences. It is shown
   larger than body text, so keep it tight.
2. **How to use it.** Numbered steps (`<Steps>`) for a task, or an example in C# or JSON.
3. **Reference.** The properties, members or classes, as a table, with defaults where they matter.
4. **Behaviour.** What the feature does on its own: keys, pointer gestures, edge cases. A table works for gestures.
5. **Related.** A short list or `CardGrid` of links to related pages, when there are natural next steps.

Keep pages focused. If a page grows past one topic, split it and add the new page to the sidebar.

## Front matter

Every page has:

```yaml
---
title: Docking
description: One sentence for search results and link previews, ending with a period.
---
```

Add `sidebar_label` only when the sidebar needs a shorter title.

## Code samples

- C# follows the repository's style: file-scoped namespaces, four-space indentation, `var` where the type is obvious, expression
  bodies for one-liners, `_camelCase` private fields, braces on their own lines.
- Samples must compile against the current API. Check every type and member against `src/Runesmith.Sdk`, and prefer adapting code that
  already runs, such as the C# plugin in `plugins/Runesmith.CSharp` or the plugin template.
- Every setting, command, key binding and path a page names must exist in the code. Take them from the source, not from memory.
- Show complete files for setup, with a `title="plugin.json"` on the code block. Shorter snippets are fine elsewhere if the surrounding code
  is obvious.
- Use `csharp`, `xml`, `json` and `bash` as languages. Shell commands are bash and run from the repository root unless the text says
  otherwise.
- Comments in code samples follow the repository rule: only what the code cannot say, one line.

## Linking

- Within a section, link to the file with a relative path: `[Settings](./settings.mdx)`.
- Across sections, link to the route: `[Create a plugin](/plugins/create-a-plugin)`.
- Link the first mention of a concept that has its own page, not every mention.
- Link text says where it goes ("see [Theming](./theming.mdx)"), never "click here".
- External links only to stable, official pages (.NET, Avalonia, Docusaurus).

## Terms

| Use | Not |
| --- | --- |
| panel (a tool window, in the plugin API) | pane |
| tab group, group | tab well, dock area |
| floating window | undocked window, tear-off |
| the theme | the skin |
| accent color | primary color, brand color |
| light and dark | light mode and dark mode, in running text |
| class (a style class) | style name |
| Windows, macOS and Linux | Win, Mac |
