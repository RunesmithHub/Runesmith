using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Editor;
using Runesmith.Sdk.Languages;
using Runesmith.Shell.Symbols;

namespace Runesmith.Shell.Views;

/// <summary>The symbols around the caret, after the file's path under the tabs, such as <c>Program › Main</c>. Clicking one lists the symbols
/// beside it to go to.</summary>
internal sealed class SymbolBreadcrumbs : StackPanel
{
    private readonly TextEditor editor;
    private readonly EditorSymbols symbols;
    private readonly DispatcherTimer timer;
    private IReadOnlyList<DocumentSymbol> shown = [];

    public SymbolBreadcrumbs(TextEditor editor, EditorSymbols symbols)
    {
        this.editor = editor;
        this.symbols = symbols;
        Orientation = Orientation.Horizontal;
        VerticalAlignment = VerticalAlignment.Center;
        ClipToBounds = true;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Update();
        };
        editor.CaretMoved += (_, _) =>
        {
            if (!timer.IsEnabled)
                timer.Start();
        };
        symbols.Changed += (_, _) => Update();
        Update();
    }

    /// <summary>Gets the names shown, for tests.</summary>
    internal IReadOnlyList<string> Names => [.. shown.Select(s => s.Name)];

    private void Update()
    {
        var path = symbols.PathAt(editor.CaretOffset);
        if (path.Count == shown.Count && path.Zip(shown).All(pair => pair.First.Name == pair.Second.Name && pair.First.Kind == pair.Second.Kind))
        {
            shown = path;
            return;
        }

        shown = path;
        Children.Clear();
        IReadOnlyList<DocumentSymbol> level = symbols.Symbols ?? [];
        for (var i = 0; i < path.Count; i++)
        {
            Children.Add(new SymbolIcon { Data = Icons.ChevronRight, Size = 12, Classes = { "crumb-separator" }, VerticalAlignment = VerticalAlignment.Center });
            Children.Add(Crumb(path[i], level, i == path.Count - 1));
            level = path[i].Children;
        }
    }

    private Button Crumb(DocumentSymbol symbol, IReadOnlyList<DocumentSymbol> siblings, bool isLast)
    {
        var icon = KindIcon(symbol.Kind, 13);
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { icon, new TextBlock { Text = symbol.Name, VerticalAlignment = VerticalAlignment.Center } },
        };
        var button = new Button { Classes = { "crumb" }, Content = content, Focusable = false };
        button.Classes.Set("current", isLast);
        ToolTip.SetTip(button, symbol.Detail is { } detail ? $"{symbol.Name}  {detail}" : symbol.Name);
        button.Flyout = MenuFlyouts.Dynamic(menu =>
        {
            foreach (var sibling in siblings.OrderBy(s => s.Range.Start).Take(200))
            {
                var item = new MenuItem { Header = sibling.Name, Icon = KindIcon(sibling.Kind, 14) };
                if (sibling.Range == symbol.Range && sibling.Name == symbol.Name)
                    item.FontWeight = Avalonia.Media.FontWeight.SemiBold;
                item.Click += (_, _) => GoTo(sibling);
                menu.Items.Add(item);
            }
        }, PlacementMode.BottomEdgeAlignedLeft);
        return button;
    }

    private void GoTo(DocumentSymbol symbol)
    {
        var span = symbol.SelectionRange;
        editor.Area.Select(new EditorSelection(span.Start, span.End), scrollIntoView: false);
        editor.Area.ScrollIntoView(span.Start, center: true);
        editor.Area.Focus();
    }

    internal static SymbolIcon KindIcon(SymbolKind kind, double size)
    {
        var icon = new SymbolIcon { Data = SymbolKinds.Icon(kind), Size = size, VerticalAlignment = VerticalAlignment.Center };
        icon[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(SymbolKinds.ColorKey(kind));
        return icon;
    }
}
