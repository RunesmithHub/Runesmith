using System.Collections.Concurrent;
using System.Composition;
using Runesmith.Sdk.Appearance;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;

namespace Runesmith.Languages.Highlighting;

/// <summary>Creates TextMate highlighters with the semantic tokens of the language's providers over them, unless the
/// <see cref="SettingKeys.SemanticHighlighting"/> setting turns those off, and restyles every open one when the syntax color scheme changes.</summary>
/// <remarks>A highlighter follows its document's language, so it is created for every document, including plain text, which it leaves
/// unhighlighted.</remarks>
[Export(typeof(ISyntaxHighlighterProvider))]
[Shared]
public sealed class TextMateHighlighterProvider : ISyntaxHighlighterProvider, IDisposable
{
    private readonly ConcurrentDictionary<TextMateHighlighter, byte> highlighters = new();
    private readonly ConcurrentDictionary<SemanticHighlighter, byte> semanticHighlighters = new();
    private readonly ConcurrentDictionary<ThemedRegistry, StyleTable> styles = new();
    private readonly ConcurrentDictionary<ThemedRegistry, SemanticTokenStyles> semanticStyles = new();
    private readonly SyntaxColors colors;
    private readonly Lazy<ISyntaxFeatures>? features;
    private readonly ISettingsService? settings;

    [ImportingConstructor]
    public TextMateHighlighterProvider(TextMateGrammars grammars, ILanguageRegistry registry, SyntaxColors colors,
        [Import(AllowDefault = true)] Lazy<ISyntaxFeatures>? features = null, [Import(AllowDefault = true)] ISettingsService? settings = null)
    {
        ArgumentNullException.ThrowIfNull(colors);
        Grammars = grammars;
        Registry = registry;
        this.colors = colors;
        this.features = features;
        this.settings = settings;
        colors.Changed += OnSchemeChanged;
        if (settings is not null)
            settings.Changed += OnSettingChanged;
    }

    internal TextMateGrammars Grammars { get; }

    internal ILanguageRegistry Registry { get; }

    internal ColorScheme Scheme => colors.Current;

    public ISyntaxHighlighter? Create(IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var highlighter = new TextMateHighlighter(document, this);
        highlighters[highlighter] = 0;
        if (features is null)
            return highlighter;

        var semantic = new SemanticHighlighter(highlighter, document, features.Value, SemanticStyles, Remove) { IsEnabled = IsSemanticHighlightingOn };
        semanticHighlighters[semantic] = 0;
        return semantic;
    }

    public void Dispose()
    {
        colors.Changed -= OnSchemeChanged;
        if (settings is not null)
            settings.Changed -= OnSettingChanged;
        foreach (var highlighter in semanticHighlighters.Keys)
            highlighter.Dispose();
        foreach (var highlighter in highlighters.Keys)
            highlighter.Dispose();
    }

    internal StyleTable GetStyles(ThemedRegistry registry) => styles.GetOrAdd(registry, r => new StyleTable(r.Theme, r.DefaultForeground));

    internal void Remove(TextMateHighlighter highlighter) => highlighters.TryRemove(highlighter, out _);

    internal void Remove(SemanticHighlighter highlighter) => semanticHighlighters.TryRemove(highlighter, out _);

    // The scheme was checked when it was chosen, so its registry reads.
    private SemanticTokenStyles? SemanticStyles()
    {
        try
        {
            var registry = Grammars.GetRegistry(Scheme);
            return semanticStyles.GetOrAdd(registry, r => new SemanticTokenStyles(r.Theme, GetStyles(r), r.DefaultForeground));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private bool IsSemanticHighlightingOn => settings?.Get<bool>(SettingKeys.SemanticHighlighting) ?? true;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key != SettingKeys.SemanticHighlighting)
            return;

        var on = IsSemanticHighlightingOn;
        foreach (var highlighter in semanticHighlighters.Keys)
            highlighter.IsEnabled = on;
    }

    private void OnSchemeChanged(object? sender, EventArgs e)
    {
        foreach (var highlighter in highlighters.Keys)
            highlighter.Reset();
        foreach (var highlighter in semanticHighlighters.Keys)
            Avalonia.Threading.Dispatcher.UIThread.Post(highlighter.Restyle);
    }
}
