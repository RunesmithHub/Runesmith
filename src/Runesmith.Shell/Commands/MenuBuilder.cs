using Avalonia.Controls;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Commands;

namespace Runesmith.Shell.Commands;

/// <summary>Builds the main menu from the registered menu items, grouped and separated by group, and the context menus plugins add to.</summary>
internal static class MenuBuilder
{
    /// <summary>Gets the main menu's menus that have items, in order: Runesmith's own as <see cref="Menus.All"/> orders them, with the ones
    /// plugins name otherwise before Tools, by <see cref="MenuDefinition.Order"/> and title, or by name when they have no definition.</summary>
    public static IReadOnlyList<string> MenuNames(IEnumerable<MenuItemDefinition> items, IEnumerable<MenuDefinition>? menus = null)
    {
        var definitions = Definitions(menus);
        var used = items.Select(i => i.Menu.Split('/')[0]).Where(m => !ContextMenus.IsContextMenu(m)).ToHashSet(StringComparer.Ordinal);
        var others = used.Where(m => !Menus.All.Contains(m))
            .OrderBy(m => definitions.GetValueOrDefault(m)?.Order ?? 0)
            .ThenBy(m => definitions.GetValueOrDefault(m)?.Title ?? m, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m, StringComparer.Ordinal);
        var tools = Menus.All.ToList().IndexOf(Menus.Tools);
        return [.. Menus.All.Take(tools).Concat(others).Concat(Menus.All.Skip(tools)).Where(used.Contains)];
    }

    /// <summary>Builds the main menu's menus, leaving out the ones none of whose commands exist.</summary>
    public static List<MenuItem> Build(CommandService commands)
    {
        var menus = new List<MenuItem>();
        var definitions = Definitions(commands.Menus);
        var placed = commands.MenuItems.Where(i => commands.Find(i.CommandId) is not null).ToList();
        foreach (var name in MenuNames(placed, commands.Menus))
        {
            var items = placed.Where(i => i.Menu == name || i.Menu.StartsWith(name + "/", StringComparison.Ordinal)).ToList();
            var menu = new MenuItem { Header = "_" + (definitions.GetValueOrDefault(name)?.Title ?? name) };
            Fill(menu, name, items, commands);
            menus.Add(menu);
        }

        return menus;
    }

    private static Dictionary<string, MenuDefinition> Definitions(IEnumerable<MenuDefinition>? menus)
    {
        var definitions = new Dictionary<string, MenuDefinition>(StringComparer.Ordinal);
        foreach (var menu in menus ?? [])
            definitions.TryAdd(menu.Id, menu);
        return definitions;
    }

    private static void Fill(MenuItem parent, string path, List<MenuItemDefinition> items, CommandService commands)
    {
        var entries = new List<(string Group, int Order, Control Item)>();
        foreach (var item in items.Where(i => i.Menu == path))
        {
            if (commands.Find(item.CommandId) is { } command)
                entries.Add((item.Group, item.Order, Item(command, commands)));
        }

        foreach (var submenu in items.Where(i => i.Menu != path).Select(i => i.Menu[(path.Length + 1)..].Split('/')[0]).Distinct())
        {
            var child = new MenuItem { Header = submenu };
            var childPath = path + "/" + submenu;
            Fill(child, childPath, [.. items.Where(i => i.Menu == childPath || i.Menu.StartsWith(childPath + "/", StringComparison.Ordinal))], commands);
            var first = items.First(i => i.Menu.StartsWith(childPath, StringComparison.Ordinal));
            entries.Add((first.Group, first.Order, child));
        }

        string? group = null;
        foreach (var entry in entries.OrderBy(e => e.Group, StringComparer.Ordinal).ThenBy(e => e.Order))
        {
            if (group is not null && group != entry.Group)
                parent.Items.Add(new Separator());
            group = entry.Group;
            parent.Items.Add(entry.Item);
        }

        parent.SubmenuOpened += (_, _) =>
        {
            foreach (var item in parent.Items.OfType<MenuItem>())
            {
                if (item.Tag is string id)
                    item.IsEnabled = commands.CanExecute(id);
            }
        };
    }

    /// <summary>Builds a context menu of commands, where null stands for a separator, followed by the items plugins added to
    /// <paramref name="contributions"/> that can run on <paramref name="target"/>. Commands that do not exist are left out, and a command a
    /// plugin placed moves to the plugin's group.</summary>
    public static ContextMenu ContextMenu(CommandService commands, IEnumerable<string?> commandIds, string? contributions = null, ContextMenuTarget? target = null)
    {
        var placed = contributions is null || target is null
            ? []
            : commands.MenuItems.Where(i => i.Menu == contributions).Select(i => i.CommandId).ToHashSet(StringComparer.Ordinal);
        var items = new List<Control>();
        foreach (var id in commandIds)
        {
            if (id is null)
            {
                AddSeparator(items);
            }
            else if (!placed.Contains(id) && commands.Find(id) is { } command)
            {
                var item = Item(command, commands);
                item.IsEnabled = commands.CanExecute(id);
                items.Add(item);
            }
        }

        if (contributions is not null && target is not null)
        {
            var contributed = ContributedItems(commands, contributions, target);
            items.AddRange(items.Count == 0 || items[^1] is Separator ? contributed.Skip(1) : contributed);
        }

        if (items.Count > 0 && items[^1] is Separator)
            items.RemoveAt(items.Count - 1);

        // A narrower context menu cuts off the longest key gestures.
        var menu = new ContextMenu { MinWidth = 320 };
        foreach (var item in items)
            menu.Items.Add(item);
        return menu;
    }

    /// <summary>Gets the items plugins added to a context menu whose commands can run on the target, each group after a separator, the first
    /// included.</summary>
    public static List<Control> ContributedItems(CommandService commands, string contextMenu, ContextMenuTarget target)
    {
        var items = new List<Control>();
        var groups = commands.MenuItems.Where(i => i.Menu == contextMenu).GroupBy(i => i.Group).OrderBy(g => g.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var separated = false;
            foreach (var definition in group.OrderBy(i => i.Order))
            {
                if (commands.Find(definition.CommandId) is not { } command || !commands.CanExecute(command.Id, target))
                    continue;

                if (!separated)
                    items.Add(new Separator());
                separated = true;
                items.Add(Item(command, commands, target));
            }
        }

        return items;
    }

    private static void AddSeparator(List<Control> items)
    {
        if (items.Count > 0 && items[^1] is not Separator)
            items.Add(new Separator());
    }

    private static CommandMenuItem Item(CommandDefinition command, CommandService commands, object? argument = null)
    {
        var gestures = commands.GetGestures(command.Id);
        var item = new CommandMenuItem
        {
            Header = command.Title,
            Tag = command.Id,
            InputGesture = gestures.Count > 0 ? gestures[0] : null,
            GestureText = gestures.Count > 0 ? KeyBindings.Format(gestures[0]) : null,
            Icon = Icons.Find(command.Icon) is { } icon ? new SymbolIcon { Data = icon, Size = 15 } : null,
        };
        item.Click += (_, _) => _ = commands.ExecuteAsync(command.Id, argument);
        return item;
    }
}
