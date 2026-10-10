using System.Collections.Concurrent;
using Avalonia.Media;
using Runesmith.Sdk.Languages;
using TextMateSharp.Themes;
using FontStyle = TextMateSharp.Themes.FontStyle;

namespace Runesmith.Languages.Highlighting;

/// <summary>How a semantic token is drawn: in <see cref="Style"/>, or, when it is null, in the TextMate style struck through.</summary>
internal readonly record struct SemanticStyle(SyntaxStyle? Style);

/// <summary>Colors semantic tokens with a color scheme: each type stands for TextMate scopes, tried in order, and the first scope the scheme
/// has a rule for gives the color and font style.</summary>
/// <remarks>The mapping is documented on the Editor extensions page; keep the two in step.</remarks>
internal sealed class SemanticTokenStyles(Theme theme, StyleTable styles, string defaultForeground)
{
    /// <summary>The scopes each standard type stands for, most specific first.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> TypeScopes = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [SemanticTokenTypes.Namespace] = ["entity.name.namespace"],
        [SemanticTokenTypes.Type] = ["entity.name.type", "support.type"],
        [SemanticTokenTypes.Class] = ["entity.name.type.class", "entity.name.type"],
        [SemanticTokenTypes.Enum] = ["entity.name.type.enum", "entity.name.type"],
        [SemanticTokenTypes.Interface] = ["entity.name.type.interface", "entity.name.type"],
        [SemanticTokenTypes.Struct] = ["entity.name.type.struct", "entity.name.type"],
        [SemanticTokenTypes.TypeParameter] = ["entity.name.type.parameter", "entity.name.type"],
        [SemanticTokenTypes.Parameter] = ["variable.parameter"],
        [SemanticTokenTypes.Variable] = ["variable.other.readwrite", "variable"],
        [SemanticTokenTypes.Property] = ["variable.other.property", "variable"],
        [SemanticTokenTypes.EnumMember] = ["variable.other.enummember", "constant.other.enum"],
        [SemanticTokenTypes.Event] = ["variable.other.event", "variable.other.property"],
        [SemanticTokenTypes.Function] = ["entity.name.function"],
        [SemanticTokenTypes.Method] = ["entity.name.function.member", "entity.name.function"],
        [SemanticTokenTypes.Macro] = ["entity.name.function.preprocessor", "entity.name.function"],
        [SemanticTokenTypes.Keyword] = ["keyword"],
        [SemanticTokenTypes.Modifier] = ["storage.modifier"],
        [SemanticTokenTypes.Comment] = ["comment"],
        [SemanticTokenTypes.String] = ["string"],
        [SemanticTokenTypes.Number] = ["constant.numeric"],
        [SemanticTokenTypes.Regexp] = ["string.regexp"],
        [SemanticTokenTypes.Operator] = ["keyword.operator"],
        [SemanticTokenTypes.Decorator] = ["entity.name.function.decorator", "entity.name.function"],
        [SemanticTokenTypes.Label] = ["entity.name.label"],
    };

    private readonly ConcurrentDictionary<(string Type, SemanticTokenModifiers Modifiers), SemanticStyle?> cache = new();

    /// <summary>Gets how a token is drawn, or null when no scope of its type has a rule, so the TextMate color stays.</summary>
    public SemanticStyle? Get(string type, SemanticTokenModifiers modifiers) =>
        cache.GetOrAdd((type, modifiers & (SemanticTokenModifiers.Readonly | SemanticTokenModifiers.DefaultLibrary | SemanticTokenModifiers.Deprecated)), Resolve);

    /// <summary>Gets the scopes a token stands for, the ones its modifiers add first.</summary>
    public static IEnumerable<string> ScopesOf(string type, SemanticTokenModifiers modifiers)
    {
        var readOnly = modifiers.HasFlag(SemanticTokenModifiers.Readonly);
        var library = modifiers.HasFlag(SemanticTokenModifiers.DefaultLibrary);
        switch (type)
        {
            case SemanticTokenTypes.Variable or SemanticTokenTypes.Parameter when readOnly:
                yield return "variable.other.constant";
                break;
            case SemanticTokenTypes.Property when readOnly:
                yield return "variable.other.constant.property";
                yield return "variable.other.constant";
                break;
            case SemanticTokenTypes.Class or SemanticTokenTypes.Struct or SemanticTokenTypes.Interface or SemanticTokenTypes.Enum or SemanticTokenTypes.Type when library:
                yield return "support.class";
                yield return "support.type";
                break;
            case SemanticTokenTypes.Function or SemanticTokenTypes.Method when library:
                yield return "support.function";
                break;
        }

        foreach (var scope in TypeScopes.GetValueOrDefault(type) ?? [])
            yield return scope;
    }

    private Color DefaultColor => Color.TryParse(defaultForeground, out var color) ? color : Colors.Gray;

    /// <summary>Gets the style of plain text struck through, for deprecated symbols the scheme has no color for.</summary>
    public SyntaxStyle PlainStruck => field ??= new SyntaxStyle(DefaultColor) { IsStrikethrough = true };

    private SemanticStyle? Resolve((string Type, SemanticTokenModifiers Modifiers) key)
    {
        var deprecated = key.Modifiers.HasFlag(SemanticTokenModifiers.Deprecated);
        foreach (var scope in ScopesOf(key.Type, key.Modifiers))
        {
            var rule = theme.Match([scope]).FirstOrDefault(r => r.parentScopes is null or { Count: 0 });
            if (rule is null || rule.scopeDepth == 0)
                continue;

            var style = styles.Get(rule.foreground, rule.fontStyle == FontStyle.NotSet ? FontStyle.None : rule.fontStyle) ?? new SyntaxStyle(DefaultColor);
            return new SemanticStyle(deprecated ? style with { IsStrikethrough = true } : style);
        }

        return deprecated ? new SemanticStyle(null) : null;
    }
}
