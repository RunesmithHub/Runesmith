using System.ComponentModel;
using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Services;
using Runesmith.Shell.Views;
using Runesmith.Workspace.Files;

namespace Runesmith.Shell.ToolWindows;

/// <summary>The Explorer: the open folder's files and folders, to open, create, rename and delete them.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class ExplorerToolWindow(
    Lazy<IToolWindowManager> toolWindows,
    FileTree tree,
    IWorkspace workspace,
    EditorService editors,
    CommandService commands,
    FileIcons icons,
    NotificationService notifications) : IToolWindowProvider
{
    public const string Id = "explorer";

    public ToolWindowDefinition Definition { get; } = new(Id, "Explorer", "folder-open", DockSide.Left) { IsVisibleByDefault = true, KeyBinding = "Ctrl+Shift+E" };

    private ExplorerView? view;

    public Control CreateContent() => view = new ExplorerView(tree, workspace, editors, commands, icons, notifications);

    /// <summary>Shows the Explorer with a file or folder selected.</summary>
    public void Reveal(string path)
    {
        toolWindows.Value.Show(Id);
        Dispatcher.UIThread.Post(() => view?.Reveal(path), DispatcherPriority.Background);
    }

    private sealed class ExplorerView : DockPanel, IToolWindowHeader
    {
        private readonly FileTree tree;
        private readonly IWorkspace workspace;
        private readonly EditorService editors;
        private readonly FileIcons icons;
        private readonly NotificationService notifications;
        private readonly CommandService commands;
        private readonly TreeView view;
        private readonly EmptyState empty;
        private readonly StackPanel actions;

        public ExplorerView(FileTree tree, IWorkspace workspace, EditorService editors, CommandService commands, FileIcons icons, NotificationService notifications)
        {
            this.tree = tree;
            this.workspace = workspace;
            this.editors = editors;
            this.icons = icons;
            this.notifications = notifications;
            this.commands = commands;

            actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Children =
                {
                    Action(Icons.FilePlus, "New file", () => _ = NewAsync(SelectedFolder(), folder: false)),
                    Action(Icons.FolderPlus, "New folder", () => _ = NewAsync(SelectedFolder(), folder: true)),
                    Action(Icons.Refresh, "Refresh", () => tree.Root?.Refresh()),
                    Action(Icons.ChevronsUp, "Collapse all", CollapseAll),
                },
            };
            view = new TreeView
            {
                Classes = { "files" },
                ItemTemplate = new FuncTreeDataTemplate<FileTreeNode?>((node, _) => node is null ? new Panel() : Row(node), node => node?.Children ?? []),
            };
            view.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
            {
                Setters = { new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(FileTreeNode.IsExpanded)) { Mode = BindingMode.TwoWay }) },
            });
            view.DoubleTapped += (_, _) => OpenSelected(focus: true);
            view.KeyDown += OnKeyDown;
            view.ContextRequested += (_, _) => view.ContextMenu = ContextMenuFor(view.SelectedItem as FileTreeNode);
            // Avalonia opens a context menu only from a control that had one when the request began, so each starts with an empty one.
            view.ContextMenu = new ContextMenu();

            empty = new EmptyState
            {
                Icon = Icons.FolderOpen,
                Title = "No folder open",
                Hint = "Open a folder to see its files here.",
                ActionText = "Open Folder",
                ActionCommand = new RelayCommand(() => _ = commands.ExecuteAsync(CommandIds.OpenFolder)),
            };
            Children.Add(new Panel { Children = { view, empty } });

            tree.PropertyChanged += OnTreeChanged;
            editors.ActiveEditorChanged += (_, _) => RevealActive();
            icons.Changed += (_, _) => UpdateIcons();
            Update();
        }

        public event EventHandler? HeaderChanged;

        public string? HeaderTitle => tree.Root is null ? null : workspace.Name;

        public Control HeaderActions => actions;

        private void OnTreeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileTree.Root))
                UiThread.Run(Update);
        }

        private void Update()
        {
            view.ItemsSource = tree.Root?.Children;
            empty.IsVisible = tree.Root is null;
            view.IsVisible = tree.Root is not null;
            actions.IsVisible = tree.Root is not null;
            HeaderChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Reveal(string path)
        {
            if (tree.Reveal(path) is { } node)
            {
                view.SelectedItem = node;
                view.ScrollIntoView(node);
                view.Focus();
            }
        }

        private void RevealActive()
        {
            if (editors.Active?.Document.FilePath is { } path && tree.Reveal(path) is { } node)
            {
                view.SelectedItem = node;
                view.ScrollIntoView(node);
            }
        }

        private void ShowIcon(SymbolIcon icon, FileTreeNode node) =>
            icons.For(node.FullPath, node.IsDirectory).ApplyTo(icon, node.IsDirectory ? "AccentBrush" : "TextSecondaryBrush");

        private void UpdateIcons()
        {
            foreach (var icon in view.GetVisualDescendants().OfType<SymbolIcon>())
            {
                if (icon.Tag is FileTreeNode node)
                    ShowIcon(icon, node);
            }
        }

        private StackPanel Row(FileTreeNode node)
        {
            var icon = new SymbolIcon { Size = 14, Margin = new Thickness(0, 0, 6, 0), Tag = node };
            ShowIcon(icon, node);
            var name = new TextBlock { Text = node.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            if (node.Name.StartsWith('.'))
                name[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");
            ToolTip.SetTip(name, node.FullPath);
            return new StackPanel { Orientation = Orientation.Horizontal, Children = { icon, name }, IsVisible = !node.IsPlaceholder };
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Enter:
                    OpenSelected(focus: true);
                    break;
                case Key.F2 when view.SelectedItem is FileTreeNode node:
                    _ = RenameAsync(node);
                    break;
                case Key.Delete when view.SelectedItem is FileTreeNode node:
                    _ = DeleteAsync(node);
                    break;
                default:
                    return;
            }

            e.Handled = true;
        }

        private void OpenSelected(bool focus)
        {
            if (view.SelectedItem is FileTreeNode { IsDirectory: false, IsPlaceholder: false } node)
                _ = editors.OpenAsync(node.FullPath, activate: focus);
        }

        private string? SelectedFolder() => view.SelectedItem switch
        {
            FileTreeNode { IsDirectory: true } folder => folder.FullPath,
            FileTreeNode file => Path.GetDirectoryName(file.FullPath),
            _ => workspace.RootPath,
        };

        private void CollapseAll()
        {
            void Collapse(IEnumerable<FileTreeNode> nodes)
            {
                foreach (var node in nodes.Where(n => n.IsDirectory))
                {
                    if (node.IsLoaded)
                        Collapse(node.Children);
                    node.IsExpanded = false;
                }
            }

            if (tree.Root is { } root)
                Collapse(root.Children);
        }

        private ContextMenu ContextMenuFor(FileTreeNode? node)
        {
            var parent = node is null ? workspace.RootPath : node.IsDirectory ? node.FullPath : Path.GetDirectoryName(node.FullPath);
            var items = new List<Control>
            {
                MenuItem("New File", Icons.FilePlus, () => _ = NewAsync(parent, folder: false)),
                MenuItem("New Folder", Icons.FolderPlus, () => _ = NewAsync(parent, folder: true)),
            };
            if (node is not null)
            {
                items.Add(new Separator());
                if (!node.IsDirectory)
                    items.Add(MenuItem("Open", Icons.FileCode, () => _ = editors.OpenAsync(node.FullPath)));
                items.Add(MenuItem("Rename", Icons.PenLine, () => _ = RenameAsync(node), "F2"));
                items.Add(MenuItem("Delete", Icons.Trash, () => _ = DeleteAsync(node), "Delete"));
                items.Add(new Separator());
                items.Add(MenuItem("Copy Path", Icons.Copy, () => _ = CopyAsync(node.FullPath)));
                items.Add(MenuItem("Copy Relative Path", null, () => _ = CopyAsync(Path.GetRelativePath(workspace.RootPath ?? "", node.FullPath))));
                items.Add(MenuItem(OperatingSystem.IsMacOS() ? "Reveal in Finder" : "Reveal in File Manager", Icons.ExternalLink, () => Launcher.Reveal(node.FullPath)));
                items.AddRange(MenuBuilder.ContributedItems(commands, ContextMenus.Explorer, new ContextMenuTarget(node.FullPath, node.IsDirectory)));
            }

            return new ContextMenu { ItemsSource = items };
        }

        private async Task NewAsync(string? parent, bool folder)
        {
            if (parent is null)
                return;

            var name = await Prompt.AskAsync(notifications.Dialogs, folder ? "New Folder" : "New File", $"Name, in {Path.GetFileName(parent)}", "",
                n => Prompt.ValidateFileName(n, parent));
            if (name is null)
                return;

            var path = Path.Combine(parent, name);
            try
            {
                if (folder)
                {
                    Directory.CreateDirectory(path);
                }
                else
                {
                    await File.WriteAllBytesAsync(path, []);
                    await editors.OpenAsync(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                notifications.Notify(NotificationKind.Error, $"{name} could not be created", exception.Message);
            }
        }

        private async Task CopyAsync(string text)
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
                await clipboard.SetTextAsync(text);
        }

        private async Task RenameAsync(FileTreeNode node)
        {
            var folder = Path.GetDirectoryName(node.FullPath)!;
            var extension = node.IsDirectory ? 0 : Path.GetExtension(node.Name).Length;
            var name = await Prompt.AskAsync(notifications.Dialogs, "Rename", $"New name for {node.Name}", node.Name,
                n => Prompt.ValidateFileName(n, folder, except: node.Name), (0, node.Name.Length - extension));
            if (name is null || name == node.Name)
                return;

            var target = Path.Combine(folder, name);
            try
            {
                if (node.IsDirectory)
                    Directory.Move(node.FullPath, target);
                else
                    File.Move(node.FullPath, target);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                notifications.Notify(NotificationKind.Error, $"{node.Name} could not be renamed", exception.Message);
            }
        }

        private async Task DeleteAsync(FileTreeNode node)
        {
            var what = node.IsDirectory ? $"the folder {node.Name} and everything in it" : node.Name;
            if (!await notifications.ConfirmAsync("Delete?", $"Delete {what}? This cannot be undone.", "Delete", isDestructive: true))
                return;

            try
            {
                if (node.IsDirectory)
                    Directory.Delete(node.FullPath, recursive: true);
                else
                    File.Delete(node.FullPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                notifications.Notify(NotificationKind.Error, $"{node.Name} could not be deleted", exception.Message);
            }
        }

        private static MenuItem MenuItem(string header, Geometry? icon, Action action, string? gesture = null)
        {
            var item = new MenuItem { Header = header, Icon = icon is null ? null : new SymbolIcon { Data = icon, Size = 14 }, InputGesture = gesture is null ? null : KeyGesture.Parse(gesture) };
            item.Click += (_, _) => action();
            return item;
        }

        private static Button Action(Geometry icon, string tip, Action action)
        {
            var button = new Button { Classes = { "icon", "small" }, Content = new SymbolIcon { Data = icon, Size = 16 } };
            ToolTip.SetTip(button, tip);
            button.Click += (_, _) => action();
            return button;
        }
    }
}
