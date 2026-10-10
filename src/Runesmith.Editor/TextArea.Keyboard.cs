using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Runesmith.Editor.Editing;
using Runesmith.Text;

namespace Runesmith.Editor;

public sealed partial class TextArea
{
    private double? preferredX;

    private Hotkeys hotkeysCache = Editor.Hotkeys.Default;

    private Hotkeys Hotkeys => this.GetPlatformSettings()?.HotkeyConfiguration is { } configuration ? hotkeysCache = hotkeysCache.For(configuration) : Editor.Hotkeys.Default;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        inputTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        base.OnKeyDown(e);
        if (!e.Handled)
            e.Handled = HandleKey(e);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        inputTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrEmpty(e.Text) || e.Text.Any(c => char.IsControl(c) && c != '\t'))
            return;

        e.Handled = true;
        if (e.Text.Length == 1)
        {
            var character = e.Text[0];
            var kind = char.IsWhiteSpace(character) ? EditKind.Other : EditKind.Typing;
            Apply(EditOperations.Type(Snapshot, selection, character, options, Language), kind);
            CharacterTyped?.Invoke(this, character);
        }
        else
        {
            Apply(EditOperations.Insert(selection, e.Text), EditKind.Typing);
        }
    }

    private bool HandleKey(KeyEventArgs e)
    {
        var hotkeys = Hotkeys;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var word = (e.KeyModifiers & hotkeys.WholeWordTextActionModifiers) != 0;
        var command = (e.KeyModifiers & hotkeys.CommandModifiers) != 0;
        var alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        var snapshot = Snapshot;

        if (Matches(hotkeys.Copy, e))
            _ = CopyAsync();
        else if (Matches(hotkeys.Cut, e))
            _ = CutAsync();
        else if (Matches(hotkeys.Paste, e))
            _ = PasteAsync();
        else if (Matches(hotkeys.Undo, e))
            Undo();
        else if (Matches(hotkeys.Redo, e))
            Redo();
        else if (Matches(hotkeys.SelectAll, e))
            SelectAll();
        else if (Matches(hotkeys.MoveCursorToTheStartOfDocument, e) || Matches(hotkeys.MoveCursorToTheStartOfDocumentWithSelection, e))
            MoveTo(0, shift);
        else if (Matches(hotkeys.MoveCursorToTheEndOfDocument, e) || Matches(hotkeys.MoveCursorToTheEndOfDocumentWithSelection, e))
            MoveTo(snapshot.Length, shift);
        else
        {
            switch (e.Key)
            {
                case Key.Left when !alt:
                    MoveHorizontally(-1, shift, word);
                    break;
                case Key.Right when !alt:
                    MoveHorizontally(1, shift, word);
                    break;
                case Key.Up or Key.Down when alt && shift && !command:
                    Apply(DuplicateLinesIn(e.Key == Key.Down));
                    break;
                case Key.Up or Key.Down when alt && !command:
                    Apply(EditOperations.MoveLines(snapshot, selection, down: e.Key == Key.Down));
                    break;
                case Key.Up or Key.Down when command && !shift:
                    ScrollOffset += new Avalonia.Vector(0, e.Key == Key.Down ? LineHeight : -LineHeight);
                    break;
                case Key.Up:
                    MoveVertically(-1, shift);
                    break;
                case Key.Down:
                    MoveVertically(1, shift);
                    break;
                case Key.PageUp:
                    MoveVertically(-Math.Max(1, (int)(Bounds.Height / LineHeight) - 1), shift);
                    break;
                case Key.PageDown:
                    MoveVertically(Math.Max(1, (int)(Bounds.Height / LineHeight) - 1), shift);
                    break;
                case Key.Home when command:
                    MoveTo(0, shift);
                    break;
                case Key.End when command:
                    MoveTo(snapshot.Length, shift);
                    break;
                case Key.Home:
                    MoveTo(SmartHome(snapshot, selection.Caret), shift);
                    break;
                case Key.End:
                    MoveTo(snapshot.GetLineFromPosition(selection.Caret).End, shift);
                    break;
                case Key.Back when word:
                    Apply(EditOperations.DeleteWord(snapshot, selection, forward: false), EditKind.Deleting);
                    break;
                case Key.Back:
                    Apply(EditOperations.Backspace(snapshot, selection, options, Language), EditKind.Deleting);
                    break;
                case Key.Delete when word:
                    Apply(EditOperations.DeleteWord(snapshot, selection, forward: true), EditKind.Deleting);
                    break;
                case Key.Delete when shift && !command:
                    _ = CutAsync();
                    break;
                case Key.Delete:
                    Apply(EditOperations.DeleteForward(snapshot, selection), EditKind.Deleting);
                    break;
                case Key.Enter when !command && !alt:
                    Apply(EditOperations.NewLine(snapshot, selection, options, Language));
                    break;
                case Key.Tab when !command && !alt:
                    Apply(shift ? EditOperations.Outdent(snapshot, selection, options) : EditOperations.Indent(snapshot, selection, options));
                    break;
                case Key.K when command && shift:
                    Apply(EditOperations.DeleteLines(snapshot, selection));
                    break;
                case Key.Oem2 or Key.Divide when command:
                    ToggleComment();
                    break;
                case Key.Escape when !selection.IsEmpty:
                    Select(EditorSelection.At(selection.Caret));
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>Comments the selected lines out, or back in.</summary>
    public void ToggleComment() => Apply(EditOperations.ToggleComment(Snapshot, selection, Language));

    /// <summary>Moves the caret to the start of a line and column, counted from zero, and scrolls it to the middle of the view.</summary>
    public void GoTo(TextPosition position)
    {
        var offset = Snapshot.GetOffset(position);
        Select(EditorSelection.At(offset), scrollIntoView: false);
        ScrollIntoView(offset, center: true);
    }

    private EditResult DuplicateLinesIn(bool down)
    {
        var result = EditOperations.DuplicateLines(Snapshot, selection);
        return down ? result : result with { Selection = selection };
    }

    private void MoveTo(int offset, bool extend)
    {
        BreakUndoGroup();
        Select(extend ? selection with { Caret = offset } : EditorSelection.At(offset));
    }

    private void MoveHorizontally(int direction, bool extend, bool word)
    {
        var snapshot = Snapshot;
        int target;
        if (!extend && !selection.IsEmpty && !word)
            target = direction < 0 ? selection.Start : selection.End;
        else if (word)
            target = direction < 0 ? WordBoundaries.PreviousWordBoundary(snapshot, selection.Caret) : WordBoundaries.NextWordBoundary(snapshot, selection.Caret);
        else
            target = Step(snapshot, selection.Caret, direction);

        MoveTo(SkipFolded(snapshot, target, direction), extend);
    }

    // An offset in lines folding hides moves past them, the way the caret moved.
    private int SkipFolded(TextSnapshot snapshot, int offset, int direction)
    {
        var line = snapshot.GetLineFromPosition(offset).LineNumber;
        if (!IsHiddenLine(line))
            return offset;

        var next = Folding.NextVisible(line, snapshot.LineCount);
        return direction > 0 && next < snapshot.LineCount ? snapshot.GetLine(next).Start : snapshot.GetLine(Folding.VisibleLineOf(line)).End;
    }

    private void MoveVertically(int lines, bool extend)
    {
        BreakUndoGroup();
        var snapshot = Snapshot;
        var position = snapshot.GetPosition(selection.Caret);
        var x = preferredX ?? GetVisualLine(position.Line).GetX(position.Column);
        var targetLine = position.Line + lines;
        if (Folding.HasHiddenLines)
        {
            targetLine = position.Line;
            for (var step = 0; step < Math.Abs(lines) && targetLine >= 0 && targetLine < snapshot.LineCount; step++)
                targetLine = lines > 0 ? Folding.NextVisible(targetLine, snapshot.LineCount) : Folding.PreviousVisible(targetLine);
        }

        int offset;
        if (targetLine < 0)
            offset = 0;
        else if (targetLine >= snapshot.LineCount)
            offset = IsHiddenLine(snapshot.LineCount - 1) ? snapshot.GetLine(Folding.VisibleLineOf(snapshot.LineCount - 1)).End : snapshot.Length;
        else
            offset = snapshot.GetLine(targetLine).Start + GetVisualLine(targetLine).GetColumn(x);

        Select(extend ? selection with { Caret = offset } : EditorSelection.At(offset));
        preferredX = x;
    }

    private static int Step(TextSnapshot snapshot, int offset, int direction)
    {
        var target = Math.Clamp(offset + direction, 0, snapshot.Length);
        if (direction < 0 && target > 0 && char.IsLowSurrogate(snapshot[target]) && char.IsHighSurrogate(snapshot[target - 1]))
            target--;
        else if (direction > 0 && target < snapshot.Length && char.IsLowSurrogate(snapshot[target]) && char.IsHighSurrogate(snapshot[target - 1]))
            target++;
        return target;
    }

    // Home goes to the first character that is not indentation, or to the line start when the caret is there already.
    private static int SmartHome(TextSnapshot snapshot, int caret)
    {
        var line = snapshot.GetLineFromPosition(caret);
        var indent = EditOperations.LeadingWhitespace(snapshot.GetLineText(line.LineNumber)).Length;
        var firstText = line.Start + indent;
        return caret == firstText ? line.Start : firstText;
    }

    private static bool Matches(IReadOnlyList<KeyGesture> gestures, KeyEventArgs e) => gestures.Any(g => g.Matches(e));
}
