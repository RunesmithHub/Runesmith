using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Text;
using TextSearch = Runesmith.Text.TextSearch;

namespace Runesmith.Editor.Find;

/// <summary>Finds and replaces text in the editor: it highlights every match, counts them and moves between them.</summary>
internal sealed class FindBar : Border
{
    private const int MaximumMatches = 10_000;

    private readonly TextArea area;
    private readonly TextBox query = new() { PlaceholderText = "Find", Width = 220, Classes = { "small" } };
    private readonly TextBox replacement = new() { PlaceholderText = "Replace", Width = 220, Classes = { "small" } };
    private readonly ToggleButton matchCase = Option("Aa", "Match case (Alt+C)");
    private readonly ToggleButton wholeWord = Option("ab", "Match whole word (Alt+W)");
    private readonly ToggleButton regex = Option(".*", "Use regular expression (Alt+R)");
    private readonly ToggleButton expand;
    private readonly TextBlock count = new() { MinWidth = 72, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Margin = new Thickness(6, 0) };
    private readonly Control replaceRow;
    private readonly DispatcherTimer refreshTimer;
    private IReadOnlyList<TextSpan> matches = [];
    private bool isUpdatingSelection;

    public FindBar(TextArea area)
    {
        this.area = area;
        Classes.Add("floating");
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 6, 22, 0);
        IsVisible = false;
        count[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("TextMutedBrush");

        expand = new ToggleButton { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = Icons.ChevronRight, Size = 14 } };
        ToolTip.SetTip(expand, "Toggle replace");
        expand.IsCheckedChanged += (_, _) =>
        {
            replaceRow!.IsVisible = expand.IsChecked == true;
            ((SymbolIcon)expand.Content!).Data = expand.IsChecked == true ? Icons.ChevronDown : Icons.ChevronRight;
        };

        var findRow = Row(
            query, matchCase, wholeWord, regex, count,
            Action(Icons.ArrowUp, "Previous match (Shift+Enter)", () => Move(backwards: true)),
            Action(Icons.ArrowDown, "Next match (Enter)", () => Move(backwards: false)),
            Action(Icons.X, "Close (Escape)", Close));
        replaceRow = Row(
            replacement,
            TextAction("Replace", "Replace this match (Enter)", ReplaceOne),
            TextAction("Replace all", "Replace every match (Ctrl+Alt+Enter)", ReplaceAll));
        replaceRow.IsVisible = false;

        var rows = new StackPanel { Spacing = 4, Children = { findRow, replaceRow } };
        var layout = new DockPanel();
        DockPanel.SetDock(expand, Dock.Left);
        expand.VerticalAlignment = VerticalAlignment.Top;
        expand.Margin = new Thickness(0, 2, 4, 0);
        layout.Children.Add(expand);
        layout.Children.Add(rows);
        Child = layout;

        refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        refreshTimer.Tick += (_, _) =>
        {
            refreshTimer.Stop();
            Refresh(selectNearest: false);
        };

        query.TextChanged += (_, _) => Refresh(selectNearest: true);
        foreach (var option in (ReadOnlySpan<ToggleButton>)[matchCase, wholeWord, regex])
            option.IsCheckedChanged += (_, _) => Refresh(selectNearest: true);
        query.KeyDown += OnQueryKeyDown;
        replacement.KeyDown += OnReplacementKeyDown;
        area.Document.Buffer.Changed += (_, _) =>
        {
            if (IsVisible)
            {
                refreshTimer.Stop();
                refreshTimer.Start();
            }
        };
        area.SelectionChanged += (_, _) =>
        {
            if (IsVisible && !isUpdatingSelection)
                UpdateCurrent();
        };
    }

    /// <summary>Opens the bar with the selected text, if it is on one line, as the query, and focuses the query.</summary>
    public void Open(bool withReplace)
    {
        var selection = area.Selection;
        if (!selection.IsEmpty && selection.Span.Length < 500)
        {
            var text = area.Snapshot.GetText(selection.Span);
            if (!text.Contains('\n'))
                query.Text = regex.IsChecked == true ? Regex.Escape(text) : text;
        }

        expand.IsChecked = withReplace || expand.IsChecked == true && IsVisible;
        IsVisible = true;
        Refresh(selectNearest: false);
        query.Focus();
        query.SelectAll();
    }

    public void Close()
    {
        IsVisible = false;
        area.SearchMatches = [];
        area.CurrentSearchMatch = null;
        area.Focus();
    }

    /// <summary>Moves to the next match after the selection, or the previous one, wrapping around.</summary>
    public void Move(bool backwards)
    {
        if (string.IsNullOrEmpty(query.Text))
        {
            Open(withReplace: false);
            return;
        }

        var selection = area.Selection;
        var start = backwards ? selection.Start : selection.End;
        if (TextSearch.FindNext(area.Snapshot, query.Text, Options, start, backwards) is { } match)
            SelectMatch(match);
    }

    private TextSearchOptions Options =>
        (matchCase.IsChecked == true ? TextSearchOptions.MatchCase : 0)
        | (wholeWord.IsChecked == true ? TextSearchOptions.WholeWord : 0)
        | (regex.IsChecked == true ? TextSearchOptions.Regex : 0);

    private void Refresh(bool selectNearest)
    {
        var text = query.Text ?? "";
        query.Classes.Set("error", false);
        if (text.Length == 0)
        {
            SetMatches([]);
            return;
        }

        if (regex.IsChecked == true && !TextSearch.TryCreateRegex(text, Options, out _, out var error))
        {
            query.Classes.Set("error", true);
            ToolTip.SetTip(query, error);
            SetMatches([]);
            count.Text = "Invalid pattern";
            return;
        }

        ToolTip.SetTip(query, null);
        IReadOnlyList<TextSpan> found;
        try
        {
            found = TextSearch.FindAll(area.Snapshot, text, Options);
        }
        catch (RegexMatchTimeoutException)
        {
            found = [];
            count.Text = "Too slow";
        }

        SetMatches(found.Count > MaximumMatches ? found.Take(MaximumMatches).ToList() : found);
        if (selectNearest && matches.Count > 0)
        {
            var caret = area.Selection.Start;
            SelectMatch(matches.FirstOrDefault(m => m.Start >= caret, matches[0]));
        }
    }

    private void SetMatches(IReadOnlyList<TextSpan> found)
    {
        matches = found;
        area.SearchMatches = found;
        UpdateCurrent();
    }

    private void UpdateCurrent()
    {
        var selection = area.Selection.Span;
        var index = -1;
        for (var i = 0; i < matches.Count; i++)
        {
            if (matches[i] == selection)
            {
                index = i;
                break;
            }
        }

        area.CurrentSearchMatch = index >= 0 ? matches[index] : null;
        var total = matches.Count >= MaximumMatches ? $"{MaximumMatches:N0}+" : matches.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        count.Text = string.IsNullOrEmpty(query.Text) ? "" : matches.Count == 0 ? "No results" : index >= 0 ? $"{index + 1:N0} of {total}" : $"{total} found";
    }

    private void SelectMatch(TextSpan match)
    {
        isUpdatingSelection = true;
        area.Select(new EditorSelection(match.Start, match.End), scrollIntoView: false);
        area.ScrollIntoView(match.Start, center: true);
        isUpdatingSelection = false;
        UpdateCurrent();
    }

    private void ReplaceOne()
    {
        if (area.CurrentSearchMatch is not { } current)
        {
            Move(backwards: false);
            return;
        }

        var replaced = Replacement(area.Snapshot.GetText(current));
        area.ApplyChanges([new TextChange(current, replaced)], EditorSelection.At(current.Start + replaced.Length));
        Refresh(selectNearest: false);
        Move(backwards: false);
    }

    private void ReplaceAll()
    {
        if (matches.Count == 0)
            return;

        var snapshot = area.Snapshot;
        var changes = matches.Select(m => new TextChange(m, Replacement(snapshot.GetText(m)))).ToList();
        var caret = OffsetMapping.Map(changes, area.Selection.Caret);
        area.ApplyChanges(changes, EditorSelection.At(caret));
        count.Text = $"Replaced {changes.Count:N0}";
    }

    // With a regular expression the replacement can refer to groups of the match, such as $1.
    private string Replacement(string matched)
    {
        var text = replacement.Text ?? "";
        if (regex.IsChecked != true || !TextSearch.TryCreateRegex(query.Text ?? "", Options, out var pattern, out _) || pattern is null)
            return text;

        return pattern.Replace(matched, text.Replace("\\n", "\n", StringComparison.Ordinal).Replace("\\t", "\t", StringComparison.Ordinal), 1);
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = HandleCommonKey(e);
        if (e.Handled)
            return;

        if (e.Key == Key.Enter)
        {
            Move(backwards: e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
        }
    }

    private void OnReplacementKeyDown(object? sender, KeyEventArgs e)
    {
        e.Handled = HandleCommonKey(e);
        if (!e.Handled && e.Key == Key.Enter)
        {
            ReplaceOne();
            e.Handled = true;
        }
    }

    private bool HandleCommonKey(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                return true;
            case Key.Enter when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Alt):
                ReplaceAll();
                return true;
            case Key.C when e.KeyModifiers == KeyModifiers.Alt:
                matchCase.IsChecked = matchCase.IsChecked != true;
                return true;
            case Key.W when e.KeyModifiers == KeyModifiers.Alt:
                wholeWord.IsChecked = wholeWord.IsChecked != true;
                return true;
            case Key.R when e.KeyModifiers == KeyModifiers.Alt:
                regex.IsChecked = regex.IsChecked != true;
                return true;
            default:
                return false;
        }
    }

    private static StackPanel Row(params Control[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        row.Children.AddRange(children);
        return row;
    }

    private static ToggleButton Option(string text, string tip)
    {
        var button = new ToggleButton
        {
            Classes = { "option" },
            Focusable = false,
            Content = new TextBlock { Text = text, FontFamily = new FontFamily(EditorOptions.BundledFontFamily), FontSize = 12 },
            MinWidth = 26,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Button Action(Geometry icon, string tip, Action action)
    {
        var button = new Button { Classes = { "icon", "small" }, Focusable = false, Content = new SymbolIcon { Data = icon, Size = 14 } };
        button.Click += (_, _) => action();
        ToolTip.SetTip(button, tip);
        return button;
    }

    private static Button TextAction(string text, string tip, Action action)
    {
        var button = new Button { Classes = { "small" }, Focusable = false, Content = text };
        button.Click += (_, _) => action();
        ToolTip.SetTip(button, tip);
        return button;
    }
}
