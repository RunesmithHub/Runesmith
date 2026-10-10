using Runesmith.Hub.Enforcement;
using Runesmith.Hub.State;
using Runesmith.Tests.Hub;
using RunesmithHub.Protocol.Resolution;

namespace Runesmith.Hub.Tests;

public sealed class IntegrityTests : IDisposable
{
    private readonly HubFixture fixture = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => fixture.Dispose();

    [Fact]
    public async Task AChangedFileTurnsThePluginAndItsDependentsOffAndOffersAReinstall()
    {
        var client = await InstallAsync("lumen.todo");
        File.AppendAllText(Path.Combine(fixture.Paths.PluginFolder("harbor.issues"), "lib", "Harbor.Issues.dll"), "x");

        var report = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);

        Assert.Equal(["lib/Harbor.Issues.dll changed."], report.ChangedFiles["harbor.issues"]);
        Assert.Contains("files changed", report.Withheld["harbor.issues"], StringComparison.Ordinal);
        Assert.Equal("It needs Harbor Issues, whose files changed since it was installed.", report.Withheld["lumen.todo"]);

        client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        var notice = Assert.Single(client.Notices(report.ChangedFiles, HubPreferences.Default));
        Assert.Equal(NoticeKind.FilesChanged, notice.Kind);
        var reinstall = client.ReinstallPlan("harbor.issues")!;
        await client.ApplyAsync(reinstall, cancellationToken: Token);

        var after = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);
        Assert.Empty(after.ChangedFiles);
        Assert.Empty(after.Withheld);
    }

    [Fact]
    public async Task AddedAndMissingFilesCountAsChanges()
    {
        await InstallAsync("ember.themes");
        var folder = fixture.Paths.PluginFolder("ember.themes");
        File.WriteAllText(Path.Combine(folder, "lib", "Extra.dll"), "MZ");
        File.Delete(Path.Combine(folder, "README.md"));

        var report = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);

        Assert.Equal(["lib/Extra.dll was added.", "README.md is missing."], report.ChangedFiles["ember.themes"]);
    }

    [Fact]
    public async Task AFileTouchedButUnchangedIsHashedOnceAndThenTrustedAgain()
    {
        await InstallAsync("ember.themes");
        var file = Path.Combine(fixture.Paths.PluginFolder("ember.themes"), "plugin.json");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-5));

        var report = HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);

        Assert.Empty(report.ChangedFiles);
        var recorded = new HubStateStore(fixture.Paths.StateFile).Load().Find("ember.themes")!.Files.Single(f => f.Path == "plugin.json");
        Assert.Equal(File.GetLastWriteTimeUtc(file), recorded.Modified);
    }

    [Fact]
    public async Task IconsAreCachedOnlyWhenTheyMatchTheIndex()
    {
        using var changed = new HubFixture(HubFixture.CopyIndex());
        var client = changed.CreateClient();
        await client.RefreshAsync(Token);
        var git = client.Catalog!.FindPlugin("runesmith.git")!.Icon;
        var csharp = client.Catalog!.FindPlugin("runesmith.csharp")!.Icon;
        var tampered = Path.Combine(changed.Index, "files", "runesmith.csharp", "icon-64.png");
        var bytes = File.ReadAllBytes(tampered);
        bytes[^1] ^= 1;
        File.WriteAllBytes(tampered, bytes);

        var cached = await client.GetIconAsync(git.Size64!, Token);

        Assert.Equal(Path.Combine(changed.Paths.Icons, git.Size64!.Sha256 + ".png"), cached);
        Assert.Equal(File.ReadAllBytes(Path.Combine(changed.Index, "files", "runesmith.git", "icon-64.png")), File.ReadAllBytes(cached!));
        Assert.Null(await client.GetIconAsync(csharp.Size64!, Token));
        Assert.False(File.Exists(Path.Combine(changed.Paths.Icons, csharp.Size64!.Sha256 + ".png")));
    }

    private async Task<HubClient> InstallAsync(string id)
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(Token);
        await client.ApplyAsync(client.Resolve(ResolutionRequest.Install(id), HubPreferences.Default).Plan!, cancellationToken: Token);
        HubStartup.Run(fixture.Configuration, fixture.Paths, hubEnabled: true, fixture.Time);
        return client;
    }
}
