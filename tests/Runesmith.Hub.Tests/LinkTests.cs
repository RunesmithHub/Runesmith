using Runesmith.Hub.Links;
using Runesmith.Tests.Hub;

namespace Runesmith.Hub.Tests;

public sealed class LinkTests
{
    [Theory]
    [InlineData("runesmith://hub/plugin/lumen.todo", "lumen.todo", null)]
    [InlineData("runesmith://hub/plugin/harbor.issues?version=1.3.2", "harbor.issues", "1.3.2")]
    [InlineData("RUNESMITH://hub/plugin/lumen.todo?version=1.1.0-beta.2", "lumen.todo", "1.1.0-beta.2")]
    public void APluginLinkCarriesAnIdAndAVersion(string text, string id, string? version)
    {
        Assert.True(HubLink.TryParse(text, out var link, out var error), error);

        var plugin = Assert.IsType<PluginLink>(link);
        Assert.Equal(id, plugin.PluginId);
        Assert.Equal(version, plugin.Version?.ToString());
    }

    [Fact]
    public void TheUpdatesLinkOpensTheUpdates() => Assert.IsType<UpdatesLink>(Parse("runesmith://hub/updates"));

    [Theory]
    [InlineData("runesmith://hub/plugin/lumen.todo?url=https://evil.example/x.rsplugin", "Runesmith refuses \"url\"")]
    [InlineData("runesmith://hub/plugin/lumen.todo?version=1.0.0&autoinstall=1", "Runesmith refuses \"autoinstall\"")]
    [InlineData("runesmith://hub/plugin/lumen.todo?version=1.0.0&version=1.1.0", "one version at most")]
    [InlineData("runesmith://hub/plugin/lumen.todo?version=1.0.0+build.5", "is not a plugin version")]
    [InlineData("runesmith://hub/plugin/lumen.todo?version=", "is not a plugin version")]
    [InlineData("runesmith://hub/plugin/lumen.todo?sha256=abc", "Runesmith refuses \"sha256\"")]
    [InlineData("runesmith://hub/plugin/Lumen.Todo", "is not a plugin id")]
    [InlineData("runesmith://hub/plugin/../../etc/passwd", "is not a plugin id")]
    [InlineData("runesmith://hub/plugin/lumen.todo/", "is not a plugin id")]
    [InlineData("runesmith://hub/plugin/lumen%2etodo", "is not a plugin id")]
    [InlineData("runesmith://evil.example/plugin/lumen.todo", "no \"evil.example\" links")]
    [InlineData("runesmith://hub/publisher/lumen", "names neither")]
    [InlineData("runesmith://hub/updates?all=1", "takes no parameters")]
    [InlineData("runesmith://hub/plugin/lumen.todo#install", "fragment")]
    [InlineData("https://hub.runesmith.dev/plugins/lumen.todo", "Only runesmith:// links")]
    [InlineData("runesmith:hub/plugin/lumen.todo", "Only runesmith:// links")]
    [InlineData("runesmith://hub/plugin/lumen.todo\n", "characters links cannot have")]
    [InlineData("", "empty")]
    public void EverythingElseIsRefusedWithAReason(string text, string reason)
    {
        Assert.False(HubLink.TryParse(text, out var link, out var error));

        Assert.Null(link);
        Assert.Contains(reason, error, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkLongerThanTheLimitIsRefused()
    {
        var text = "runesmith://hub/plugin/lumen.todo?version=1.0.0-" + new string('a', HubLink.MaxLength);

        Assert.False(HubLink.TryParse(text, out _, out var error));
        Assert.Contains("longer than 512", error, StringComparison.Ordinal);
    }

    [Fact]
    public void ALinkPrintsAsItIsWritten() =>
        Assert.Equal("runesmith://hub/plugin/harbor.issues?version=1.3.2", Parse("runesmith://hub/plugin/harbor.issues?version=1.3.2").ToString());

    [Fact]
    public void PastThreeLinksAMinuteEachReplacesTheOthersUntilTheMinutePasses()
    {
        var time = new ManualTime(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero));
        var throttle = new LinkThrottle(time);

        Assert.Equal([0, 0, 0], [throttle.Arrive(), throttle.Arrive(), throttle.Arrive()]);
        Assert.Equal(3, throttle.Arrive());
        time.Now = time.Now.AddSeconds(30);
        Assert.Equal(4, throttle.Arrive());
        time.Now = time.Now.AddSeconds(61);
        Assert.Equal(0, throttle.Arrive());
    }

    private static HubLink Parse(string text) => HubLink.TryParse(text, out var link, out var error) ? link : throw new InvalidOperationException(error);
}
