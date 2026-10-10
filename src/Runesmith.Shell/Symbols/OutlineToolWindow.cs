using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Editor;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Views;

namespace Runesmith.Shell.Symbols;

/// <summary>The Outline panel: the symbols of the active file as a tree that follows the caret, with a filter and a choice of order.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class OutlineToolWindow(EditorService editors, DocumentSymbols symbols) : IToolWindowProvider
{
    public const string Id = "outline";

    public ToolWindowDefinition Definition { get; } = new(Id, "Outline", "list-tree", DockSide.Left) { Order = 2 };

    public Control CreateContent() => new OutlineView(editors, symbols);
}

/// <summary>The content of the Outline panel.</summary>
internal sealed class OutlineView : DockPanel
{
    private readonly EditorService editors;
    private readonly DocumentSymbols symbols;
    private readonly OutlineModel model = new();
    private readonly TextBox filter = new() { PlaceholderText = "Filter symbols", Classes = { "small" } };
    private readonly ToggleButton byName = Option("A-Z", "Sort by name");
    private readonly ToggleButton follow = new() { Classes = { "option" }, MinWidth = 26, IsChecked = true, Content = new SymbolIcon { Data = Icons.Crosshair, Size = 13 } };
    private readonly TreeView tree;
    private readonly EmptyState empty = new() { Icon = Icons.ListTree };
    private readonly DispatcherTimer caretTimer;
    private TextEditor? editor;
    private EditorSymbols? tracked;
    private bool selectingFromCaret;

    public OutlineView(EditorService editors, DocumentSymbols symbols)
    {
        this.editors = editors;
        this.symbols = symbols;
        ToolTip.SetTip(follow, "Follow the caret");
        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(4, 0, 0, 0), Children = { byName, follow } };
        var header = new DockPanel { Margin = new Thickness(10, 8, 10, 6) };
        DockPanel.SetDock(options, Dock.Right);
        header.Children.Add(options);
        header.Children.Add(filter);
        DockPanel.SetDock(header, Dock.Top);
        Children.Add(header);

        tree = new TreeView
        {
            ItemsSource = model.Roots,
            ItemTemplate = new FuncTreeDataTemplate<OutlineNode?>((node, _) => node is null ? new Panel() : Row(node), node => node?.Children ?? []),
        };
        tree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters = { new Setter(TreeViewItem.IsExpandedProperty, new Binding(nameof(OutlineNode.IsExpanded)) { Mode = BindingMode.TwoWay }) },
        });
        tree.SelectionChanged += (_, _) =>
        {
            if (!selectingFromCaret && tree.SelectedItem is OutlineNode node)
                GoTo(node, focus: false);
        };
        tree.AddHandler(TappedEvent, (_, _) =>
        {
            if (tree.SelectedItem is OutlineNode node)
                GoTo(node, focus: true);
        }, RoutingStrategies.Bubble);
        tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && tree.SelectedItem is OutlineNode node)
            {
                GoTo(node, focus: true);
                e.Handled = true;
            }
        };
        Children.Add(new Panel { Children = { tree, empty } });

        caretTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        caretTimer.Tick += (_, _) =>
        {
            caretTimer.Stop();
            SelectCaretSymbol();
        };
        filter.TextChanged += (_, _) => model.Filter = filter.Text ?? "";
        filter.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && model.Roots.Count > 0)
            {
                tree.SelectedItem = model.Roots[0];
                tree.Focus();
                e.Handled = true;
            }
        };
        byName.IsCheckedChanged += (_, _) => model.Sort = byName.IsChecked == true ? OutlineSort.Name : OutlineSort.Position;
        follow.IsCheckedChanged += (_, _) => SelectCaretSymbol();
        model.Changed += (_, _) => UpdateEmpty();
        editors.ActiveEditorChanged += (_, _) => Track(editors.Active);
        Track(editors.Active);
    }

    /// <summary>Gets the model, for tests.</summary>
    internal OutlineModel Model => model;

    /// <summary>Gets the tree, for tests.</summary>
    internal TreeView Tree => tree;

    private void Track(TextEditor? active)
    {
        if (ReferenceEquals(active, editor) && tracked is not null)
            return;

        if (editor is not null)
            editor.CaretMoved -= OnCaretMoved;
        if (tracked is not null)
            tracked.Changed -= OnSymbolsChanged;

        editor = active;
        tracked = active is null ? null : symbols.For(active);
        if (editor is not null)
            editor.CaretMoved += OnCaretMoved;
        if (tracked is not null)
            tracked.Changed += OnSymbolsChanged;

        model.SetSymbols(tracked?.Symbols, sameFile: false);
        SelectCaretSymbol();
    }

    private void OnSymbolsChanged(object? sender, EventArgs e)
    {
        model.SetSymbols(tracked?.Symbols);
        SelectCaretSymbol();
    }

    private void OnCaretMoved(object? sender, EventArgs e)
    {
        if (follow.IsChecked == true && !caretTimer.IsEnabled)
            caretTimer.Start();
    }

    private void SelectCaretSymbol()
    {
        if (follow.IsChecked != true || editor is null || model.NodeAt(editor.CaretOffset) is not { } node || ReferenceEquals(tree.SelectedItem, node))
            return;

        selectingFromCaret = true;
        try
        {
            OutlineModel.Reveal(node);
            tree.SelectedItem = node;
        }
        finally
        {
            selectingFromCaret = false;
        }

        Dispatcher.UIThread.Post(() => tree.ContainerFromItem(node)?.BringIntoView(), DispatcherPriority.Background);
    }

    private void GoTo(OutlineNode node, bool focus)
    {
        if (editor is not { } target)
            return;

        var span = node.Symbol.SelectionRange;
        target.Area.Select(new EditorSelection(span.Start, span.End), scrollIntoView: false);
        target.Area.ScrollIntoView(span.Start, center: true);
        if (focus)
            target.Area.Focus();
    }

    private void UpdateEmpty()
    {
        var hasRows = model.Roots.Count > 0;
        empty.IsVisible = !hasRows;
        tree.IsVisible = hasRows;
        if (hasRows)
            return;

        (empty.Title, empty.Hint) = editor is null
            ? ("No file open", "The outline shows the symbols of the active file.")
            : tracked?.HasProvider != true
                ? ("No outline", $"Nothing lists the symbols of {editor.Document.Name}. A language server or plugin for its language adds them.")
                : model.SymbolCount == 0
                    ? ("No symbols", $"{editor.Document.Name} declares nothing to list.")
                    : ("Nothing matches", "No symbol matches the filter.");
    }

    private static StackPanel Row(OutlineNode node)
    {
        var symbol = node.Symbol;
        var name = new TextBlock { Text = symbol.Name, VerticalAlignment = VerticalAlignment.Center };
        if (symbol.IsDeprecated)
            name.TextDecorations = TextDecorations.Strikethrough;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { SymbolBreadcrumbs.KindIcon(symbol.Kind, 14), name } };
        if (symbol.Detail is { } detail)
            row.Children.Add(new TextBlock { Text = detail.ReplaceLineEndings(" "), Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 400 });
        ToolTip.SetTip(row, $"{symbol.Name} ({SymbolKinds.Name(symbol.Kind)})");
        return row;
    }

    private static ToggleButton Option(string text, string tip)
    {
        var button = new ToggleButton
        {
            Classes = { "option" },
            MinWidth = 26,
            Content = new TextBlock { Text = text, FontFamily = new FontFamily(EditorOptions.BundledFontFamily), FontSize = 11 },
        };
        ToolTip.SetTip(button, tip);
        return button;
    }
}
