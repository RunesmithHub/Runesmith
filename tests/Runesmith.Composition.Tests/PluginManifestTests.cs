namespace Runesmith.Composition.Tests;

public sealed class PluginManifestTests : IDisposable
{
    private readonly TestPluginFolder folder = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public void APackagedManifestNamesTheAssembliesInLibAndWhatThePluginNeeds()
    {
        folder.Add(Greeters.Greeter);
        var path = folder.Add(Greeters.Welcome with { Capabilities = ["network"], NetworkHosts = ["api.example.com"] });

        var manifest = PluginManifest.Read(Path.Combine(path, PluginManifest.FileName));

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.False(manifest.IsLegacy);
        Assert.Equal("tests.welcome", manifest.Id);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("lib/Tests.Welcome.dll", manifest.Assembly);
        Assert.Equal("lib/Tests.Welcome.Contracts.dll", manifest.ContractsAssembly);
        Assert.Equal(">=0.1.0 <0.2.0", manifest.RunesmithApi?.ToString());
        var dependency = Assert.Single(manifest.Dependencies);
        Assert.Equal("tests.greeter", dependency.Id);
        Assert.True(manifest.Allows("network"));
        Assert.False(manifest.Allows("credentials"));
        Assert.Equal(["api.example.com"], manifest.NetworkHosts);
    }

    [Fact]
    public void APackagedManifestWithoutDependenciesOrCapabilitiesHasNone()
    {
        var path = folder.Add(Greeters.Greeter);

        var manifest = PluginManifest.Read(Path.Combine(path, PluginManifest.FileName));

        Assert.Empty(manifest.Dependencies);
        Assert.Empty(manifest.Capabilities!);
        Assert.Empty(manifest.NetworkHosts);
        Assert.False(manifest.Allows("process"));
    }

    [Fact]
    public void AManifestWithoutASchemaVersionLoadsAsALocalPluginThatMayUseEverything()
    {
        folder.AddLegacy("acme.todo", "Acme.Todo", "namespace Acme.Todo; public sealed class Todo { }");

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Loaded, plugin.State);
        Assert.Equal(PluginSource.Local, plugin.Source);
        Assert.True(plugin.Manifest.IsLegacy);
        Assert.Null(plugin.Manifest.Capabilities);
        Assert.True(plugin.Manifest.Allows("credentials"));
        Assert.Equal("Acme.Todo.dll", plugin.Manifest.Assembly);
        Assert.Null(plugin.ContractsPath);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 2, "id": "acme.todo" }""", "schemaVersion")]
    [InlineData("""{ "schemaVersion": 1, "id": "acme.todo" }""", "PackagedManifest")]
    [InlineData("""{ "id": "acme.todo", "name": "Todo", "version": "1.0" }""", "apiVersion")]
    [InlineData("""{ "schemaVersion": 1, """, "not valid JSON")]
    public void ABrokenManifestFailsThePluginWithTheReason(string json, string reason)
    {
        Directory.CreateDirectory(Path.Combine(folder.PluginsPath, "acme.todo"));
        File.WriteAllText(Path.Combine(folder.PluginsPath, "acme.todo", PluginManifest.FileName), json);

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains(reason, plugin.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void APackagedManifestNeedsAPluginId()
    {
        folder.Add(Greeters.Greeter with { Id = "tests.greeter.extra" });

        var plugin = Assert.Single(folder.Discover());

        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains("is not a plugin id", plugin.Error, StringComparison.Ordinal);
    }
}
