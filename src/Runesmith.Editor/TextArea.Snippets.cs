using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Runesmith.Editor.Editing;
using Runesmith.Editor.Rendering;
using Runesmith.Editor.Snippets;
using Runesmith.Sdk.Documents.Snippets;
using Runesmith.Text;

namespace Runesmith.Editor;

public sealed partial class TextArea
{
    private SnippetSession? snippet;

    /// <summary>Gets whether a snippet's tab stops are active, so Tab and Shift+Tab move between them.</summary>
    public bool IsInSnippet => snippet is not null;

    /// <summary>Gets the active snippet, for the tests and the renderer.</summary>
    internal SnippetSession? Snippet => snippet;

    /// <summary>Raised when the caret comes to a tab stop with choices, which the editor offers in a list.</summary>
    internal event EventHandler<SnippetTarget>? SnippetChoicesRequested;

    /// <summary>Replaces the selection with a snippet, as one undo step, and selects its first tab stop.</summary>
    public Task InsertSnippetAsync(string snippetText) => InsertSnippetAsync(snippetText, selection.Span, []);

    /// <summary>Replaces a span with a snippet and makes other changes with it, as one undo step, such as accepting a completion.</summary>
    internal async Task InsertSnippetAsync(string snippetText, TextSpan replace, IReadOnlyList<TextChange> additionalChanges)
    {
        var nodes = SnippetParser.Parse(snippetText);
        string? clipboard = null;
        if (SnippetParser.Uses(nodes, SnippetVariables.Clipboard) && TopLevel.GetTopLevel(this)?.Clipboard is { } system)
        {
            var before = Snapshot;
            clipboard = await system.TryGetTextAsync();
            if (!ReferenceEquals(before, Snapshot))
            {
                replace = selection.Span;
                additionalChanges = [];
            }
        }

        InsertSnippet(nodes, replace, additionalChanges, clipboard);
    }

    /// <summary>Inserts parsed snippet syntax in place of a span; see <see cref="InsertSnippetAsync(string, TextSpan, IReadOnlyList{TextChange})"/>.</summary>
    internal void InsertSnippet(IReadOnlyList<SnippetNode> nodes, TextSpan replace, IReadOnlyList<TextChange> additionalChanges, string? clipboard = null)
    {
        if (IsReadOnly)
            return;

        EndSnippet();
        var snapshot = Snapshot;
        replace = TextSpan.FromBounds(Math.Clamp(replace.Start, 0, snapshot.Length), Math.Clamp(replace.End, 0, snapshot.Length));
        var context = new SnippetContext(Document.FilePath, Document.Name, snapshot, replace, clipboard)
        {
            LineComment = Language?.LineComment,
            BlockComment = Language?.BlockComment,
        };
        var expansion = SnippetExpansion.Create(nodes, name => SnippetVariables.Resolve(name, context), SnippetVariables.IndentOf(snapshot, replace.Start), options.IndentUnit);

        var changes = new List<TextChange> { new(replace, expansion.Text) };
        var shift = 0;
        foreach (var extra in additionalChanges)
        {
            if (extra.Span.End > snapshot.Length || extra.Span.End > replace.Start && extra.Span.Start < replace.End)
                continue;

            changes.Add(extra);
            if (extra.Span.End <= replace.Start)
                shift += extra.NewText.Length - extra.Span.Length;
        }

        changes.Sort((a, b) => a.Span.Start.CompareTo(b.Span.Start));
        var start = replace.Start + shift;
        Apply(new EditResult(changes, EditorSelection.At(start + expansion.FinalOffset)));
        if (!expansion.HasTabStops || !ReferenceEquals(Document.Buffer.Current, Snapshot) || Snapshot.Length < start + expansion.Text.Length)
            return;

        snippet = new SnippetSession(expansion, start);
        MoveInSnippet(1);
    }

    /// <summary>Moves to the next tab stop, or the one before; past the last one the caret goes to the final position and the snippet ends.</summary>
    public void MoveInSnippet(int direction)
    {
        if (snippet is not { } session)
            return;

        BreakUndoGroup();
        var target = session.Move(direction);
        Select(new EditorSelection(target.Span.Start, target.Span.End));
        if (target.IsFinal)
            EndSnippet();
        else if (target.Choices is { Count: > 0 })
            SnippetChoicesRequested?.Invoke(this, target);
        InvalidateVisual();
    }

    /// <summary>Leaves the snippet's tab stops; the text stays as it is.</summary>
    public void EndSnippet()
    {
        if (snippet is null)
            return;

        snippet = null;
        InvalidateVisual();
    }

    /// <summary>Replaces the current tab stop's text, such as with a choice the user picked, and its linked places with it.</summary>
    internal void ReplaceSnippetStop(string text)
    {
        if (snippet?.Current is not { } group)
            return;

        var span = group.Primary.Span;
        Apply(new EditResult([new TextChange(span, text)], new EditorSelection(span.Start, span.Start + text.Length)));
    }

    /// <summary>Handles Tab, Shift+Tab and Esc while a snippet is active; returns whether it took the key.</summary>
    internal bool HandleSnippetKey(KeyEventArgs e)
    {
        if (snippet is null)
            return false;

        switch (e.Key)
        {
            case Key.Tab when e.KeyModifiers == KeyModifiers.None:
                MoveInSnippet(1);
                return true;
            case Key.Tab when e.KeyModifiers == KeyModifiers.Shift:
                MoveInSnippet(-1);
                return true;
            case Key.Escape when e.KeyModifiers == KeyModifiers.None:
                EndSnippet();
                return true;
            default:
                return false;
        }
    }

    private EditResult MirrorSnippetEdit(EditResult result)
    {
        if (snippet?.Mirror(result.Changes, Snapshot) is not { } mirrored)
            return result;

        var moved = result.Selection;
        return new EditResult(mirrored.Changes, new EditorSelection(moved.Anchor + mirrored.ShiftBefore, moved.Caret + mirrored.ShiftBefore));
    }

    private void FollowEditWithSnippet(TextChangeSet changeSet) => snippet?.Map(changeSet.Changes);

    private void LeaveSnippetWhenCaretLeaves()
    {
        if (snippet is not null && !snippet.Contains(selection.Caret))
            EndSnippet();
    }

    private void DrawSnippet(DrawingContext context, EditorBrushes brushes, TextSpan visible, double left)
    {
        if (snippet is not { } session)
            return;

        var current = session.Current;
        foreach (var group in session.Groups)
        {
            var isCurrent = ReferenceEquals(group, current);
            foreach (var range in group.Ranges)
            {
                var span = range.Span;
                if (span.End < visible.Start || span.Start > visible.End)
                    continue;

                if (span.IsEmpty)
                {
                    var caret = GetCharacterRect(span.Start);
                    context.FillRectangle(isCurrent ? brushes.SnippetBorder.Brush! : brushes.SnippetStop, new Rect(caret.X - 1, caret.Y + 2, 2, caret.Height - 4));
                    continue;
                }

                foreach (var rect in SpanRects(span, left))
                {
                    if (isCurrent)
                        context.DrawRectangle(brushes.SnippetStop, brushes.SnippetBorder, rect.Deflate(new Thickness(0, 1)), 2, 2);
                    else
                        context.FillRectangle(brushes.SnippetStop, rect.Deflate(new Thickness(0, 1)), 2);
                }
            }
        }
    }
}
