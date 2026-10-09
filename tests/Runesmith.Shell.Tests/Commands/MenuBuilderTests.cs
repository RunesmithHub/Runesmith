using Avalonia.Controls;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;
using Runesmith.Shell.Commands;

namespace Runesmith.Shell.Tests.Commands;

public sealed class MenuBuilderTests
{
    [Fact]
    public void PluginMenusComeAfterBuildAndBeforeToolsSortedByNameAndContextMenusStayOut()
    {
        MenuItemDefinition[] items =
        [
            new(Menus.Help, "a"),
            new("Git", "b"),
            new("Database", "c"),
            new(Menus.Tools + "/GitHub", "d"),
            new(Menus.File, "e"),
            new(Menus.Build, "f"),
            new(ContextMenus.Editor, "g"),
            new(ContextMenus.Explorer, "h"),
        ];

        Assert.Equal([Menus.File, Menus.Build, "Database", "Git", Menus.Tools, Menus.Help], MenuBuilder.MenuNames(items));
    }

    [Fact]
    public void PluginMenusGoByTheirOrderThenTitle()
    {
        MenuItemDefinition[] items = [new("docker", "a"), new("Git", "b"), new("zeta", "c"), new(Menus.Help, "d")];
        MenuDefinition[] menus = [new("docker", "Containers"), new("zeta", "Alpha") { Order = 0 }, new("Git", "Git") { Order = -1 }];

        Assert.Equal(["Git", "zeta", "docker", Menus.Help], MenuBuilder.MenuNames(items, menus));
    }

    [Fact]
    public Task APluginMenuShowsItsTitleAndOnlyWhileItHasACommandThatExists() => HeadlessSession.Value.Dispatch(() =>
    {
        var commands = new CommandService([], new Lazy<INotificationService>(() => throw new InvalidOperationException()));
        commands.Add(new CommandDefinition("docker.up", "Start Containers", "Docker"), _ => Task.CompletedTask);
        commands.AddMenu(new MenuDefinition("docker", "Docker"));
        commands.AddMenu(new MenuDefinition("docker", "Ignored"));
        commands.AddMenu(new MenuDefinition("empty", "Empty"));
        commands.AddMenuItem(new MenuItemDefinition("docker", "docker.up"));
        commands.AddMenuItem(new MenuItemDefinition("empty", "missing.command"));
        commands.AddMenuItem(new MenuItemDefinition(Menus.Help, "missing.command"));

        var menu = Assert.Single(MenuBuilder.Build(commands));

        Assert.Equal("_Docker", menu.Header);
        Assert.Equal("Start Containers", Assert.IsType<CommandMenuItem>(Assert.Single(menu.Items)).Header);
        Assert.Equal(2, commands.Menus.Count);
    }, CancellationToken.None);

    [Theory]
    [InlineData("")]
    [InlineData("Docker/Compose")]
    [InlineData(Menus.Tools)]
    [InlineData(ContextMenus.Editor)]
    public void RefusesAMenuIdThatCannotBeAPluginsMenu(string id)
    {
        var commands = new CommandService([], new Lazy<INotificationService>(() => throw new InvalidOperationException()));

        commands.AddMenu(new MenuDefinition(id, "Menu"));

        Assert.Empty(commands.Menus);
        Assert.Single(commands.Errors);
    }

    [Fact]
    public Task ContextMenusShowThePluginsItemsThatApplyInTheirGroupsAndMoveTheHostsCommandsTheyPlace() => HeadlessSession.Value.Dispatch(() =>
    {
        var commands = new CommandService([], new Lazy<INotificationService>(() => throw new InvalidOperationException()));
        object? received = null;
        commands.Add(new CommandDefinition("host.copy", "Copy", "Edit"), _ => Task.CompletedTask);
        commands.Add(new CommandDefinition(CommandIds.RollbackLines, "Rollback Lines", "Editor"), _ => Task.CompletedTask);
        commands.Add(new CommandDefinition("git.diff", "Show Diff", "Git"), argument =>
        {
            received = argument;
            return Task.CompletedTask;
        }, argument => argument is ContextMenuTarget { IsDirectory: false });
        commands.Add(new CommandDefinition("git.history", "Show History", "Git"), _ => Task.CompletedTask);
        commands.AddMenuItem(new MenuItemDefinition(ContextMenus.Editor, "git.history", "git", 2));
        commands.AddMenuItem(new MenuItemDefinition(ContextMenus.Editor, CommandIds.RollbackLines, "git", 1));
        commands.AddMenuItem(new MenuItemDefinition(ContextMenus.Editor, "git.diff", "git", 0));
        commands.AddMenuItem(new MenuItemDefinition(ContextMenus.Explorer, "git.history", "git", 0));

        var target = new ContextMenuTarget("/repo/a.cs") { Lines = (3, 4) };
        var menu = MenuBuilder.ContextMenu(commands, ["host.copy", null, CommandIds.RollbackLines], ContextMenus.Editor, target);

        Assert.Equal(["Copy", "-", "Show Diff", "Rollback Lines", "Show History"], menu.Items.Select(i => i is MenuItem item ? (string)item.Header! : "-"));
        ((MenuItem)menu.Items[2]!).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Same(target, received);

        var folder = MenuBuilder.ContributedItems(commands, ContextMenus.Editor, new ContextMenuTarget("/repo/src", IsDirectory: true));
        Assert.Equal(["-", "Rollback Lines", "Show History"], folder.Select(i => i is MenuItem item ? (string)item.Header! : "-"));
    }, CancellationToken.None);
}
