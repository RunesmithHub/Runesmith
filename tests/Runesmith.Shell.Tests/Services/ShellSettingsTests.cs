using Runesmith.Shell.Services;

namespace Runesmith.Shell.Tests.Services;

public sealed class ShellSettingsTests
{
    [Theory]
    [InlineData("toolbar", MenuBarMode.Toolbar)]
    [InlineData("separate", MenuBarMode.Separate)]
    [InlineData("Separate", MenuBarMode.Separate)]
    [InlineData("", MenuBarMode.Toolbar)]
    [InlineData(null, MenuBarMode.Toolbar)]
    [InlineData("hidden", MenuBarMode.Toolbar)]
    public void ReadsWhereTheMenusAre(string? value, MenuBarMode expected) => Assert.Equal(expected, ShellSettings.ParseMenuBar(value));

    [Theory]
    [InlineData("custom", TitleBarMode.Custom)]
    [InlineData("system", TitleBarMode.System)]
    [InlineData("System", TitleBarMode.System)]
    [InlineData(null, TitleBarMode.Custom)]
    [InlineData("native", TitleBarMode.Custom)]
    public void ReadsWhoDrawsTheTitleBar(string? value, TitleBarMode expected) => Assert.Equal(expected, ShellSettings.ParseTitleBar(value));

    [Fact]
    public void DrawsTheTitleBarUnlessTheUserAsksForTheSystemOne()
    {
        var setting = Assert.Single(new ShellSettings().Settings, s => s.Key == ShellSettings.TitleBar);

        Assert.Equal("custom", setting.DefaultValue);
        Assert.Equal(["custom", "system"], setting.Choices);
    }

    [Fact]
    public void PutsTheMenusInTheToolbarUnlessTheUserAsksForABar()
    {
        var setting = Assert.Single(new ShellSettings().Settings, s => s.Key == ShellSettings.MenuBar);

        Assert.Equal("toolbar", setting.DefaultValue);
        Assert.Equal(["toolbar", "separate"], setting.Choices);
        Assert.Equal("Window", setting.Category);
    }
}
