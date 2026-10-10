using System.Text;
using Runesmith.Languages.Highlighting;
using Runesmith.Sdk.Appearance;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars;
using TextMateSharp.Themes;

namespace Runesmith.Languages.Tests.Highlighting;

public sealed class SyntaxColorsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "runesmith-scheme-tests", Guid.NewGuid().ToString("N"));

    public SyntaxColorsTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void ShipsRunesmithsSchemesAsFilesTheyAddLikeAPlugin()
    {
        var colors = new SyntaxColors(new TextMateGrammars([]));

        Assert.Equal(
            [BuiltInColorSchemes.DarkId, BuiltInColorSchemes.LightId, BuiltInColorSchemes.LowContrastDarkId, BuiltInColorSchemes.LowContrastLightId],
            new BuiltInColorSchemes().Schemes.Select(s => s.Id));
        Assert.All(new BuiltInColorSchemes().Schemes, s => Assert.Null(colors.Check(s)));
        Assert.Same(BuiltInColorSchemes.Dark, colors.Current);
    }

    [Fact]
    public void UsesAPluginsSchemeAndTellsTheHighlightersOnce()
    {
        var colors = new SyntaxColors(new TextMateGrammars([]));
        var scheme = new ColorScheme("ember.dusk", "Ember Dusk", Write("dusk.json", """
            { "name": "Dusk", "tokenColors": [ { "scope": "keyword", "settings": { "foreground": "#FF8800" } } ] }
            """));
        var changes = 0;
        colors.Changed += (_, _) => changes++;

        Assert.Null(colors.Use(scheme));
        Assert.Null(colors.Use(scheme));

        Assert.Same(scheme, colors.Current);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "name": "Empty" }""")]
    public void KeepsTheCurrentSchemeWhenAFileIsNotATextMateTheme(string content)
    {
        var colors = new SyntaxColors(new TextMateGrammars([]));
        var scheme = new ColorScheme("broken", "Broken", Write("broken.json", content));

        var problem = colors.Use(scheme);

        Assert.Contains("broken.json", problem, StringComparison.Ordinal);
        Assert.Same(BuiltInColorSchemes.Dark, colors.Current);
        Assert.NotNull(colors.Check(new ColorScheme("gone", "Gone", Path.Combine(folder, "gone.json"))));
    }

    [Fact]
    public void ColorsTokensFromASchemesJsonLikeFromItsFile()
    {
        const string theme = """
            { "name": "Dusk", "tokenColors": [ { "scope": "keyword", "settings": { "foreground": "#FF8800", "fontStyle": "bold" } } ] }
            """;
        var grammars = new TextMateGrammars([]);
        var colors = new SyntaxColors(grammars);
        var fromFile = new ColorScheme("ember.file", "Ember File", Write("dusk.json", theme));
        var fromJson = new ColorScheme("ember.json", "Ember Json", Encoding.UTF8.GetBytes(theme));

        Assert.Null(colors.Use(fromJson));

        Assert.Same(fromJson, colors.Current);
        Assert.Equal("", fromJson.FilePath);
        Assert.True(fromFile.Json.IsEmpty);
        var styles = Styles(grammars, fromJson, "return x;");
        Assert.Contains(("#FF8800", FontStyle.Bold), styles);
        Assert.Equal(Styles(grammars, fromFile, "return x;"), styles);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "name": "Empty" }""")]
    public void KeepsTheCurrentSchemeWhenItsJsonIsNotATextMateTheme(string content)
    {
        var colors = new SyntaxColors(new TextMateGrammars([]));

        var problem = colors.Use(new ColorScheme("broken", "Broken", Encoding.UTF8.GetBytes(content)));

        Assert.Contains("Its JSON is not a TextMate theme", problem, StringComparison.Ordinal);
        Assert.Same(BuiltInColorSchemes.Dark, colors.Current);
    }

    private static List<(string? Color, FontStyle Style)> Styles(TextMateGrammars grammars, ColorScheme scheme, string line)
    {
        var registry = grammars.GetRegistry(scheme);
        var grammar = Assert.IsAssignableFrom<IGrammar>(registry.GetGrammar("source.cs"));
        var tokens = grammar.TokenizeLine2(new LineText(line), null, TimeSpan.FromSeconds(10)).Tokens;
        return [.. Enumerable.Range(0, tokens.Length / 2).Select(i => tokens[(2 * i) + 1])
            .Select(m => (registry.Theme.GetColor(EncodedTokenAttributes.GetForeground(m)), EncodedTokenAttributes.GetFontStyle(m)))];
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }
}
