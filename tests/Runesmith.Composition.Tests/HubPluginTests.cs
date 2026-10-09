namespace Runesmith.Composition.Tests;

public sealed class HubPluginTests : IDisposable
{
    private readonly TestPluginFolder bundled = new();
    private readonly TestPluginFolder user = new();

    public void Dispose()
    {
        bundled.Dispose();
        user.Dispose();
    }

    [Fact]
    public void ANewerCopyFromTheHubRunsInsteadOfTheBundledOne()
    {
        bundled.Add(Greeters.Greeter);
        user.Add(Greeters.Greeter with { Version = "1.2.0" });

        var plugins = Discover(hub: ["tests.greeter"]);

        Assert.Equal(PluginState.Replaced, plugins[0].State);
        Assert.Equal("Version 1.2.0 from the hub runs instead.", plugins[0].Error);
        Assert.Equal((PluginState.Loaded, PluginSource.Hub, "1.2.0"), (plugins[1].State, plugins[1].Source, plugins[1].Manifest.Version));
        Assert.Equal("Tests.Greeter", Assert.Single(new PluginLoader(plugins).Load()).Assembly.GetName().Name);
    }

    [Fact]
    public void TheBundledCopyStaysWhenTheHubCopyIsNotNewer()
    {
        bundled.Add(Greeters.Greeter with { Version = "1.5.0" });
        user.Add(Greeters.Greeter with { Version = "1.2.0" });

        var plugins = Discover(hub: ["tests.greeter"]);

        Assert.Equal(PluginState.Loaded, plugins[0].State);
        Assert.Equal(PluginState.Replaced, plugins[1].State);
        Assert.Equal("Runesmith ships version 1.5.0, which runs instead.", plugins[1].Error);
    }

    [Fact]
    public void TheBundledCopyStaysWhenTheHubCopyCannotLoad()
    {
        bundled.Add(Greeters.Greeter);
        var copy = user.Add(Greeters.Greeter with { Version = "1.2.0" });
        File.WriteAllBytes(Path.Combine(copy, "lib", "Tests.Greeter.dll"), [0x4D, 0x5A, 1, 2, 3]);

        var plugins = Discover(hub: ["tests.greeter"]);

        Assert.Equal(PluginState.Loaded, plugins[0].State);
        Assert.Equal(PluginState.Replaced, plugins[1].State);
        Assert.Equal("Version 1.2.0 from the hub could not load: lib/Tests.Greeter.dll is not a .NET assembly. Runesmith ships version 1.0.0, which runs instead.", plugins[1].Error);
    }

    [Fact]
    public void ALocalCopyOfABundledPluginIsStillADuplicate()
    {
        bundled.Add(Greeters.Greeter);
        user.Add(Greeters.Greeter with { Version = "2.0.0" });

        var plugins = Discover(hub: []);

        Assert.Equal(PluginState.Loaded, plugins[0].State);
        Assert.Equal((PluginState.Failed, PluginSource.Local), (plugins[1].State, plugins[1].Source));
    }

    [Fact]
    public void AWithheldPluginDoesNotLoadAndSaysWhy()
    {
        user.Add(Greeters.Greeter);
        user.Add(Greeters.Welcome);

        var plugins = Discover(hub: ["tests.greeter", "tests.welcome"], withheld: new Dictionary<string, string> { ["tests.greeter"] = "The hub blocked it." });
        new PluginLoader(plugins).Load();

        var greeter = plugins.Single(p => p.Manifest.Id == "tests.greeter");
        Assert.Equal((PluginState.Disabled, "The hub blocked it."), (greeter.State, greeter.Error));
        Assert.Equal(PluginState.Failed, plugins.Single(p => p.Manifest.Id == "tests.welcome").State);
    }

    private List<PluginInfo> Discover(string[] hub, Dictionary<string, string>? withheld = null) =>
        [.. PluginDiscovery.Discover([(bundled.PluginsPath, PluginSource.Bundled), (user.PluginsPath, PluginSource.Local)], new HashSet<string>(), hub.ToHashSet(), withheld)];
}
