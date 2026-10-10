using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Editor;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Editors;

namespace Runesmith.Shell.Navigation;

/// <summary>The References panel: the places Find References and Go to Implementation found, by file, with their lines.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class ReferencesToolWindow(EditorService editors, IDocumentService documents, IWorkspace workspace, FileIcons icons, Lazy<IToolWindowManager> toolWindows)
    : IToolWindowProvider, IDisposable
{
    public const string Id = "references";

    private CancellationTokenSource? showing;

    public ToolWindowDefinition Definition { get; } = new(Id, "References", "link", DockSide.Bottom) { Order = 3 };

    /// <summary>Gets what the panel lists.</summary>
    public ReferencesModel Model { get; } = new();

    public void Dispose()
    {
        showing?.Cancel();
        showing?.Dispose();
        showing = null;
    }

    public Control CreateContent() => new ReferencesView(Model, path => icons.For(path), (location, focus) => editors.RevealAsync(location.FilePath, location.Start, location.End, focus));

    /// <summary>Lists the places of a search and shows the panel.</summary>
    public async Task ShowAsync(string title, string noun, IReadOnlyList<DocumentLocation> locations)
    {
        showing?.Cancel();
        showing?.Dispose();
        showing = new CancellationTokenSource();
        await Model.ShowAsync(title, noun, locations, path => documents.Find(path)?.Buffer.Current, workspace.RootPath, showing.Token);
        toolWindows.Value.Show(Id);
    }
}

/// <summary>The content of the References panel.</summary>
internal sealed class ReferencesView : DockPanel
{
    private readonly ReferencesModel model;
    private readonly Func<string, ThemedIcon> iconOf;
    private readonly Func<DocumentLocation, bool, Task> reveal;
    private readonly TextBlock title = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock summary = new() { Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    private readonly TreeView tree;
    private readonly EmptyState empty = new()
    {
        Icon = Icons.Link,
        Title = "No references",
        Hint = "Find References (Shift+F12) and Go to Implementation (Ctrl+F12) list what they find here.",
    };

    public ReferencesView(ReferencesModel model, Func<string, ThemedIcon> iconOf, Func<DocumentLocation, bool, Task> reveal)
    {
        this.model = model;
        this.iconOf = iconOf;
        this.reveal = reveal;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 6), Children = { title, summary } };
        DockPanel.SetDock(header, Dock.Top);
        Children.Add(header);

        tree = new TreeView
        {
            ItemsSource = model.Groups,
            ItemTemplate = new FuncTreeDataTemplate<object?>((item, _) => item is null ? new Panel() : Row(item), item => item is ReferenceGroup group ? group.Items : []),
        };
        tree.ContainerPrepared += (_, e) =>
        {
            if (e.Container is TreeViewItem item && item.DataContext is ReferenceGroup)
                item.IsExpanded = true;
        };
        tree.SelectionChanged += (_, _) =>
        {
            if (tree.SelectedItem is ReferenceItem item)
                _ = reveal(item.Location, false);
        };
        tree.AddHandler(TappedEvent, (_, _) => Open(), RoutingStrategies.Bubble);
        tree.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Open();
                e.Handled = true;
            }
            else if (e.Key == Key.F4)
            {
                tree.SelectedItem = model.Step(tree.SelectedItem as ReferenceItem, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                e.Handled = true;
            }
        };
        Children.Add(new Panel { Children = { tree, empty } });
        model.Changed += (_, _) => Update();
        Update();
    }

    /// <summary>Gets the tree, for tests.</summary>
    internal TreeView Tree => tree;

    private void Open()
    {
        if (tree.SelectedItem is ReferenceItem item)
            _ = reveal(item.Location, true);
    }

    private void Update()
    {
        title.Text = model.Title;
        summary.Text = model.Summary;
        var hasRows = model.Groups.Count > 0;
        empty.IsVisible = !hasRows;
        tree.IsVisible = hasRows;
        Children[0].IsVisible = hasRows;
        if (hasRows)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => tree.ContainerFromItem(model.Groups[0])?.BringIntoView(), Avalonia.Threading.DispatcherPriority.Background);
    }

    private StackPanel Row(object item)
    {
        if (item is ReferenceGroup group)
        {
            var fileIcon = new SymbolIcon { Size = 14, Margin = new Thickness(0, 0, 6, 0) };
            iconOf(group.FilePath).ApplyTo(fileIcon, "TextSecondaryBrush");
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    fileIcon,
                    new TextBlock { Text = group.Name, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = group.Folder, Classes = { "caption", "muted" }, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
                    new Badge { Content = group.Items.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center },
                },
            };
        }

        var reference = (ReferenceItem)item;
        var line = new TextBlock { FontFamily = new FontFamily(EditorOptions.BundledFontFamily), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var preview = reference.Preview;
        var start = Math.Clamp(reference.MatchStart, 0, preview.Length);
        var end = Math.Clamp(start + reference.MatchLength, start, preview.Length);
        line.Inlines!.Add(new Run(preview[..start]));
        var hit = new Run(preview[start..end]) { FontWeight = FontWeight.Bold };
        hit[!TextElement.BackgroundProperty] = new DynamicResourceExtension("AccentSubtleBrush");
        line.Inlines.Add(hit);
        line.Inlines.Add(new Run(preview[end..]));
        var number = new TextBlock
        {
            Text = (reference.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Classes = { "caption", "muted" },
            MinWidth = 32,
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { number, line } };
    }
}
