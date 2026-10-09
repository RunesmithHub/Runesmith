using Runesmith.Languages.Highlighting;
using Runesmith.Sdk.Appearance;

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

        Assert.Equal([BuiltInColorSchemes.DarkId, BuiltInColorSchemes.LightId], new BuiltInColorSchemes().Schemes.Select(s => s.Id));
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

    private string Write(string name, string content)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, content);
        return path;
    }
}
