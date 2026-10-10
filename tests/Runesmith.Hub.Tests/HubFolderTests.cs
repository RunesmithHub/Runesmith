using Runesmith.Hub.Enforcement;
using Runesmith.Hub.State;
using Runesmith.Tests.Hub;
using RunesmithHub.Protocol.Resolution;

namespace Runesmith.Hub.Tests;

public sealed class HubFolderTests : IDisposable
{
    private readonly HubFixture fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => fixture.Dispose();

    [Fact]
    public void EverythingTheHubKeepsIsInItsOwnFolder()
    {
        var root = fixture.Paths.Root;

        Assert.Equal(Path.Combine(root, "plugins", "runesmith.git"), fixture.Paths.PluginFolder("runesmith.git"));
        Assert.All(
            [fixture.Paths.Plugins, fixture.Paths.StateFile, fixture.Paths.Staging, fixture.Paths.Quarantine, fixture.Paths.TrustStore, fixture.Paths.Icons],
            path => Assert.Equal(root, Path.GetDirectoryName(path)));
    }

    [Fact]
    public async Task InstallsUpdatesAndRemovalsUseTheHubFolderAndNeverTheUserPluginsFolder()
    {
        InstalledPlugins.Write(fixture, ("runesmith.git", "0.3.0", "official", true));
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);

        await client.ApplyAsync(client.Resolve(ResolutionRequest.Install("ember.themes"), HubPreferences.Default).Plan!, cancellationToken: Token);
        await client.ApplyAsync(client.Resolve(ResolutionRequest.Update("runesmith.git"), HubPreferences.Default).Plan!, cancellationToken: Token);
        Assert.True(Directory.EnumerateFileSystemEntries(fixture.Paths.Staging).Any());
        var report = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);

        Assert.Empty(report.Problems);
        Assert.Equal(["ember.themes", "runesmith.git"], report.HubPlugins.Order(StringComparer.Ordinal));
        Assert.True(File.Exists(Path.Combine(fixture.Paths.PluginFolder("ember.themes"), "plugin.json")));
        Assert.Contains("\"0.3.4\"", File.ReadAllText(Path.Combine(fixture.Paths.PluginFolder("runesmith.git"), "plugin.json")), StringComparison.Ordinal);
        Assert.True(File.Exists(fixture.Paths.StateFile));
        Assert.False(Directory.Exists(fixture.UserPlugins));

        client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        await client.ApplyAsync(client.Resolve(ResolutionRequest.Remove("ember.themes"), HubPreferences.Default).Plan!, cancellationToken: Token);
        HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);

        Assert.False(Directory.Exists(fixture.Paths.PluginFolder("ember.themes")));
        Assert.True(Directory.Exists(fixture.Paths.PluginFolder("runesmith.git")));
        Assert.False(Directory.Exists(fixture.UserPlugins));
    }

    [Fact]
    public async Task ALocalBuildWithTheSameIdLeavesTheHubCopyAndItsIntegrityAlone()
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        await client.ApplyAsync(client.Resolve(ResolutionRequest.Install("runesmith.git"), HubPreferences.Default).Plan!, cancellationToken: Token);
        HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);
        var before = Snapshot(fixture.Paths.PluginFolder("runesmith.git"));

        var local = WriteLocalBuild("runesmith.git");
        var report = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);

        Assert.Empty(report.ChangedFiles);
        Assert.Empty(report.Withheld);
        Assert.Contains("runesmith.git", report.HubPlugins);
        Assert.Equal(before, Snapshot(fixture.Paths.PluginFolder("runesmith.git")));
        Assert.True(PluginFiles.Check(fixture.Paths.PluginFolder("runesmith.git"), new HubStateStore(fixture.Paths.StateFile).Load().Find("runesmith.git")!.Files).IsIntact);
        Assert.True(File.Exists(Path.Combine(local, "lib", "runesmith.git.dll")));
    }

    [Fact]
    public async Task QuarantineTakesTheHubCopyAndLeavesALocalBuildWithTheSameId()
    {
        InstalledPlugins.Write(fixture, ("harbor.issues", "1.3.1", "verified", false));
        var local = WriteLocalBuild("harbor.issues");
        await fixture.CreateClient().RefreshAsync(Token);

        var report = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);

        Assert.Equal(["harbor.issues"], report.Quarantined);
        Assert.False(Directory.Exists(fixture.Paths.PluginFolder("harbor.issues")));
        Assert.Single(Directory.EnumerateDirectories(fixture.Paths.Quarantine));
        Assert.True(File.Exists(Path.Combine(local, "plugin.json")));
    }

    // Lays out a plugin the way dotnet build -t:InstallPlugin does: the user's plugins folder, one subfolder named after the id.
    private string WriteLocalBuild(string id)
    {
        var folder = Directory.CreateDirectory(Path.Combine(fixture.UserPlugins, id, "lib")).Parent!.FullName;
        File.WriteAllText(Path.Combine(folder, "plugin.json"), $$"""{ "id": "{{id}}", "version": "9.9.9" }""");
        File.WriteAllBytes(Path.Combine(folder, "lib", id + ".dll"), [9, 9, 9]);
        return folder;
    }

    private static Dictionary<string, string> Snapshot(string folder) =>
        Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(folder, file), file => Convert.ToHexString(File.ReadAllBytes(file)), StringComparer.Ordinal);
}
