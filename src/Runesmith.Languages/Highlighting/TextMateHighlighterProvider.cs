using System.Collections.Concurrent;
using System.Composition;
using Runesmith.Sdk.Appearance;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;

namespace Runesmith.Languages.Highlighting;

/// <summary>Creates TextMate highlighters, and restyles every open one when the syntax color scheme changes.</summary>
/// <remarks>A highlighter follows its document's language, so it is created for every document, including plain text, which it leaves
/// unhighlighted.</remarks>
[Export(typeof(ISyntaxHighlighterProvider))]
[Shared]
public sealed class TextMateHighlighterProvider : ISyntaxHighlighterProvider, IDisposable
{
    private readonly ConcurrentDictionary<TextMateHighlighter, byte> highlighters = new();
    private readonly ConcurrentDictionary<ThemedRegistry, StyleTable> styles = new();
    private readonly SyntaxColors colors;

    [ImportingConstructor]
    public TextMateHighlighterProvider(TextMateGrammars grammars, ILanguageRegistry registry, SyntaxColors colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        Grammars = grammars;
        Registry = registry;
        this.colors = colors;
        colors.Changed += OnSchemeChanged;
    }

    internal TextMateGrammars Grammars { get; }

    internal ILanguageRegistry Registry { get; }

    internal ColorScheme Scheme => colors.Current;

    public ISyntaxHighlighter? Create(IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var highlighter = new TextMateHighlighter(document, this);
        highlighters[highlighter] = 0;
        return highlighter;
    }

    public void Dispose()
    {
        colors.Changed -= OnSchemeChanged;
        foreach (var highlighter in highlighters.Keys)
            highlighter.Dispose();
    }

    internal StyleTable GetStyles(ThemedRegistry registry) => styles.GetOrAdd(registry, r => new StyleTable(r.Theme, r.DefaultForeground));

    internal void Remove(TextMateHighlighter highlighter) => highlighters.TryRemove(highlighter, out _);

    private void OnSchemeChanged(object? sender, EventArgs e)
    {
        foreach (var highlighter in highlighters.Keys)
            highlighter.Reset();
    }
}
