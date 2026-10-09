namespace Runesmith.Composition.Tests;

public sealed class DependencyTests : IDisposable
{
    private readonly TestPluginFolder folder = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public void APluginLoadsAfterThePluginsItDependsOn()
    {
        var zeta = new TestPlugin("tests.zeta", "Tests.Zeta") { Contracts = "namespace Tests.Zeta; public interface IZeta { }" };
        folder.Add(zeta);
        folder.Add(new TestPlugin("tests.alpha", "Tests.Alpha") { Dependencies = [("tests.zeta", "^1.0.0")], BuiltAgainst = [zeta] });
        var plugins = folder.Discover();

        var loaded = new PluginLoader(plugins).Load();

        Assert.Equal(["Tests.Zeta", "Tests.Alpha"], loaded.Select(l => l.Assembly.GetName().Name));
    }

    [Fact]
    public void APluginWhoseDependencyIsMissingDoesNotLoad()
    {
        folder.Add(Greeters.Greeter);
        folder.Add(Greeters.Welcome);
        Directory.Delete(Path.Combine(folder.PluginsPath, "tests.greeter"), recursive: true);

        var welcome = LoadAll().Single();

        Assert.Equal(PluginState.Failed, welcome.State);
        Assert.Equal("It needs tests.greeter >=1.0.0 <2.0.0, which is not installed.", welcome.Error);
    }

    [Fact]
    public void APluginWhoseDependencyIsTurnedOffDoesNotLoad()
    {
        folder.Add(Greeters.Greeter);
        folder.Add(Greeters.Welcome);

        var plugins = LoadAll("tests.greeter");

        Assert.Equal(PluginState.Disabled, plugins.Single(p => p.Manifest.Id == "tests.greeter").State);
        var welcome = plugins.Single(p => p.Manifest.Id == "tests.welcome");
        Assert.Equal(PluginState.Failed, welcome.State);
        Assert.Equal("It needs Tests.Greeter, which is turned off.", welcome.Error);
    }

    [Fact]
    public void APluginWhoseDependencyFailedDoesNotLoad()
    {
        folder.Add(Greeters.Greeter with { RunesmithApi = "^0.2.0" });
        folder.Add(Greeters.Welcome);

        var welcome = LoadAll().Single(p => p.Manifest.Id == "tests.welcome");

        Assert.Equal(PluginState.Failed, welcome.State);
        Assert.Equal("It needs Tests.Greeter, which could not load.", welcome.Error);
    }

    [Fact]
    public void APluginWhoseDependencyHasAVersionOutsideItsRangeDoesNotLoad()
    {
        folder.Add(Greeters.Greeter);
        folder.Add(Greeters.Welcome with { Dependencies = [("tests.greeter", "^2.0.0")] });

        var welcome = LoadAll().Single(p => p.Manifest.Id == "tests.welcome");

        Assert.Equal(PluginState.Failed, welcome.State);
        Assert.Equal("It needs Tests.Greeter >=2.0.0 <3.0.0, and version 1.0.0 is installed.", welcome.Error);
    }

    [Fact]
    public void APluginCannotDependOnAnOlderManifest()
    {
        folder.AddLegacy("tests.greeter", "Tests.Greeter", "namespace Tests.Greeter; public sealed class Greeter { }");
        folder.Add(new TestPlugin("tests.welcome", "Tests.Welcome") { Dependencies = [("tests.greeter", ">=1.0.0")] });

        var welcome = LoadAll().Single(p => p.Manifest.Id == "tests.welcome");

        Assert.Equal(PluginState.Failed, welcome.State);
        Assert.Contains("no contracts", welcome.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void PluginsThatDependOnEachOtherDoNotLoad()
    {
        folder.Add(new TestPlugin("tests.one", "Tests.One") { Dependencies = [("tests.two", "^1.0.0")] });
        folder.Add(new TestPlugin("tests.two", "Tests.Two") { Dependencies = [("tests.one", "^1.0.0")] });

        var plugins = LoadAll();

        Assert.All(plugins, plugin =>
        {
            Assert.Equal(PluginState.Failed, plugin.State);
            Assert.Contains("cycle", plugin.Error, StringComparison.Ordinal);
        });
    }

    private List<PluginInfo> LoadAll(params string[] disabled)
    {
        var plugins = folder.Discover(disabled);
        new PluginLoader(plugins).Load();
        return plugins;
    }
}
