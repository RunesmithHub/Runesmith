using Runesmith.Composition;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Settings;
using Runesmith.Workspace.Settings;

namespace Runesmith.Workspace.Tests.Settings;

public sealed class SettingsOwnershipTests : IDisposable
{
    private static readonly PluginInfo Todo = Plugin("acme.todo");
    private static readonly PluginInfo Notes = Plugin("acme.notes");

    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-settings-").FullName;
    private readonly Contributor host = new(new SettingDefinition("editor.fontFamily", "Font", "Editor", "Mono"));
    private readonly Contributor todo = new(new SettingDefinition("acme.todo.highlight", "Highlight", "To-do", true), new SettingDefinition("todo.color", "Color", "To-do", "red"));
    private readonly Contributor notes = new(new SettingDefinition("notes.folder", "Folder", "Notes", ""));
    private PluginInfo? caller;

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public void APluginChangesTheSettingsWhoseKeysStartWithItsIdAndTheOnesItContributes()
    {
        using var settings = Create();
        caller = Todo;

        settings.Set("acme.todo.highlight", false);
        settings.Set("todo.color", "blue");
        settings.Reset("todo.color");

        Assert.False(settings.Get<bool>("acme.todo.highlight"));
        Assert.Equal("red", settings.Get<string>("todo.color"));
    }

    [Theory]
    [InlineData("editor.fontFamily")]
    [InlineData("notes.folder")]
    public void APluginCannotChangeOtherSettings(string key)
    {
        using var settings = Create();
        caller = Todo;

        Assert.Throws<UnauthorizedAccessException>(() => settings.Set(key, "elsewhere"));
        Assert.Throws<UnauthorizedAccessException>(() => settings.Reset(key));
    }

    [Fact]
    public void RunesmithChangesEverySetting()
    {
        using var settings = Create();

        settings.Set("editor.fontFamily", "Serif");
        settings.Set("notes.folder", "elsewhere");

        Assert.Equal("Serif", settings.Get<string>("editor.fontFamily"));
        Assert.Equal("elsewhere", settings.Get<string>("notes.folder"));
    }

    private SettingsService Create() => new(
        [host, todo, notes],
        new MessageBus(),
        Path.Combine(folder, "settings.json"),
        () => caller,
        contributor => contributor == todo ? Todo : contributor == notes ? Notes : null);

    private static PluginInfo Plugin(string id) =>
        new(new PluginManifest(id, id, "1.0.0") { SchemaVersion = 1, Capabilities = [] }, Path.Combine(Path.GetTempPath(), id), PluginSource.Local);

    private sealed class Contributor(params SettingDefinition[] settings) : ISettingContributor
    {
        public IEnumerable<SettingDefinition> Settings => settings;
    }
}
