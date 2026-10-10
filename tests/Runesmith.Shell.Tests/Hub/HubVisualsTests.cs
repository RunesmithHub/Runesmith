using Avalonia.Controls;
using Runesmith.Shell.Hub;
using Runesmith.Tests.Hub;
using RunesmithHub.Protocol.Index;

namespace Runesmith.Shell.Tests.Hub;

public sealed class HubVisualsTests : IDisposable
{
    private readonly HubFixture fixture = new();

    public void Dispose() => fixture.Dispose();

    [Fact]
    public Task AnOfficialPluginShowsItsOwnIconFromTheHub() => HeadlessSession.Value.Dispatch(async () =>
    {
        var client = fixture.CreateClient();
        await client.RefreshAsync(TestContext.Current.CancellationToken);
        var record = client.Catalog!.FindPlugin("runesmith.git")!;

        var icon = (Border)HubVisuals.PluginIcon(client, record.Id, record.Name, record.Tier, record.Icon.Size64, null, 32);

        Assert.IsNotType<Image>(icon.Child);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (icon.Child?.GetType() != typeof(Image) && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.IsType<Image>(icon.Child);
        Assert.True(File.Exists(Path.Combine(fixture.Paths.Icons, record.Icon.Size64!.Sha256 + ".png")));
        return true;
    }, CancellationToken.None);

    [Fact]
    public Task AnOfficialPluginShowsTheIconInItsFolder() => HeadlessSession.Value.Dispatch(() =>
    {
        var folder = Directory.CreateDirectory(Path.Combine(fixture.Home, "runesmith.git", "icon")).FullName;
        File.Copy(Path.Combine(fixture.Index, "files", "runesmith.git", "icon-64.png"), Path.Combine(folder, "64.png"));

        var icon = (Border)HubVisuals.PluginIcon(fixture.CreateClient(), "runesmith.git", "Git", PluginTier.Official, null, Path.GetDirectoryName(folder), 32);

        Assert.IsType<Image>(icon.Child);
        return Task.FromResult(true);
    }, CancellationToken.None);
}
