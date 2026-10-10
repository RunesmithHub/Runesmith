using System.Collections.Concurrent;
using System.Composition;
using Runesmith.Sdk.Appearance;
using Runesmith.Sdk.Languages;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars.Reader;
using TextMateSharp.Internal.Themes.Reader;
using TextMateSharp.Internal.Types;
using TextMateSharp.Registry;
using TextMateSharp.Themes;
using Registry = TextMateSharp.Registry.Registry;
using SdkGrammarDefinition = Runesmith.Sdk.Languages.GrammarDefinition;

namespace Runesmith.Languages;

/// <summary>The TextMate grammars Runesmith ships and the ones plugins add, loaded once and shared by every highlighter.</summary>
/// <remarks>Each color scheme has its own registry, because a grammar bakes the scheme's colors into the tokens it produces.</remarks>
[Export]
[Shared]
public sealed class TextMateGrammars
{
    private readonly ConcurrentDictionary<object, Lazy<ThemedRegistry>> registries = new();
    private readonly GrammarLocator locator;

    [ImportingConstructor]
    public TextMateGrammars([ImportMany] IEnumerable<SdkGrammarDefinition> pluginGrammars)
    {
        ArgumentNullException.ThrowIfNull(pluginGrammars);
        BuiltIn = new RegistryOptions(ThemeName.DarkPlus);
        locator = new GrammarLocator(BuiltIn, pluginGrammars.ToDictionary(g => g.ScopeName, g => g.FilePath, StringComparer.Ordinal));
    }

    /// <summary>Gets the grammars and language configurations TextMateSharp ships.</summary>
    internal RegistryOptions BuiltIn { get; }

    /// <summary>Gets the registry that colors tokens with a scheme's TextMate theme, read from its JSON or its file.</summary>
    /// <exception cref="InvalidDataException">The theme cannot be read as a TextMate theme; asking again throws again.</exception>
    internal ThemedRegistry GetRegistry(ColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        object key = scheme.Json.IsEmpty ? scheme.FilePath : scheme.Json;
        return registries.GetOrAdd(key, _ => new Lazy<ThemedRegistry>(() => new ThemedRegistry(locator, ReadTheme(scheme)))).Value;
    }

    private static IRawTheme ReadTheme(ColorScheme scheme)
    {
        try
        {
            using var reader = scheme.Json.IsEmpty ? new StreamReader(scheme.FilePath) : new StreamReader(new MemoryStream(scheme.Json.ToArray(), writable: false));
            var theme = ThemeReader.ReadThemeSync(reader);
            if (theme?.GetTokenColors() is not { Count: > 0 } && theme?.GetSettings() is not { Count: > 0 })
                throw new InvalidDataException("It has no token colors.");
            return theme;
        }
        catch (Exception exception)
        {
            var source = scheme.Json.IsEmpty ? Path.GetFileName(scheme.FilePath) : "Its JSON";
            throw new InvalidDataException($"{source} is not a TextMate theme Runesmith can read: {exception.Message}", exception);
        }
    }

    private sealed class GrammarLocator(RegistryOptions builtIn, Dictionary<string, string> pluginGrammars) : IRegistryOptions
    {
        public IRawGrammar GetGrammar(string scopeName)
        {
            if (!pluginGrammars.TryGetValue(scopeName, out var path))
                return builtIn.GetGrammar(scopeName);

            using var reader = new StreamReader(path);
            return GrammarReader.ReadGrammarSync(reader);
        }

        public ICollection<string> GetInjections(string scopeName) => builtIn.GetInjections(scopeName);

        public IRawTheme GetTheme(string scopeName) => builtIn.GetTheme(scopeName);

        public IRawTheme GetDefaultTheme() => builtIn.GetDefaultTheme();
    }
}

/// <summary>A TextMate registry colored with one theme. Loading grammars and tokenizing are not thread-safe, so callers lock the grammar.</summary>
internal sealed class ThemedRegistry
{
    private readonly Registry registry;
    private readonly Dictionary<string, IGrammar?> grammars = new(StringComparer.Ordinal);

    public ThemedRegistry(IRegistryOptions locator, IRawTheme theme)
    {
        registry = new Registry(locator);
        registry.SetTheme(theme);
        Theme = registry.GetTheme();
        DefaultForeground = Theme.GetColor(1);
    }

    public Theme Theme { get; }

    /// <summary>Gets the theme's text color; tokens in it are left to the editor's own text color.</summary>
    public string DefaultForeground { get; }

    /// <summary>Gets the grammar of a scope, or null when there is none or it fails to load.</summary>
    public IGrammar? GetGrammar(string scopeName)
    {
        lock (grammars)
        {
            if (grammars.TryGetValue(scopeName, out var grammar))
                return grammar;

            try
            {
                grammar = registry.LoadGrammar(scopeName);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or TextMateSharp.TMException or FormatException or UnauthorizedAccessException)
            {
                grammar = null;
            }

            grammars[scopeName] = grammar;
            return grammar;
        }
    }
}
