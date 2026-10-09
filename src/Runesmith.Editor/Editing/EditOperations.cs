using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Editing;

/// <summary>The editing operations of the editor, as functions from a text and a selection to the changes they make.</summary>
public static class EditOperations
{
    /// <summary>Replaces the selection with text, such as a paste.</summary>
    public static EditResult Insert(EditorSelection selection, string text) =>
        new([new TextChange(selection.Span, text)], EditorSelection.At(selection.Start + text.Length));

    /// <summary>Types one character, closing brackets and quotes, wrapping a selection in them and typing over a closing one.</summary>
    public static EditResult Type(TextSnapshot snapshot, EditorSelection selection, char character, EditorOptions options, LanguageDefinition? language)
    {
        var text = character.ToString();
        if (!options.AutoCloseBrackets || language is null)
            return Insert(selection, text);

        var caret = selection.Caret;
        var next = caret < snapshot.Length ? snapshot[caret] : '\0';
        var isQuote = language.Quotes.Contains(character);
        var closer = isQuote ? character : ClosingBracket(language, character);

        if (selection.IsEmpty && next == character && (isQuote || IsClosingBracket(language, character)))
            return EditResult.Move(EditorSelection.At(caret + 1));

        if (closer is not { } close)
            return Insert(selection, text);

        if (!selection.IsEmpty)
        {
            var selected = snapshot.GetText(selection.Span);
            return new EditResult(
                [new TextChange(selection.Span, character + selected + close)],
                new EditorSelection(selection.Start + 1, selection.Start + 1 + selected.Length));
        }

        var previous = caret > 0 ? snapshot[caret - 1] : '\0';
        var nextAllowsPair = next is '\0' or ' ' or '\t' or '\n' or ';' or ',' or ')' or ']' or '}' or '>';
        var previousAllowsQuote = !isQuote || !(WordBoundaries.IsWordCharacter(previous) || previous == character);
        if (!nextAllowsPair || !previousAllowsQuote)
            return Insert(selection, text);

        return new EditResult([new TextChange(new TextSpan(caret, 0), text + close)], EditorSelection.At(caret + 1));
    }

    /// <summary>Starts a new line with the indentation of the current one, one level deeper after an opening bracket, and puts a closing
    /// bracket right after the caret on a line of its own.</summary>
    public static EditResult NewLine(TextSnapshot snapshot, EditorSelection selection, EditorOptions options, LanguageDefinition? language)
    {
        var line = snapshot.GetLineFromPosition(selection.Start);
        var lineText = snapshot.GetLineText(line.LineNumber);
        var indent = LeadingWhitespace(lineText[..Math.Min(lineText.Length, selection.Start - line.Start)]);

        var before = selection.Start - 1;
        while (before >= line.Start && snapshot[before] is ' ' or '\t')
            before--;

        var opener = before >= line.Start ? snapshot[before] : '\0';
        var closer = language is null ? null : ClosingBracket(language, opener);
        if (closer is null)
        {
            var plain = "\n" + indent;
            return new EditResult([new TextChange(selection.Span, plain)], EditorSelection.At(selection.Start + plain.Length));
        }

        var inner = "\n" + indent + options.IndentUnit;
        var after = selection.End < snapshot.Length ? snapshot[selection.End] : '\0';
        var text = after == closer ? inner + "\n" + indent : inner;
        return new EditResult([new TextChange(selection.Span, text)], EditorSelection.At(selection.Start + inner.Length));
    }

    /// <summary>Deletes the selection or the character before the caret; in indentation made of spaces it deletes back to the previous tab stop,
    /// and between an empty pair of brackets or quotes it deletes both.</summary>
    public static EditResult Backspace(TextSnapshot snapshot, EditorSelection selection, EditorOptions options, LanguageDefinition? language)
    {
        if (!selection.IsEmpty)
            return Delete(selection.Span);

        var caret = selection.Caret;
        if (caret == 0)
            return EditResult.Move(selection);

        var previous = snapshot[caret - 1];
        var next = caret < snapshot.Length ? snapshot[caret] : '\0';
        if (language is not null && options.AutoCloseBrackets && IsEmptyPair(language, previous, next))
            return Delete(new TextSpan(caret - 1, 2));

        var line = snapshot.GetLineFromPosition(caret);
        var column = caret - line.Start;
        if (previous == ' ' && column > 0)
        {
            var leading = LeadingWhitespace(snapshot.GetText(new TextSpan(line.Start, column)));
            if (leading.Length == column && !leading.Contains('\t'))
            {
                var remove = (column - 1) % options.TabSize + 1;
                return Delete(new TextSpan(caret - remove, remove));
            }
        }

        var length = caret >= 2 && char.IsLowSurrogate(previous) && char.IsHighSurrogate(snapshot[caret - 2]) ? 2 : 1;
        return Delete(new TextSpan(caret - length, length));
    }

    /// <summary>Deletes the selection or the character after the caret.</summary>
    public static EditResult DeleteForward(TextSnapshot snapshot, EditorSelection selection)
    {
        if (!selection.IsEmpty)
            return Delete(selection.Span);

        var caret = selection.Caret;
        if (caret >= snapshot.Length)
            return EditResult.Move(selection);

        var length = caret + 1 < snapshot.Length && char.IsHighSurrogate(snapshot[caret]) && char.IsLowSurrogate(snapshot[caret + 1]) ? 2 : 1;
        return Delete(new TextSpan(caret, length));
    }

    /// <summary>Deletes the selection, or from the caret to the previous or next word boundary.</summary>
    public static EditResult DeleteWord(TextSnapshot snapshot, EditorSelection selection, bool forward)
    {
        if (!selection.IsEmpty)
            return Delete(selection.Span);

        var other = forward ? WordBoundaries.NextWordBoundary(snapshot, selection.Caret) : WordBoundaries.PreviousWordBoundary(snapshot, selection.Caret);
        return Delete(TextSpan.FromBounds(Math.Min(other, selection.Caret), Math.Max(other, selection.Caret)));
    }

    /// <summary>Indents: a selection over several lines indents each of them, otherwise the caret moves to the next tab stop.</summary>
    public static EditResult Indent(TextSnapshot snapshot, EditorSelection selection, EditorOptions options)
    {
        var (first, last) = SelectedLines(snapshot, selection);
        if (first == last && (selection.IsEmpty || !SpansWholeLine(snapshot, selection, first)))
        {
            var line = snapshot.GetLineFromPosition(selection.Start);
            var column = VisualColumn(snapshot.GetText(TextSpan.FromBounds(line.Start, selection.Start)), options.TabSize);
            var text = options.InsertSpaces ? new string(' ', options.TabSize - column % options.TabSize) : "\t";
            return Insert(selection, text);
        }

        var changes = new List<TextChange>();
        for (var number = first; number <= last; number++)
        {
            var line = snapshot.GetLine(number);
            if (line.Length > 0)
                changes.Add(new TextChange(new TextSpan(line.Start, 0), options.IndentUnit));
        }

        return new EditResult(changes, MapSelection(selection, changes, growAtStart: true));
    }

    /// <summary>Removes one level of indentation from each selected line.</summary>
    public static EditResult Outdent(TextSnapshot snapshot, EditorSelection selection, EditorOptions options)
    {
        var (first, last) = SelectedLines(snapshot, selection);
        var changes = new List<TextChange>();
        for (var number = first; number <= last; number++)
        {
            var line = snapshot.GetLine(number);
            var text = snapshot.GetLineText(number);
            var remove = 0;
            var width = 0;
            while (remove < text.Length && width < options.TabSize)
            {
                if (text[remove] == '\t')
                {
                    remove++;
                    break;
                }

                if (text[remove] != ' ')
                    break;

                remove++;
                width++;
            }

            if (remove > 0)
                changes.Add(new TextChange(new TextSpan(line.Start, remove), ""));
        }

        return new EditResult(changes, MapSelection(selection, changes, growAtStart: false));
    }

    /// <summary>Comments the selected lines out with the language's line comment, or back in when they all are; a language with only block
    /// comments wraps the selection instead.</summary>
    public static EditResult ToggleComment(TextSnapshot snapshot, EditorSelection selection, LanguageDefinition? language)
    {
        if (language?.LineComment is { } token)
            return ToggleLineComment(snapshot, selection, token);

        if (language?.BlockComment is not { } block)
            return EditResult.Move(selection);

        var text = snapshot.GetText(selection.Span);
        if (text.StartsWith(block.Open, StringComparison.Ordinal) && text.EndsWith(block.Close, StringComparison.Ordinal) && text.Length >= block.Open.Length + block.Close.Length)
        {
            var inner = text[block.Open.Length..^block.Close.Length];
            return new EditResult([new TextChange(selection.Span, inner)], new EditorSelection(selection.Start, selection.Start + inner.Length));
        }

        var wrapped = block.Open + text + block.Close;
        return new EditResult([new TextChange(selection.Span, wrapped)], new EditorSelection(selection.Start, selection.Start + wrapped.Length));
    }

    /// <summary>Moves the selected lines one line up or down, keeping the selection on them.</summary>
    public static EditResult MoveLines(TextSnapshot snapshot, EditorSelection selection, bool down)
    {
        var (first, last) = SelectedLines(snapshot, selection);
        if (down ? last >= snapshot.LineCount - 1 : first == 0)
            return EditResult.Move(selection);

        var block = TextSpan.FromBounds(snapshot.GetLine(first).Start, snapshot.GetLine(last).End);
        var blockText = snapshot.GetText(block);
        if (down)
        {
            var below = snapshot.GetLine(last + 1);
            var belowText = snapshot.GetText(below.Span);
            var span = TextSpan.FromBounds(block.Start, below.End);
            var shift = belowText.Length + 1;
            return new EditResult([new TextChange(span, belowText + "\n" + blockText)], new EditorSelection(selection.Anchor + shift, selection.Caret + shift));
        }

        var above = snapshot.GetLine(first - 1);
        var aboveText = snapshot.GetText(above.Span);
        var upSpan = TextSpan.FromBounds(above.Start, block.End);
        var upShift = aboveText.Length + 1;
        return new EditResult([new TextChange(upSpan, blockText + "\n" + aboveText)], new EditorSelection(selection.Anchor - upShift, selection.Caret - upShift));
    }

    /// <summary>Copies the selected lines below themselves and selects the copy.</summary>
    public static EditResult DuplicateLines(TextSnapshot snapshot, EditorSelection selection)
    {
        var (first, last) = SelectedLines(snapshot, selection);
        var block = TextSpan.FromBounds(snapshot.GetLine(first).Start, snapshot.GetLine(last).End);
        var text = "\n" + snapshot.GetText(block);
        var shift = text.Length;
        return new EditResult([new TextChange(new TextSpan(block.End, 0), text)], new EditorSelection(selection.Anchor + shift, selection.Caret + shift));
    }

    /// <summary>Deletes the selected lines entirely.</summary>
    public static EditResult DeleteLines(TextSnapshot snapshot, EditorSelection selection)
    {
        var (first, last) = SelectedLines(snapshot, selection);
        var start = snapshot.GetLine(first).Start;
        var end = snapshot.GetLine(last).EndIncludingBreak;
        if (end == snapshot.Length && first > 0)
            start = snapshot.GetLine(first - 1).End;

        var caretLine = Math.Min(first, snapshot.LineCount - 1 - (last - first + 1));
        var span = TextSpan.FromBounds(start, end);
        var after = snapshot.Apply([new TextChange(span, "")]);
        var line = after.GetLine(Math.Clamp(caretLine, 0, after.LineCount - 1));
        return new EditResult([new TextChange(span, "")], EditorSelection.At(line.Start));
    }

    /// <summary>Gets the first and last line a selection touches; a selection that ends at the start of a line leaves that line out.</summary>
    public static (int First, int Last) SelectedLines(TextSnapshot snapshot, EditorSelection selection)
    {
        var first = snapshot.GetLineFromPosition(selection.Start).LineNumber;
        var lastLine = snapshot.GetLineFromPosition(selection.End);
        var last = lastLine.LineNumber > first && selection.End == lastLine.Start ? lastLine.LineNumber - 1 : lastLine.LineNumber;
        return (first, last);
    }

    /// <summary>Gets the column at the end of some text when tabs advance to the next tab stop.</summary>
    public static int VisualColumn(ReadOnlySpan<char> text, int tabSize)
    {
        var column = 0;
        foreach (var character in text)
            column = character == '\t' ? column + tabSize - column % tabSize : column + 1;
        return column;
    }

    /// <summary>Gets the spaces and tabs a line starts with.</summary>
    public static string LeadingWhitespace(string text)
    {
        var length = 0;
        while (length < text.Length && text[length] is ' ' or '\t')
            length++;
        return text[..length];
    }

    private static EditResult ToggleLineComment(TextSnapshot snapshot, EditorSelection selection, string token)
    {
        var (first, last) = SelectedLines(snapshot, selection);
        var lines = Enumerable.Range(first, last - first + 1).Select(n => (Line: snapshot.GetLine(n), Text: snapshot.GetLineText(n))).ToList();
        var nonBlank = lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).ToList();
        if (nonBlank.Count == 0)
            return EditResult.Move(selection);

        var changes = new List<TextChange>();
        var allCommented = nonBlank.All(l => l.Text.TrimStart().StartsWith(token, StringComparison.Ordinal));
        if (allCommented)
        {
            foreach (var (line, text) in nonBlank)
            {
                var at = text.IndexOf(token, StringComparison.Ordinal);
                var length = token.Length + (at + token.Length < text.Length && text[at + token.Length] == ' ' ? 1 : 0);
                changes.Add(new TextChange(new TextSpan(line.Start + at, length), ""));
            }
        }
        else
        {
            var column = nonBlank.Min(l => LeadingWhitespace(l.Text).Length);
            foreach (var (line, _) in nonBlank)
                changes.Add(new TextChange(new TextSpan(line.Start + column, 0), token + " "));
        }

        return new EditResult(changes, MapSelection(selection, changes, growAtStart: !allCommented));
    }

    private static EditResult Delete(TextSpan span) => new([new TextChange(span, "")], EditorSelection.At(span.Start));

    private static bool SpansWholeLine(TextSnapshot snapshot, EditorSelection selection, int lineNumber)
    {
        var line = snapshot.GetLine(lineNumber);
        return selection.Start <= line.Start && selection.End >= line.End;
    }

    // With growAtStart, text inserted right at a selection's start ends up inside the selection rather than before it.
    private static EditorSelection MapSelection(EditorSelection selection, IReadOnlyList<TextChange> changes, bool growAtStart)
    {
        int Map(int offset, bool keepBeforeInsert)
        {
            var delta = 0;
            foreach (var change in changes)
            {
                if (change.Span.Start > offset)
                    break;

                if (change.Span.IsEmpty && change.Span.Start == offset)
                {
                    if (keepBeforeInsert)
                        break;

                    delta += change.NewText.Length;
                    continue;
                }

                if (offset < change.Span.End)
                    return change.Span.Start + delta;

                delta += change.NewText.Length - change.Span.Length;
            }

            return offset + delta;
        }

        if (selection.IsEmpty)
            return EditorSelection.At(Map(selection.Caret, keepBeforeInsert: false));

        var start = Map(selection.Start, keepBeforeInsert: growAtStart);
        var end = Map(selection.End, keepBeforeInsert: false);
        return selection.Anchor <= selection.Caret ? new EditorSelection(start, end) : new EditorSelection(end, start);
    }

    private static char? ClosingBracket(LanguageDefinition language, char open)
    {
        foreach (var (o, c) in language.Brackets)
        {
            if (o == open)
                return c;
        }

        return null;
    }

    private static bool IsClosingBracket(LanguageDefinition language, char character) => language.Brackets.Any(b => b.Close == character);

    private static bool IsEmptyPair(LanguageDefinition language, char previous, char next) =>
        language.Brackets.Any(b => b.Open == previous && b.Close == next) || language.Quotes.Contains(previous) && previous == next;
}
