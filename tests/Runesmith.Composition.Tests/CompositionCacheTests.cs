namespace Runesmith.Composition.Tests;

public sealed class CompositionCacheTests : IDisposable
{
    private readonly TestPluginFolder folder = new();
    private readonly string cache = Directory.CreateTempSubdirectory("runesmith-composition-cache-").FullName;

    public void Dispose()
    {
        folder.Dispose();
        Directory.Delete(cache, recursive: true);
    }

    [Fact]
    public async Task AnUnchangedStartUsesTheCache()
    {
        folder.Add(Greeters.Greeter);
        (await ComposeAsync()).Dispose();

        using var second = await ComposeAsync();

        Assert.True(second.IsFromCache);
    }

    [Fact]
    public async Task ChangedContractsComposeAgainEvenWhenTheImplementationIsTheSame()
    {
        var plugin = folder.Add(Greeters.Greeter);
        (await ComposeAsync()).Dispose();
        var contracts = folder.AddLibrary("Tests.Greeter.Contracts", Greeters.Greeter.Contracts + " public interface IFarewell { }");
        var installed = Path.Combine(plugin, "lib", "Tests.Greeter.Contracts.dll");
        // Windows refuses to delete or overwrite a loaded assembly, but lets it be renamed.
        File.Move(installed, installed + ".old");
        File.Copy(contracts, installed);

        using var second = await ComposeAsync();

        Assert.False(second.IsFromCache);
        Assert.Equal(PluginState.Loaded, Assert.Single(second.Plugins).State);
    }

    private Task<CompositionResult> ComposeAsync() =>
        RunesmithComposition.CreateAsync(new CompositionOptions([], [(folder.PluginsPath, PluginSource.Local)], cache), TestContext.Current.CancellationToken);
}
