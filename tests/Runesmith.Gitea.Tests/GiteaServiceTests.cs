using Runesmith.Gitea;
using Runesmith.Plugins.Gitea;

namespace Runesmith.Gitea.Tests;

public sealed class GiteaServiceTests
{
    [Fact]
    public void StartsWithGiteaCom()
    {
        var flavor = GiteaService.Flavor;

        Assert.Equal("runesmith.gitea", flavor.PluginId);
        Assert.Equal("Gitea", flavor.Name);
        Assert.Equal("gitea.com", flavor.DefaultServer.Key);
        Assert.Equal("gitea.com", flavor.ServerName(flavor.DefaultServer));
        Assert.Equal("Gitea (git.example.com)", flavor.ServerName(GiteaServer.Of("git.example.com")));
        Assert.Equal("gitea.oauthClientIds", flavor.ClientIdsSetting);
    }

    [Fact]
    public void TakesGiteaServersOnly()
    {
        Assert.True(GiteaService.Flavor.Accepts(ServerKind.Gitea));
        Assert.False(GiteaService.Flavor.Accepts(ServerKind.Forgejo));
        Assert.False(GiteaService.Flavor.Accepts(ServerKind.Unknown));
    }

    [Fact]
    public void OffersTheClientIdSettingInItsCategory()
    {
        var setting = Assert.Single(new GiteaSettings().Settings);

        Assert.Equal("gitea.oauthClientIds", setting.Key);
        Assert.Equal("Gitea", setting.Category);
        Assert.Equal("", setting.DefaultValue);
    }
}
