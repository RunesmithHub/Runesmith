using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Runesmith.Composition;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Tests.Plugins;
using Runesmith.Shell.TreeViews;

namespace Runesmith.Shell.Tests.TreeViews;

public sealed class TreeViewTests
{
    private static readonly TreeViewDefinition Definition = new(new ToolWindowDefinition("acme.files", "Files", "folder", DockSide.Left)) { EmptyMessage = "No files" };

    [Fact]
    public async Task ChildrenLoadWhenAnItemIsFirstExpandedAndRowsFollowTheTree()
    {
        var provider = new FakeTree();
        using var model = new TreeViewModel(provider, Definition, _ => { });

        await model.LoadAsync();
        Assert.Equal(["src", "README.md"], Labels(model));
        Assert.Equal(["(root)"], provider.Asked);

        await model.ExpandAsync(model.Roots[0]);
        Assert.Equal(["src", "  app", "    Program.cs", "  lib", "README.md"], Labels(model));
        Assert.Equal(["(root)", "src", "app"], provider.Asked);

        model.Collapse(model.Roots[0]);
        Assert.Equal(["src", "README.md"], Labels(model));
        await model.ExpandAsync(model.Roots[0]);
        Assert.Equal(3, provider.Asked.Count);
        Assert.Equal(5, model.Rows.Count);
    }

    [Fact]
    public async Task RefreshingKeepsExpandedItemsExpandedAndReloadsOneItemOnItsOwn()
    {
        var provider = new FakeTree();
        using var model = new TreeViewModel(provider, Definition, _ => { });
        await model.LoadAsync();
        await model.ExpandAsync(model.Roots[0]);
        await model.ExpandAsync(model.Rows.First(n => n.Item.Label == "lib"));

        provider.Children["lib"] = [Leaf("Util.cs")];
        await model.RefreshAsync(null);
        Assert.Equal(["src", "  app", "    Program.cs", "  lib", "    Util.cs", "README.md"], Labels(model));

        provider.Children["app"] = [Leaf("Program.cs"), Leaf("Startup.cs")];
        provider.Asked.Clear();
        await model.RefreshAsync(Folder("app") with { Description = "2 files" });
        Assert.Equal(["app"], provider.Asked);
        Assert.Equal("2 files", model.Rows[1].Item.Description);
        Assert.Equal(["src", "  app", "    Program.cs", "    Startup.cs", "  lib", "    Util.cs", "README.md"], Labels(model));
    }

    [Fact]
    public async Task AProviderThatFailsShowsAnErrorRowAndIsReportedNotThrown()
    {
        var provider = new FakeTree { FailOn = "src" };
        var log = new List<string>();
        using var model = new TreeViewModel(provider, Definition, log.Add);
        await model.LoadAsync();

        await model.ExpandAsync(model.Roots[0]);

        Assert.Equal(TreeNodeKind.Error, model.Rows[1].Kind);
        Assert.Contains("broken", model.Rows[1].Item.Label, StringComparison.Ordinal);
        Assert.Contains("children of src", Assert.Single(log), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFilterShowsMatchingLoadedItemsWithTheItemsThatHoldThem()
    {
        using var model = new TreeViewModel(new FakeTree(), Definition, _ => { });
        await model.LoadAsync();
        await model.ExpandAsync(model.Roots[0]);
        await model.ExpandAsync(model.Rows.First(n => n.Item.Label == "lib"));

        model.SetFilter("prog");
        Assert.Equal(["src", "  app", "    Program.cs"], Labels(model));
        Assert.Equal([0, 1, 2, 3], model.Rows[2].FilterMatches);

        model.SetFilter("");
        Assert.Equal(["src", "  app", "    Program.cs", "  lib", "README.md"], Labels(model));
        Assert.Null(model.Rows[2].FilterMatches);
    }

    [Fact]
    public Task RevealLoadsThePathToAnItemThatWasNotLoadedAndSelectsIt() => HeadlessSession.Value.Dispatch(async () =>
    {
        var provider = new FakeTree();
        using var model = new TreeViewModel(provider, Definition, _ => { });
        var revealed = new List<TreeNode>();
        model.RevealRequested += (_, e) => revealed.Add(e.Node);

        Assert.True(await model.RevealAsync(Leaf("Program.cs"), focus: true));

        Assert.Equal(["src", "  app", "    Program.cs", "  lib", "README.md"], Labels(model));
        Assert.Equal("Program.cs", Assert.Single(model.Selection).Label);
        Assert.Equal("Program.cs", Assert.Single(revealed).Item.Label);
        Assert.False(await model.RevealAsync(Leaf("Missing.cs")));
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task SelectionAndCheckboxesRaiseEventsAndAFailingHandlerIsReported()
    {
        var log = new List<string>();
        using var model = new TreeViewModel(new FakeTree(), Definition, log.Add);
        await model.LoadAsync();
        var checkedItems = new List<(string, bool)>();
        model.SelectionChanged += (_, _) => throw new InvalidOperationException("handler bug");
        model.ItemChecked += (_, e) => checkedItems.Add((e.Item.Label, e.IsChecked));

        model.SetSelection([model.Roots[1]]);
        model.SetChecked(model.Roots[1], true);

        Assert.Equal("README.md", Assert.Single(model.Selection).Label);
        Assert.Equal([("README.md", true)], checkedItems);
        Assert.True(model.Roots[1].Item.IsChecked);
        Assert.Contains("handler bug", Assert.Single(log), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyThePluginThatExportsATreeViewGetsIt()
    {
        var owner = TestCallers.Plugin("acme");
        PluginInfo? caller = owner;
        var service = new TreeViewService([new FakeTree()], () => caller, _ => owner, _ => { });

        Assert.Equal("acme.files", service.GetTreeView("acme.files").Id);
        caller = null;
        Assert.Same(service.GetTreeView("acme.files"), service.GetTreeView("acme.files"));
        caller = TestCallers.Plugin("other");
        Assert.Throws<UnauthorizedAccessException>(() => service.GetTreeView("acme.files"));
        Assert.Throws<KeyNotFoundException>(() => service.GetTreeView("nope"));
    }

    [Fact]
    public void ATreeViewWhoseDefinitionFailsIsLeftOutAndReported()
    {
        var log = new List<string>();
        var service = new TreeViewService([new BrokenDefinition(), new FakeTree()], () => null, _ => TestCallers.Plugin("acme"), log.Add);

        Assert.Equal(["acme.files"], service.Definitions.Select(d => d.Id));
        Assert.Contains("acme", Assert.Single(log), StringComparison.Ordinal);
    }

    [Fact]
    public Task ALargeTreeDrawsOnlyTheRowsInView() => HeadlessSession.Value.Dispatch(async () =>
    {
        var provider = new FakeTree();
        provider.Children["lib"] = [.. Enumerable.Range(0, 10_000).Select(i => Leaf($"File{i}.cs"))];
        using var model = new TreeViewModel(provider, Definition, _ => { });
        var view = new TreeViewControl(model, null, null);
        var window = new Window { Width = 400, Height = 500, Content = view };
        window.Show();
        await model.LoadAsync();
        await model.ExpandAsync(model.Roots[0]);
        await model.ExpandAsync(model.Rows.First(n => n.Item.Label == "lib"));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(10_005, model.Rows.Count);
        Assert.InRange(view.List.GetRealizedContainers().Count(), 1, 60);
        view.List.ScrollIntoView(9_000);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(view.List.GetRealizedContainers(), c => c.DataContext is TreeNode { Item.Label: "File8996.cs" });
        window.Close();
        return true;
    }, TestContext.Current.CancellationToken);

    [Fact]
    public Task KeysExpandCollapseAndOpenItemsAndTheContextMenuFollowsTheContextValue() => HeadlessSession.Value.Dispatch(async () =>
    {
        var commands = new CommandService([], new Lazy<INotificationService>(() => null!));
        var opened = new List<object?>();
        commands.Add(new CommandDefinition("acme.open", "Open", "Acme"), argument =>
        {
            opened.Add(argument);
            return Task.CompletedTask;
        });
        commands.Add(new CommandDefinition("acme.delete", "Delete", "Acme"), _ => Task.CompletedTask, a => a is TreeViewTarget { ContextValue: "file" });
        commands.Add(new CommandDefinition("acme.newFile", "New File", "Acme") { Icon = "file-plus" }, _ => Task.CompletedTask);
        commands.AddMenuItem(new MenuItemDefinition(TreeViewMenus.Item("acme.files"), "acme.delete"));
        commands.AddMenuItem(new MenuItemDefinition(TreeViewMenus.Title("acme.files"), "acme.newFile"));
        using var model = new TreeViewModel(new FakeTree(), Definition, _ => { });
        var view = new TreeViewControl(model, commands, null);
        var window = new Window { Width = 400, Height = 500, Content = view };
        window.Show();
        await model.LoadAsync();
        Dispatcher.UIThread.RunJobs();

        view.List.SelectedIndex = 0;
        Press(view, Key.Right);
        await WaitAsync(() => model.Rows.Count == 5);
        Press(view, Key.Left);
        Assert.Equal(2, model.Rows.Count);
        view.List.SelectedIndex = 1;
        Press(view, Key.Enter);
        var target = Assert.IsType<TreeViewTarget>(Assert.Single(opened));
        Assert.Equal("README.md", target.Item?.Label);

        Assert.Single(MenuBuilder.ContributedItems(commands, TreeViewMenus.Item("acme.files"), new TreeViewTarget("acme.files", model.Roots[1].Item)).OfType<MenuItem>());
        Assert.Empty(MenuBuilder.ContributedItems(commands, TreeViewMenus.Item("acme.files"), new TreeViewTarget("acme.files", model.Roots[0].Item)).OfType<MenuItem>());
        Assert.Contains(((StackPanel)view.HeaderActions).Children.OfType<Button>(), b => (string?)b.Tag == "acme.newFile");
        window.Close();
        return true;
    }, TestContext.Current.CancellationToken);

    private static void Press(TreeViewControl view, Key key) =>
        view.List.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = view.List });

    private static async Task WaitAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "The condition was not met in time.");
            await Task.Delay(10);
        }
    }

    private static string[] Labels(TreeViewModel model) => [.. model.Rows.Select(n => new string(' ', n.Depth * 2) + n.Item.Label)];

    private static TreeItem Folder(string name) => new(name) { Id = name, CollapsibleState = TreeItemCollapsibleState.Collapsed, IconPath = "/repo/" + name };

    private static TreeItem Leaf(string name) => new(name) { Id = name, ContextValue = "file", CommandId = "acme.open", IsChecked = name == "README.md" ? false : null };

    private sealed class FakeTree : ITreeViewProvider
    {
        public Dictionary<string, IReadOnlyList<TreeItem>> Children { get; } = new()
        {
            [""] = [Folder("src") with { CommandId = null }, Leaf("README.md")],
            ["src"] = [Folder("app") with { CollapsibleState = TreeItemCollapsibleState.Expanded }, Folder("lib")],
            ["app"] = [Leaf("Program.cs")],
            ["lib"] = [],
        };

        public List<string> Asked { get; } = [];

        public string? FailOn { get; init; }

        public TreeViewDefinition Definition => TreeViewTests.Definition;

        public async Task<IReadOnlyList<TreeItem>> GetChildrenAsync(TreeItem? parent, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Asked.Add(parent?.Label ?? "(root)");
            if (FailOn is not null && parent?.Label == FailOn)
                throw new IOException("broken");
            return Children.GetValueOrDefault(parent?.Id ?? "") ?? [];
        }

        public Task<TreeItem?> GetParentAsync(TreeItem item, CancellationToken cancellationToken)
        {
            foreach (var (key, children) in Children)
            {
                if (children.Any(c => c.Id == item.Id))
                    return Task.FromResult<TreeItem?>(key.Length == 0 ? null : Children.Values.SelectMany(c => c).First(c => c.Id == key));
            }

            return Task.FromResult<TreeItem?>(null);
        }
    }

    private sealed class BrokenDefinition : ITreeViewProvider
    {
        public TreeViewDefinition Definition => throw new InvalidOperationException("no definition");

        public Task<IReadOnlyList<TreeItem>> GetChildrenAsync(TreeItem? parent, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TreeItem>>([]);
    }
}
