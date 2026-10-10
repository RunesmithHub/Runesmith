using Runesmith.Sdk.Plugins;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Composition.Tests;

public sealed class ApiCompatibilityTests : IDisposable
{
    private readonly TestPluginFolder folder = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public void TheApiIsVersionZeroPointOnePointTwoAndRunsPluginsBuiltForZeroPointOne()
    {
        Assert.Equal(new Version(0, 1, 2), RunesmithApi.Version);
        Assert.Equal(new Version(0, 1, 0), RunesmithApi.OldestSupported);
        Assert.Equal("0.1.2", PluginDiscovery.ApiVersion.ToString());
    }

    [Theory]
    [InlineData("^0.1.0")]
    [InlineData("^0.1.1")]
    [InlineData("^0.1.2")]
    public void APluginBuiltForAnApiOfThisLineUpToThisOneLoads(string range)
    {
        folder.Add(Greeters.Greeter with { RunesmithApi = range });

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Loaded, plugin.State);
    }

    [Fact]
    public void APluginBuiltForANewerPatchOfTheApiDoesNotLoad()
    {
        folder.Add(Greeters.Greeter with { RunesmithApi = "^0.1.3" });

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains("this Runesmith has API 0.1.2", plugin.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("^0.4.0", true)]
    [InlineData("^0.4.2", true)]
    [InlineData(">=0.4.1 <0.4.5", true)]
    [InlineData("^0.4.3", false)]
    [InlineData("^0.5.0", false)]
    [InlineData("^0.3.0", false)]
    [InlineData(">=0.3.0 <0.5.0", false)]
    public void APluginRunsWhenTheApiIsInItsRangeAndItWasBuiltForTheCurrentLine(string range, bool compatible) =>
        Assert.Equal(compatible, PluginDiscovery.IsApiCompatible(VersionRange.Parse(range), SemanticVersion.Parse("0.4.2"), SemanticVersion.Parse("0.4.0")));

    [Theory]
    [InlineData(0, 1, true)]
    [InlineData(0, 0, false)]
    [InlineData(0, 2, false)]
    [InlineData(1, 0, false)]
    public void AnOlderManifestRunsWhenItsApiVersionIsInTheCurrentLine(int major, int minor, bool compatible) =>
        Assert.Equal(compatible, RunesmithApi.IsCompatible(new Version(major, minor)));

    [Fact]
    public void APluginForAnotherApiLineDoesNotLoad()
    {
        folder.Add(Greeters.Greeter with { RunesmithApi = "^0.2.0" });

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains("this Runesmith has API 0.1.2", plugin.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void APluginBuiltForAnOlderApiThanTheOldestSupportedDoesNotLoad()
    {
        folder.Add(Greeters.Greeter with { RunesmithApi = ">=0.0.1 <0.2.0" });

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains("built for plugin API 0.0.1", plugin.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOlderManifestForAnotherApiDoesNotLoad()
    {
        folder.AddLegacy("acme.todo", "Acme.Todo", "namespace Acme.Todo; public sealed class Todo { }", apiVersion: "0.2");

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Failed, plugin.State);
    }
}
