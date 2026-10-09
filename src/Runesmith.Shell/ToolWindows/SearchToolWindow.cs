using System.Collections.ObjectModel;
using System.Composition;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI.Controls;
using Runesmith.Editor;
using Runesmith.Sdk.ToolWindows;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Appearance;
using Runesmith.Shell.Editors;
using Runesmith.Text;
using Runesmith.Workspace.Search;

namespace Runesmith.Shell.ToolWindows;

/// <summary>Find in files: searches every file of the open folder and lists the matches by file.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class SearchToolWindow(FindInFiles search, IWorkspace workspace, EditorService editors, FileIcons icons) : IToolWindowProvider, IDisposable
{
    public const string Id = "search";

    private SearchView? view;

    public ToolWindowDefinition Definition { get; } = new(Id, "Search", "search", DockSide.Left) { Order = 1, IsVisibleByDefault = true };

    public Control CreateContent() => view = new SearchView(search, workspace, editors, icons);

    /// <summary>Puts text in the search box, when given, and focuses it.</summary>
    public void Dispose() => view?.Dispose();

    public void Focus(string? query) => Dispatcher.UIThread.Post(() => view?.FocusQuery(query), DispatcherPriority.Background);

    private sealed record FileRow(string Path, string Name, string Folder, IReadOnlyList<MatchRow> Matches, ThemedIcon Icon);

    private sealed record MatchRow(string Path, FileSearchMatch Match);

    private sealed class SearchView : DockPanel, IDisposable
    {
        private readonly FindInFiles search;
        private readonly IWorkspace workspace;
        private readonly EditorService editors;
        private readonly FileIcons icons;
        private readonly TextBox query = new() { PlaceholderText = "Search", Classes = { "small" } };
        private readonly TextBox include = new() { PlaceholderText = "Files to include, such as *.cs; src/**", Classes = { "small" } };
        private readonly TextBox exclude = new() { PlaceholderText = "Files to exclude", Classes = { "small" } };
        private readonly ToggleButton matchCase = Option("Aa", "Match case");
        private readonly ToggleButton wholeWord = Option("ab", "Match whole word");
        private readonly ToggleButton regex = Option(".*", "Use regular expression");
        private readonly TextBlock status = new() { Classes = { "caption", "muted" }, Margin = new Thickness(12, 4), TextWrapping = TextWrapping.Wrap };
        private readonly ObservableCollection<FileRow> results = [];
        private readonly DispatcherTimer debounce;
        private CancellationTokenSource? running;

        public SearchView(FindInFiles search, IWorkspace workspace, EditorService editors, FileIcons icons)
        {
            this.search = search;
            this.workspace = workspace;
            this.editors = editors;
            this.icons = icons;
            debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
            debounce.Tick += (_, _) =>
            {
                debounce.Stop();
                _ = RunAsync();
            };

            var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { matchCase, wholeWord, regex } };
            var queryRow = new DockPanel();
            DockPanel.SetDock(options, Dock.Right);
            options.Margin = new Thickness(4, 0, 0, 0);
            queryRow.Children.Add(options);
            queryRow.Children.Add(query);
            var form = new StackPanel { Spacing = 6, Margin = new Thickness(10, 8, 10, 4), Children = { queryRow, include, exclude } };
            DockPanel.SetDock(form, Dock.Top);
            DockPanel.SetDock(status, Dock.Top);
            Children.Add(form);
            Children.Add(status);

            var list = new TreeView
            {
                ItemsSource = results,
                ItemTemplate = new FuncTreeDataTemplate<object?>((item, _) => item is null ? new Panel() : Row(item), item => item is FileRow file ? file.Matches : []),
            };
            list.ContainerPrepared += (_, e) =>
            {
                if (e.Container is TreeViewItem item && item.DataContext is FileRow)
                    item.IsExpanded = true;
            };
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is MatchRow match)
                    _ = editors.JumpToAsync(match.Path, new TextPosition(match.Match.Line, match.Match.Column));
            };
            Children.Add(list);

            foreach (var box in (ReadOnlySpan<TextBox>)[query, include, exclude])
            {
                box.TextChanged += (_, _) => Schedule();
                box.KeyDown += (_, e) =>
                {
                    if (e.Key != Key.Enter)
                        return;
                    debounce.Stop();
                    _ = RunAsync();
                    e.Handled = true;
                };
            }

            foreach (var option in (ReadOnlySpan<ToggleButton>)[matchCase, wholeWord, regex])
                option.IsCheckedChanged += (_, _) => Schedule();
            workspace.Changed += (_, _) =>
            {
                results.Clear();
                status.Text = "";
            };
        }

        public void Dispose()
        {
            running?.Cancel();
            running?.Dispose();
            running = null;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            running?.Cancel();
        }

        public void FocusQuery(string? text)
        {
            if (text is not null)
                query.Text = text;
            query.Focus();
            query.SelectAll();
        }

        private void Schedule()
        {
            debounce.Stop();
            debounce.Start();
        }

        private async Task RunAsync()
        {
            running?.Cancel();
            running?.Dispose();
            running = new CancellationTokenSource();
            var token = running.Token;
            results.Clear();
            var text = query.Text ?? "";
            if (text.Length == 0 || workspace.RootPath is null)
            {
                status.Text = workspace.RootPath is null ? "Open a folder to search its files." : "";
                return;
            }

            var options = (matchCase.IsChecked == true ? TextSearchOptions.MatchCase : 0)
                | (wholeWord.IsChecked == true ? TextSearchOptions.WholeWord : 0)
                | (regex.IsChecked == true ? TextSearchOptions.Regex : 0);
            status.Text = "Searching...";
            var count = 0;
            var limited = false;
            try
            {
                await foreach (var file in search.SearchAsync(text, options, Blank(include.Text), Blank(exclude.Text), token))
                {
                    if (token.IsCancellationRequested)
                        return;

                    var relative = Path.GetRelativePath(workspace.RootPath, file.FilePath);
                    var icon = icons.For(file.FilePath);
                    results.Add(new FileRow(file.FilePath, Path.GetFileName(relative), Path.GetDirectoryName(relative) ?? "", [.. file.Matches.Select(m => new MatchRow(file.FilePath, m))], icon));
                    count += file.Matches.Count;
                    limited |= file.IsLimitReached;
                }

                var files = results.Count == 1 ? "1 file" : $"{results.Count:N0} files";
                var found = count == 1 ? "1 result" : $"{count:N0} results";
                status.Text = count == 0 ? "No results." : $"{found} in {files}" + (limited ? $", stopped at {FindInFiles.MaxMatches:N0}" : "") + ".";
            }
            catch (ArgumentException exception)
            {
                status.Text = $"The pattern is not valid: {exception.Message}";
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

        private static SymbolIcon FileIcon(ThemedIcon themed)
        {
            var icon = new SymbolIcon { Size = 14, Margin = new Thickness(0, 0, 6, 0) };
            themed.ApplyTo(icon, "TextSecondaryBrush");
            return icon;
        }

        private static StackPanel Row(object item)
        {
            if (item is FileRow file)
            {
                var folder = new TextBlock { Text = file.Folder, Classes = { "caption", "muted" }, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                var badge = new Badge { Content = file.Matches.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                return new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        FileIcon(file.Icon),
                        new TextBlock { Text = file.Name, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center },
                        folder,
                        badge,
                    },
                };
            }

            var match = ((MatchRow)item).Match;
            var line = new TextBlock { FontFamily = new FontFamily(EditorOptions.BundledFontFamily), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            var start = Math.Clamp(match.PreviewColumn, 0, match.Preview.Length);
            var end = Math.Clamp(start + match.Length, start, match.Preview.Length);
            line.Inlines!.Add(new Run(match.Preview[..start]));
            var hit = new Run(match.Preview[start..end]) { FontWeight = FontWeight.Bold };
            hit[!TextElement.BackgroundProperty] = new DynamicResourceExtension("AccentSubtleBrush");
            line.Inlines.Add(hit);
            line.Inlines.Add(new Run(match.Preview[end..]));
            var number = new TextBlock { Text = (match.Line + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Classes = { "caption", "muted" }, MinWidth = 28, VerticalAlignment = VerticalAlignment.Center };
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { number, line } };
        }

        private static ToggleButton Option(string text, string tip)
        {
            var button = new ToggleButton
            {
                Classes = { "option" },
                MinWidth = 26,
                Content = new TextBlock { Text = text, FontFamily = new FontFamily(EditorOptions.BundledFontFamily), FontSize = 12 },
            };
            ToolTip.SetTip(button, tip);
            return button;
        }
    }
}
