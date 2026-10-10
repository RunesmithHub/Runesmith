namespace Runesmith.Composition.Tests;

public sealed class HubPluginTests : IDisposable
{
    private readonly TestPluginFolder hub = new();
    private readonly TestPluginFolder user = new();

    public void Dispose()
    {
        hub.Dispose();
        user.Dispose();
    }

    [Fact]
    public void ALocalCopyOfAHubPluginIsRefusedUnlessTheUserAllowsIt()
    {
        hub.Add(Greeters.Greeter);
        user.Add(Greeters.Greeter with { Version = "2.0.0" });

        var plugins = Discover(hubIds: ["tests.greeter"]);

        var installed = plugins.Single(p => p.Source == PluginSource.Hub);
        var copy = plugins.Single(p => p.Source == PluginSource.Local);
        Assert.Equal((PluginState.Loaded, false), (installed.State, installed.IsLocalCopy));
        Assert.Equal(PluginState.Refused, copy.State);
        Assert.StartsWith("The hub installed this plugin, so the copy in your plugins folder does not load.", copy.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.9.0")]
    [InlineData("1.0.0")]
    [InlineData("1.1.0")]
    public void AnAllowedLocalCopyRunsInsteadOfTheHubOneWhateverItsVersion(string version)
    {
        hub.Add(Greeters.Greeter);
        user.Add(Greeters.Greeter with { Version = version });

        var plugins = Discover(hubIds: ["tests.greeter"], allowLocalOverrides: true);

        Assert.Equal((PluginState.Replaced, PluginSource.Hub, $"Version {version} from your plugins folder runs instead."), (plugins[0].State, plugins[0].Source, plugins[0].Error));
        Assert.Equal((PluginState.Loaded, PluginSource.Local, true), (plugins[1].State, plugins[1].Source, plugins[1].IsLocalCopy));
        var loaded = Assert.Single(new PluginLoader(plugins).Load());
        Assert.Equal(1, loaded.Index);
        Assert.True(PluginCallers.Of(loaded.Assembly)?.IsLocalCopy);
    }

    [Fact]
    public void TheHubCopyStaysWhenAnAllowedLocalCopyCannotLoad()
    {
        hub.Add(Greeters.Greeter);
        var copy = user.Add(Greeters.Greeter with { Version = "1.2.0" });
        File.WriteAllBytes(Path.Combine(copy, "lib", "Tests.Greeter.dll"), [0x4D, 0x5A, 1, 2, 3]);

        var plugins = Discover(hubIds: ["tests.greeter"], allowLocalOverrides: true);

        Assert.Equal(PluginState.Loaded, plugins[0].State);
        Assert.Equal(PluginState.Replaced, plugins[1].State);
        Assert.Equal("Version 1.2.0 from your plugins folder could not load: lib/Tests.Greeter.dll is not a .NET assembly. Version 1.0.0 from the hub runs instead.", plugins[1].Error);
    }

    [Fact]
    public void WhatTheHubWithholdsDoesNotHoldBackAnAllowedLocalCopy()
    {
        hub.Add(Greeters.Greeter);
        user.Add(Greeters.Greeter);

        var plugins = Discover(hubIds: ["tests.greeter"], withheld: new Dictionary<string, string> { ["tests.greeter"] = "The plugin hub is turned off." }, allowLocalOverrides: true);

        Assert.Equal(PluginState.Replaced, plugins.Single(p => p.Source == PluginSource.Hub).State);
        Assert.Equal(PluginState.Loaded, plugins.Single(p => p.Source == PluginSource.Local).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALocalPluginWithItsOwnIdIsNotALocalCopy(bool allowLocalOverrides)
    {
        hub.Add(Greeters.Greeter);
        user.Add(new TestPlugin("tests.other", "Tests.Other") { Implementation = "namespace Tests.Other; public sealed class Other { }" });

        var plugins = Discover(hubIds: ["tests.greeter"], allowLocalOverrides: allowLocalOverrides);
        var loaded = new PluginLoader(plugins).Load();

        var other = plugins.Single(p => p.Manifest.Id == "tests.other");
        Assert.Equal((PluginState.Loaded, PluginSource.Local, false), (other.State, other.Source, other.IsLocalCopy));
        Assert.Equal(2, loaded.Count);
        Assert.All(loaded, plugin => Assert.False(PluginCallers.Of(plugin.Assembly)?.IsLocalCopy));
    }

    [Fact]
    public void TwoLocalCopiesOfAPluginAreDuplicates()
    {
        user.Add(Greeters.Greeter);
        var other = new TestPluginFolder();
        try
        {
            other.Add(Greeters.Greeter with { Version = "2.0.0" });

            var plugins = PluginDiscovery.Discover([(user.PluginsPath, PluginSource.Local), (other.PluginsPath, PluginSource.Local)], new HashSet<string>());

            Assert.Equal(PluginState.Loaded, plugins[0].State);
            Assert.Equal((PluginState.Failed, "Another plugin with the id tests.greeter was loaded first."), (plugins[1].State, plugins[1].Error));
        }
        finally
        {
            other.Dispose();
        }
    }

    [Fact]
    public void OnlyOneAllowedLocalCopyOfAHubPluginRuns()
    {
        hub.Add(Greeters.Greeter);
        user.Add(Greeters.Greeter);
        user.Add(Greeters.Greeter, folder: "tests.greeter-2");

        var plugins = Discover(hubIds: ["tests.greeter"], allowLocalOverrides: true);

        Assert.Equal([PluginState.Replaced, PluginState.Loaded, PluginState.Failed], plugins.Select(p => p.State));
        Assert.Equal("Another plugin with the id tests.greeter was loaded first.", plugins[2].Error);
    }

    [Fact]
    public void AWithheldPluginDoesNotLoadAndSaysWhy()
    {
        hub.Add(Greeters.Greeter);
        hub.Add(Greeters.Welcome);

        var plugins = Discover(hubIds: ["tests.greeter", "tests.welcome"], withheld: new Dictionary<string, string> { ["tests.greeter"] = "The hub blocked it." });
        new PluginLoader(plugins).Load();

        var greeter = plugins.Single(p => p.Manifest.Id == "tests.greeter");
        Assert.Equal((PluginState.Disabled, "The hub blocked it."), (greeter.State, greeter.Error));
        Assert.Equal(PluginState.Failed, plugins.Single(p => p.Manifest.Id == "tests.welcome").State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ALocalBuildWithTheIdOfAHubPluginLeavesTheHubCopyAlone(bool allowLocalOverrides)
    {
        var installed = hub.Add(Greeters.Greeter with { Version = "1.2.0" });
        var before = Snapshot(installed);
        var local = user.Add(Greeters.Greeter with { Version = "1.3.0" });

        var plugins = Discover(hubIds: ["tests.greeter"], allowLocalOverrides: allowLocalOverrides);

        var fromHub = plugins.Single(p => p.Source == PluginSource.Hub);
        var copy = plugins.Single(p => p.Source == PluginSource.Local);
        Assert.Equal((installed, local), (fromHub.Directory, copy.Directory));
        Assert.Equal(allowLocalOverrides ? (PluginState.Replaced, PluginState.Loaded) : (PluginState.Loaded, PluginState.Refused), (fromHub.State, copy.State));
        Assert.Equal(plugins.IndexOf(allowLocalOverrides ? copy : fromHub), Assert.Single(new PluginLoader(plugins).Load()).Index);
        Assert.Equal(before, Snapshot(installed));
    }

    [Fact]
    public void TheFolderAPluginIsFoundInDecidesItsSource()
    {
        hub.Add(Greeters.Greeter);
        hub.Add(Greeters.Stowaway);
        user.Add(new TestPlugin("tests.other", "Tests.Other") { Implementation = "namespace Tests.Other; public sealed class Other { }" });

        var plugins = Discover(hubIds: ["tests.greeter", "tests.other"]);

        Assert.Equal(
            [("tests.greeter", PluginSource.Hub, false), ("tests.other", PluginSource.Local, false)],
            plugins.Select(p => (p.Manifest.Id, p.Source, p.IsLocalCopy)));
    }

    private static Dictionary<string, string> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(folder, file), file => Convert.ToHexString(File.ReadAllBytes(file)), StringComparer.Ordinal);

    private List<PluginInfo> Discover(string[] hubIds, Dictionary<string, string>? withheld = null, bool allowLocalOverrides = false) =>
        [.. PluginDiscovery.Discover([(hub.PluginsPath, PluginSource.Hub), (user.PluginsPath, PluginSource.Local)], new HashSet<string>(), hubIds.ToHashSet(), withheld, allowLocalOverrides)];
}
