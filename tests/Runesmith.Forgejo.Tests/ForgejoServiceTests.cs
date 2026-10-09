using Runesmith.Forgejo;
using Runesmith.Plugins.Gitea;

namespace Runesmith.Forgejo.Tests;

public sealed class ForgejoServiceTests
{
    [Fact]
    public void StartsWithCodeberg()
    {
        var flavor = ForgejoService.Flavor;

        Assert.Equal("runesmith.forgejo", flavor.PluginId);
        Assert.Equal("Forgejo", flavor.Name);
        Assert.Equal("codeberg.org", flavor.DefaultServer.Key);
        Assert.Equal("Codeberg", flavor.ServerName(flavor.DefaultServer));
        Assert.Equal("Forgejo (git.example.com)", flavor.ServerName(GiteaServer.Of("git.example.com")));
        Assert.Equal("forgejo.oauthClientIds", flavor.ClientIdsSetting);
    }

    [Fact]
    public void TakesForgejoServersOnly()
    {
        Assert.True(ForgejoService.Flavor.Accepts(ServerKind.Forgejo));
        Assert.False(ForgejoService.Flavor.Accepts(ServerKind.Gitea));
        Assert.False(ForgejoService.Flavor.Accepts(ServerKind.Unknown));
    }

    [Fact]
    public void OffersTheClientIdSettingInItsCategory()
    {
        var setting = Assert.Single(new ForgejoSettings().Settings);

        Assert.Equal("forgejo.oauthClientIds", setting.Key);
        Assert.Equal("Forgejo", setting.Category);
        Assert.Equal("", setting.DefaultValue);
    }
}
