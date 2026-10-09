using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Settings;
using Runesmith.Workspace.Settings;

namespace Runesmith.Workspace.Tests.Settings;

public sealed class SettingValueTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-settings-").FullName;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void ANumberSetReadsBackAtOnceWithoutWaitingForTheFileToReload()
    {
        using var settings = Create();

        settings.Set("plugins.hub.checkInterval", 12);
        settings.Set("editor.lineHeight", 1.75);

        Assert.Equal(12, settings.Get<int>("plugins.hub.checkInterval"));
        Assert.Equal((object)12, settings.GetValue("plugins.hub.checkInterval", SettingScope.User));
        Assert.Equal(SettingScope.User, settings.GetEffectiveScope("plugins.hub.checkInterval"));
        Assert.Equal(1.75, settings.Get<double>("editor.lineHeight"));
    }

    [Fact]
    public void ANumberIsKeptWithinItsLimits()
    {
        using var settings = Create();

        settings.Set("plugins.hub.checkInterval", 40);

        Assert.Equal(24, settings.Get<int>("plugins.hub.checkInterval"));
        using var reloaded = Create();
        Assert.Equal(24, reloaded.Get<int>("plugins.hub.checkInterval"));
    }

    private SettingsService Create() => new(
        [new Contributor(
            new SettingDefinition("plugins.hub.checkInterval", "Check interval", "Plugins", 6) { Minimum = 1, Maximum = 24 },
            new SettingDefinition("editor.lineHeight", "Line height", "Editor", 1.4))],
        new MessageBus(),
        Path.Combine(folder, "settings.json"),
        () => null,
        _ => null);

    private sealed class Contributor(params SettingDefinition[] settings) : ISettingContributor
    {
        public IEnumerable<SettingDefinition> Settings => settings;
    }
}
