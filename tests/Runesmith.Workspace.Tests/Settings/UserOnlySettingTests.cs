using Runesmith.Sdk;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Settings;
using Runesmith.Workspace.Settings;

namespace Runesmith.Workspace.Tests.Settings;

public sealed class UserOnlySettingTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-user-only-").FullName;
    private readonly MessageBus bus = new();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void AFoldersSettingsCannotLetLocalCopiesReplacePlugins()
    {
        var root = Directory.CreateDirectory(Path.Combine(folder, "project")).FullName;
        Directory.CreateDirectory(Path.Combine(root, RunesmithPaths.WorkspaceFolderName));
        File.WriteAllText(RunesmithPaths.WorkspaceSettings(root), $$"""{ "{{CoreSettings.AllowLocalOverrides}}": true }""");
        using var settings = Create();

        bus.Publish(new WorkspaceOpenedMessage(root));

        Assert.False(settings.Get<bool>(CoreSettings.AllowLocalOverrides));
        Assert.Null(settings.GetValue(CoreSettings.AllowLocalOverrides, SettingScope.Workspace));
        Assert.Equal(SettingScope.Default, settings.GetEffectiveScope(CoreSettings.AllowLocalOverrides));
        Assert.Throws<InvalidOperationException>(() => settings.Set(CoreSettings.AllowLocalOverrides, true, SettingScope.Workspace));
    }

    [Fact]
    public void TheUsersSettingsLetLocalCopiesReplacePlugins()
    {
        using var settings = Create();

        settings.Set(CoreSettings.AllowLocalOverrides, true);

        Assert.True(settings.Get<bool>(CoreSettings.AllowLocalOverrides));
        Assert.Equal(SettingScope.User, settings.GetEffectiveScope(CoreSettings.AllowLocalOverrides));
    }

    [Fact]
    public void LocalCopiesReplacePluginsOnlyWhenTheUserTurnsItOn() =>
        Assert.Equal(false, new CoreSettings().Settings.Single(d => d.Key == CoreSettings.AllowLocalOverrides).DefaultValue);

    private SettingsService Create() => new([new CoreSettings()], bus, Path.Combine(folder, "settings.json"), () => null, _ => null);
}
