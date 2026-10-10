using Runesmith.Sdk.Settings;
using Runesmith.Workspace.Settings;

namespace Runesmith.Workspace.Tests.Settings;

public sealed class EditorSettingTests
{
    [Theory]
    [InlineData(SettingKeys.Folding)]
    [InlineData(SettingKeys.OccurrenceHighlight)]
    [InlineData(SettingKeys.SemanticHighlighting)]
    public void FoldingOccurrencesAndSemanticHighlightingAreEditorSettingsThatStartOn(string key)
    {
        var definition = Assert.Single(new CoreSettings().Settings, d => d.Key == key);

        Assert.Equal("Editor", definition.Category);
        Assert.Equal(true, definition.DefaultValue);
        Assert.False(string.IsNullOrWhiteSpace(definition.Description));
    }
}
