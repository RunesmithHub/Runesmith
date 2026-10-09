using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Commands;

namespace Runesmith.Shell.Tests.Commands;

public sealed class CommandServiceTests
{
    [Fact]
    public void ACommandSitsInAMenuOnceWhenTwoPartsPlaceIt()
    {
        var commands = new CommandService([], new Lazy<INotificationService>(() => throw new InvalidOperationException()));

        commands.AddMenuItem(new MenuItemDefinition(Menus.File, CommandIds.CloneRepository, "1-new", 3));
        commands.AddMenuItem(new MenuItemDefinition(Menus.File, CommandIds.CloneRepository, "vcs", 0));
        commands.AddMenuItem(new MenuItemDefinition("Git", CommandIds.CloneRepository));

        Assert.Equal([Menus.File, "Git"], commands.MenuItems.Select(i => i.Menu));
        Assert.Equal("1-new", commands.MenuItems[0].Group);
    }
}
