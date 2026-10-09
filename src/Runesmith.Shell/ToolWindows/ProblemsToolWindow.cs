using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Editors;

namespace Runesmith.Shell.ToolWindows;

/// <summary>The Problems panel: every error and warning that language analyzers, language servers and builds report, by file.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class ProblemsToolWindow(IDiagnosticService diagnostics, IWorkspace workspace, EditorService editors, Lazy<IToolWindowManager> toolWindows) : IToolWindowProvider
{
    public const string Id = "problems";

    public ToolWindowDefinition Definition { get; } = new(Id, "Problems", "alert-triangle", DockSide.Bottom) { IsVisibleByDefault = true, KeyBinding = "Ctrl+Shift+M" };

    public Control CreateContent()
    {
        var tree = new TreeView { ItemTemplate = new FuncTreeDataTemplate<object?>((item, _) => item is null ? new Panel() : Row(item), item => item is FileGroup group ? group.Problems : []) };
        var empty = new EmptyState { Icon = Icons.CheckCircle, Title = "No problems", Hint = "Errors and warnings from the language analyzers and builds show here." };
        tree.SelectionChanged += (_, _) =>
        {
            if (tree.SelectedItem is Diagnostic problem)
                _ = editors.JumpToAsync(problem.FilePath, problem.Start);
        };

        void Refresh()
        {
            var groups = diagnostics.All
                .GroupBy(d => d.FilePath)
                .Select(g => new FileGroup(g.Key, workspace.RootPath is { } root ? Path.GetRelativePath(root, g.Key) : g.Key,
                    [.. g.OrderBy(d => d.Severity).ThenBy(d => d.Start.Line).ThenBy(d => d.Start.Column)]))
                .OrderBy(g => g.Problems.Min(p => p.Severity))
                .ThenBy(g => g.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            tree.ItemsSource = groups;
            empty.IsVisible = groups.Count == 0;
            foreach (var item in tree.GetRealizedContainers().OfType<TreeViewItem>())
                item.IsExpanded = true;
        }

        tree.ContainerPrepared += (_, e) =>
        {
            if (e.Container is TreeViewItem item && item.DataContext is FileGroup)
                item.IsExpanded = true;
        };
        // Servers publish problems after almost every key press; rebuilding the tree each time would cost the editor frames.
        var refresh = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var root = new Panel { Children = { tree, empty } };
        var isStale = false;
        refresh.Tick += (_, _) =>
        {
            refresh.Stop();
            if (root.IsAttachedToVisualTree())
                Refresh();
            else
                isStale = true;
        };
        diagnostics.Changed += (_, _) =>
        {
            refresh.Stop();
            refresh.Start();
        };
        root.AttachedToVisualTree += (_, _) =>
        {
            if (isStale)
            {
                isStale = false;
                Refresh();
            }
        };
        Refresh();
        return root;
    }

    /// <summary>Shows the number of errors and warnings on the window's stripe button; called once at startup.</summary>
    public void ShowCounts()
    {
        // Problems change after almost every key press; counting them a few times a second is enough for a badge.
        var refresh = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        refresh.Tick += (_, _) =>
        {
            refresh.Stop();
            var count = diagnostics.All.Count(d => d.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning);
            toolWindows.Value.SetBadge(Id, count == 0 ? null : count > 99 ? "99+" : count.ToString(System.Globalization.CultureInfo.CurrentCulture));
        };
        diagnostics.Changed += (_, _) => UiThread.Run(() =>
        {
            if (!refresh.IsEnabled)
                refresh.Start();
        });
        refresh.Start();
    }

    private sealed record FileGroup(string Path, string RelativePath, IReadOnlyList<Diagnostic> Problems);

    private static StackPanel Row(object item)
    {
        if (item is FileGroup group)
        {
            var folder = System.IO.Path.GetDirectoryName(group.RelativePath);
            return new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new SymbolIcon { Data = Icons.FileCode, Size = 14 },
                    new TextBlock { Text = System.IO.Path.GetFileName(group.Path), FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center },
                    new TextBlock { Text = folder, Classes = { "caption", "muted" }, VerticalAlignment = VerticalAlignment.Center },
                    new Badge { Content = group.Problems.Count.ToString(System.Globalization.CultureInfo.CurrentCulture), VerticalAlignment = VerticalAlignment.Center },
                },
            };
        }

        var problem = (Diagnostic)item;
        var (icon, brush) = problem.Severity switch
        {
            DiagnosticSeverity.Error => (Icons.AlertCircle, "DangerBrush"),
            DiagnosticSeverity.Warning => (Icons.AlertTriangle, "WarningBrush"),
            _ => (Icons.Info, "InfoBrush"),
        };
        var symbol = new SymbolIcon { Data = icon, Size = 14, VerticalAlignment = VerticalAlignment.Center };
        symbol[!SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(brush);
        var source = problem.Code is null ? problem.Source : $"{problem.Source} {problem.Code}";
        var where = new TextBlock
        {
            Text = $"{source}  [Ln {problem.Start.Line + 1}, Col {problem.Start.Column + 1}]",
            Classes = { "caption", "muted" },
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        var message = new TextBlock { Text = problem.Message.ReplaceLineEndings(" "), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(message, problem.Message);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { symbol, message, where } };
    }
}
