using System.Collections.ObjectModel;
using System.Composition;
using Avalonia.Controls;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.Session;

namespace Runesmith.Shell.ToolWindows;

/// <summary>Shows and hides the tool windows plugins and Runesmith export, keeps their stripe buttons, and adds a command and a View menu item
/// for each.</summary>
/// <remarks>Each window belongs to an area (left, right or bottom) that shows one window at a time. The left stripe holds the left area's
/// windows above the bottom area's; the right stripe holds the right area's.</remarks>
[Export(typeof(IToolWindowManager))]
[Export(typeof(ICommandContributor))]
[Export]
[Shared]
public sealed class ToolWindowManager : IToolWindowManager, ICommandContributor
{
    private readonly Dictionary<string, IToolWindowProvider> providers;
    private readonly Dictionary<string, Control> contents = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ToolStripeItem> items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> badges = new(StringComparer.Ordinal);
    private readonly Dictionary<DockSide, ObservableCollection<ToolStripeItem>> stripes = new()
    {
        [DockSide.Left] = [],
        [DockSide.Right] = [],
        [DockSide.Bottom] = [],
    };

    private ToolWindowLayout layout;
    private bool isSynced;

    [ImportingConstructor]
    public ToolWindowManager([ImportMany] IEnumerable<IToolWindowProvider> providers)
    {
        this.providers = providers.GroupBy(p => p.Definition.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        layout = CreateLayout(SessionStore.LoadToolWindows());
    }

    public IReadOnlyList<ToolWindowDefinition> ToolWindows => [.. providers.Values.Select(p => p.Definition).OrderBy(d => d.Side).ThenBy(d => d.Order)];

    /// <summary>Gets the arrangement of the tool windows.</summary>
    internal ToolWindowLayout Layout => layout;

    /// <summary>Raised when a window moves, shows or hides, or the arrangement is reset.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the stripe buttons of an area's windows, in stripe order; the collection stays the same while the buttons change.</summary>
    public ObservableCollection<ToolStripeItem> StripeItems(DockSide area)
    {
        if (!isSynced)
            SyncStripes();
        return stripes[area];
    }

    public ToolWindowDefinition? Find(string toolWindowId) => providers.GetValueOrDefault(toolWindowId)?.Definition;

    /// <summary>Gets a window's content, created the first time it is asked for.</summary>
    public Control? GetContent(string toolWindowId)
    {
        if (contents.TryGetValue(toolWindowId, out var content))
            return content;
        if (!providers.TryGetValue(toolWindowId, out var provider))
            return null;

        content = provider.CreateContent();
        contents[toolWindowId] = content;
        return content;
    }

    public void Show(string toolWindowId) => UiThread.Run(() => layout.Show(toolWindowId));

    public void Hide(string toolWindowId) => UiThread.Run(() => layout.Hide(toolWindowId));

    public bool IsVisible(string toolWindowId) => layout.IsShown(toolWindowId);

    /// <summary>Shows a tool window, or hides it when its area shows it.</summary>
    public void Toggle(string toolWindowId) => layout.Toggle(toolWindowId);

    /// <summary>Moves a window to another area, or to another place on its stripe.</summary>
    public void Move(string toolWindowId, DockSide area, int index = int.MaxValue) => layout.Move(toolWindowId, area, index);

    public void SetAvailable(string toolWindowId, bool available) => UiThread.Run(() => layout.SetAvailable(toolWindowId, available));

    public void SetBadge(string toolWindowId, string? badge) => UiThread.Run(() =>
    {
        badges[toolWindowId] = badge;
        if (items.TryGetValue(toolWindowId, out var item))
            item.Badge = badge;
    });

    /// <summary>Puts every window back on its default side, showing the default ones, and keeps the areas' sizes.</summary>
    public void Reset()
    {
        layout.Changed -= OnLayoutChanged;
        var previous = layout.Save();
        var unavailable = providers.Keys.Where(id => !layout.IsAvailable(id)).ToList();
        layout = CreateLayout(null);
        layout.Resize(DockSide.Left, previous.LeftSize);
        layout.Resize(DockSide.Right, previous.RightSize);
        layout.Resize(DockSide.Bottom, previous.BottomSize);
        foreach (var id in unavailable)
            layout.SetAvailable(id, available: false);
        OnLayoutChanged(this, EventArgs.Empty);
    }

    /// <summary>Remembers the arrangement for the next start.</summary>
    public void Save() => SessionStore.SaveToolWindows(layout.Save());

    public void Contribute(ICommandRegistry registry)
    {
        var order = 0;
        foreach (var definition in ToolWindows)
        {
            var id = "view." + definition.Id;
            registry.Add(
                new CommandDefinition(id, $"Show {definition.Title}", "View") { Icon = definition.Icon, KeyBinding = definition.KeyBinding },
                _ =>
                {
                    Toggle(definition.Id);
                    return Task.CompletedTask;
                },
                _ => layout.IsAvailable(definition.Id));
            registry.AddMenuItem(new MenuItemDefinition(Menus.View, id, "2-tools", order++));
        }
    }

    /// <summary>Sets the tooltips of the stripe buttons, with the key that shows each window when it has one.</summary>
    public void SetGestures(Func<string, string?> gestureOf)
    {
        foreach (var id in providers.Keys)
        {
            var item = Item(id);
            item.ToolTip = gestureOf("view." + id) is { } gesture ? $"{item.Title} ({gesture.Split(" | ")[0]})" : item.Title;
        }
    }

    private ToolWindowLayout CreateLayout(ToolWindowSession? saved)
    {
        var created = new ToolWindowLayout(ToolWindows, saved);
        created.Changed += OnLayoutChanged;
        return created;
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        SyncStripes();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SyncStripes()
    {
        isSynced = true;
        foreach (var (area, list) in stripes)
        {
            var wanted = layout.WindowsIn(area).Select(Item).ToList();
            if (!list.SequenceEqual(wanted))
            {
                list.Clear();
                foreach (var item in wanted)
                    list.Add(item);
            }

            foreach (var item in list)
                item.IsActive = layout.ShownIn(area) == item.Id;
        }
    }

    private ToolStripeItem Item(string id)
    {
        if (!items.TryGetValue(id, out var item))
        {
            var definition = providers[id].Definition;
            item = new ToolStripeItem(id, definition.Title, Icons.Find(definition.Icon) ?? Icons.PanelLeft) { Badge = badges.GetValueOrDefault(id) };
            items[id] = item;
        }

        return item;
    }
}
