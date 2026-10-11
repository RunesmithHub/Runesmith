namespace Runesmith.Composition.Tests;

public sealed class LocalizationManifestTests : IDisposable
{
    private static readonly TestPlugin Dutch = new("tests.dutch", "Tests.Dutch") { LanguagePack = ("nl", "Nederlands", "Dutch") };

    private readonly TestPluginFolder folder = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public void APluginListsTheLanguagesOfItsOwnTranslations()
    {
        var path = folder.Add(Greeters.Greeter with { Languages = ["en", "nl", "pt-BR"] });

        var manifest = PluginManifest.Read(Path.Combine(path, PluginManifest.FileName));

        Assert.Equal(["en", "nl", "pt-BR"], manifest.Languages);
        Assert.False(manifest.IsLanguagePack);
    }

    [Fact]
    public void APluginWithoutLanguagesIsNotLocalized()
    {
        var path = folder.Add(Greeters.Greeter);

        var manifest = PluginManifest.Read(Path.Combine(path, PluginManifest.FileName));

        Assert.Null(manifest.Languages);
        Assert.Null(manifest.LanguagePack);
    }

    [Fact]
    public void ALanguagePackNamesItsLanguage()
    {
        var path = folder.Add(Dutch);

        var manifest = PluginManifest.Read(Path.Combine(path, PluginManifest.FileName));

        Assert.True(manifest.IsLanguagePack);
        Assert.Equal(new PluginLanguagePack("nl", "Nederlands", "Dutch"), manifest.LanguagePack);
    }

    [Theory]
    [InlineData(new[] { "nl" }, "must include en")]
    [InlineData(new[] { "en", "NL" }, "must be written nl")]
    [InlineData(new[] { "en", "not a tag" }, "not a BCP 47 language tag")]
    [InlineData(new[] { "en", "nl", "nl" }, "lists nl again")]
    public void AWrongLanguageListFailsThePlugin(string[] languages, string reason)
    {
        folder.Add(Greeters.Greeter with { Languages = languages });

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains(reason, plugin.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ALanguagePackThatDeclaresCapabilitiesFails()
    {
        folder.Add(Dutch with { Capabilities = ["network"] });

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains("declares no capabilities", plugin.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ALanguagePackThatAlsoListsLanguagesFails()
    {
        folder.Add(Dutch with { Languages = ["en", "nl"] });

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains("lists no languages", plugin.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ALanguagePackWithAnEmptyNameFails()
    {
        folder.Add(Dutch with { LanguagePack = ("nl", " ", "Dutch") });

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains("languagePack.name", plugin.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ALanguagePackCountsAsLoadedWithoutItsCodeLoading()
    {
        folder.Add(Dutch with { Implementation = "namespace Tests.Dutch; public sealed class Marker { }" });
        var plugins = folder.Discover();

        var loaded = new PluginLoader(plugins).Load();

        Assert.Empty(loaded);
        Assert.Equal(PluginState.Loaded, Assert.Single(plugins).State);
        Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), assembly => assembly.GetName().Name == "Tests.Dutch");
    }

    [Fact]
    public void APluginCannotDependOnALanguagePack()
    {
        folder.Add(Dutch);
        folder.Add(Greeters.Greeter with { Dependencies = [("tests.dutch", "^1.0.0")] });
        var plugins = folder.Discover();

        new PluginLoader(plugins).Load();

        var greeter = plugins.Single(p => p.Manifest.Id == "tests.greeter");
        Assert.Equal(PluginState.Failed, greeter.State);
        Assert.Equal("It needs Tests.Dutch, which is a language pack and has no contracts for other plugins.", greeter.Error);
    }
}
