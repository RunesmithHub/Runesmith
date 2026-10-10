using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Runesmith.Editor.Editing;
using Runesmith.Text;

namespace Runesmith.Editor;

/// <summary>How an edit groups with the one before it in the undo history.</summary>
internal enum EditKind
{
    /// <summary>Typing joins the typing before it into one undo step, until a pause, a space or a move.</summary>
    Typing,

    /// <summary>Deleting with Backspace or Delete joins the deleting before it the same way.</summary>
    Deleting,

    /// <summary>Every other edit is an undo step of its own.</summary>
    Other,
}

public sealed partial class TextArea
{
    private static readonly TimeSpan MergeWindow = TimeSpan.FromSeconds(1.5);

    /// <summary>Gets or sets whether the text can only be read, selected and copied, such as the older side of a diff.</summary>
    public bool IsReadOnly { get; set; }

    private EditKind lastEditKind = EditKind.Other;
    private DateTime lastEditTime;
    private int lastEditCaret = -1;
    private string? lineCopy;

    /// <summary>Applies an edit made through this editor: changes the text, records the undo step and moves the selection.</summary>
    internal void Apply(EditResult result, EditKind kind = EditKind.Other)
    {
        if (IsReadOnly)
            return;

        result = MirrorSnippetEdit(result);
        if (result.Changes.Count == 0)
        {
            Select(result.Selection);
            BreakUndoGroup();
            return;
        }

        var editStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var before = selection;
        var changeSet = Document.Buffer.Apply(result.Changes, this);
        if (changeSet.IsEmpty)
        {
            Select(result.Selection);
            return;
        }

        // The history raises its events, such as the document no longer being modified, so the selection has to fit the new text first.
        Select(result.Selection);
        var now = DateTime.UtcNow;
        var merge = kind != EditKind.Other && kind == lastEditKind && before.IsEmpty && before.Caret == lastEditCaret && now - lastEditTime < MergeWindow;
        Document.History.Push(changeSet, before, result.Selection, merge);
        lastEditKind = kind;
        lastEditTime = now;
        lastEditCaret = result.Selection.Caret;
        EditorMetrics.Edit.Record(EditorMetrics.Since(editStart));
    }

    /// <summary>Makes the next edit start a new undo step.</summary>
    internal void BreakUndoGroup() => lastEditKind = EditKind.Other;

    /// <summary>Replaces the selection with text, as one undo step.</summary>
    public void InsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Apply(EditOperations.Insert(selection, LineEndings.Normalize(text)));
    }

    /// <summary>Makes changes to the text as one undo step, such as accepting a completion, and selects <paramref name="selectionAfter"/>.</summary>
    public void ApplyChanges(IReadOnlyList<TextChange> changes, EditorSelection selectionAfter) => Apply(new EditResult(changes, selectionAfter));

    /// <summary>Raised before an undo; a handler that sets <see cref="HandledEventArgs.Handled"/> undoes something else instead, such as a
    /// whole workspace edit.</summary>
    public event EventHandler<HandledEventArgs>? UndoRequested;

    /// <summary>Raised before a redo; a handler that sets <see cref="HandledEventArgs.Handled"/> redoes something else instead.</summary>
    public event EventHandler<HandledEventArgs>? RedoRequested;

    public void Undo()
    {
        if (IsReadOnly)
            return;

        EndSnippet();
        if (Handled(UndoRequested))
            return;

        BreakUndoGroup();
        if (Document.History.Undo(Document.Buffer) is { StateBefore: EditorSelection before })
            Select(before);
        else
            Select(selection);
    }

    public void Redo()
    {
        if (IsReadOnly)
            return;

        EndSnippet();
        if (Handled(RedoRequested))
            return;

        BreakUndoGroup();
        if (Document.History.Redo(Document.Buffer) is { StateAfter: EditorSelection after })
            Select(after);
        else
            Select(selection);
    }

    private bool Handled(EventHandler<HandledEventArgs>? handler)
    {
        var args = new HandledEventArgs();
        handler?.Invoke(this, args);
        return args.Handled;
    }

    public void SelectAll() => Select(new EditorSelection(0, Snapshot.Length), scrollIntoView: false);

    /// <summary>Copies the selection, or the caret's whole line when nothing is selected.</summary>
    public async Task CopyAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(LineEndings.Apply(CopiedText(), Document.LineEnding));
    }

    /// <summary>Cuts the selection, or the caret's whole line when nothing is selected.</summary>
    public async Task CutAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        var text = CopiedText();
        await clipboard.SetTextAsync(LineEndings.Apply(text, Document.LineEnding));
        if (selection.IsEmpty)
            Apply(EditOperations.DeleteLines(Snapshot, selection));
        else
            Apply(EditOperations.Insert(selection, ""));
    }

    /// <summary>Pastes; a whole line copied without a selection is pasted above the caret's line.</summary>
    public async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard || await clipboard.TryGetTextAsync() is not { Length: > 0 } text)
            return;

        text = LineEndings.Normalize(text);
        if (text == lineCopy && selection.IsEmpty)
        {
            var line = Snapshot.GetLineFromPosition(selection.Caret);
            var caret = selection.Caret + text.Length;
            Apply(new EditResult([new TextChange(new TextSpan(line.Start, 0), text)], EditorSelection.At(caret)));
            return;
        }

        Apply(EditOperations.Insert(selection, text));
    }

    private string CopiedText()
    {
        if (!selection.IsEmpty)
        {
            lineCopy = null;
            return Snapshot.GetText(selection.Span);
        }

        var line = Snapshot.GetLineFromPosition(selection.Caret);
        var text = Snapshot.GetText(line.Span) + "\n";
        lineCopy = text;
        return text;
    }

    private void OnBufferChanged(object? sender, Text.TextChangedEventArgs e)
    {
        var changeSet = e.ChangeSet;
        if (changeSet.Changes.Count == 0)
            return;

        var before = changeSet.Before;
        var first = before.GetLineFromPosition(changeSet.Changes[0].Span.Start).LineNumber;
        var lastBefore = before.GetLineFromPosition(changeSet.Changes[^1].Span.End).LineNumber;
        var delta = changeSet.After.LineCount - before.LineCount;
        cache.ApplyEdit(first, lastBefore, delta);
        FollowEditWithSnippet(changeSet);
        MapDecorations(changeSet);
        FollowEditWithChanges(first, lastBefore, delta);
        Folding.Map(changeSet);
        for (var number = first; number <= Math.Min(lastBefore + delta, changeSet.After.LineCount - 1); number++)
            maxLineWidth = Math.Max(maxLineWidth, changeSet.After.GetLine(number).Length * Formatting.CharacterWidth);

        if (!ReferenceEquals(changeSet.Tag, this) && !ReferenceEquals(changeSet.Tag, UndoHistory.UndoTag) && !ReferenceEquals(changeSet.Tag, UndoHistory.RedoTag))
        {
            BreakUndoGroup();
            var replacesAll = changeSet.Changes.Count == 1 && changeSet.Changes[0].Span.Length == before.Length;
            if (replacesAll)
            {
                var anchor = before.GetPosition(selection.Anchor);
                var caret = before.GetPosition(selection.Caret);
                selection = new EditorSelection(changeSet.After.GetOffset(anchor), changeSet.After.GetOffset(caret));
            }
            else
            {
                selection = selection.Through(changeSet).Clamp(changeSet.After.Length);
            }

            UpdateBracketMatch();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Whoever made the edit sets the selection afterwards; until then it must still fit the text, because other listeners read it.
            var moved = selection.Through(changeSet).Clamp(changeSet.After.Length);
            selectionMovedByEdit |= moved != selection;
            selection = moved;
            UpdateBracketMatch();
        }

        InvalidateVisual();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }
}
