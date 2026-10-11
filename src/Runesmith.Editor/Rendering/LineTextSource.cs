using Avalonia.Media.TextFormatting;
using Runesmith.Sdk.Languages;

namespace Runesmith.Editor.Rendering;

/// <summary>The text of one line, split into runs at the edges of its syntax tokens and of its inlay hints; neighbors drawn alike share a
/// run, so the line is shaped and drawn in as few pieces as it can be.</summary>
internal sealed class LineTextSource(string text, IReadOnlyList<SyntaxToken> tokens, TextFormatting formatting, LineInlays inlays, TextRunProperties? inlayProperties)
    : ITextSource
{
    // The formatter asks for runs from the start of the line onward, so the search for a column's token resumes where the last one ended.
    private int nextToken;
    private int lastColumn;

    public LineTextSource(string text, IReadOnlyList<SyntaxToken> tokens, TextFormatting formatting)
        : this(text, tokens, formatting, LineInlays.None, null)
    {
    }

    public TextRun? GetTextRun(int textSourceIndex)
    {
        if (inlays.IsEmpty)
            return Run(textSourceIndex, text.Length);

        if (inlays.At(textSourceIndex) is { } inlay)
            return new TextCharacters(inlay.Text.AsMemory(textSourceIndex - inlay.Start), inlayProperties ?? formatting.Default);

        var column = inlays.ToColumn(textSourceIndex);
        var next = inlays.NextStart(textSourceIndex, int.MaxValue);
        return Run(column, next == int.MaxValue ? text.Length : column + (next - textSourceIndex));
    }

    private TextRun Run(int column, int limit)
    {
        if (column >= text.Length)
            return new TextEndOfParagraph();

        var end = Math.Min(limit, text.Length);
        var properties = Segment(column, out var segmentEnd);
        var runEnd = Math.Min(end, segmentEnd);
        while (runEnd < end && ReferenceEquals(Segment(runEnd, out segmentEnd), properties))
            runEnd = Math.Min(end, segmentEnd);

        return new TextCharacters(text.AsMemory(column, runEnd - column), properties);
    }

    // The properties of the token, or of the gap between tokens, at a column, and the column where it ends.
    private TextRunProperties Segment(int column, out int end)
    {
        if (column < lastColumn)
            nextToken = 0;
        lastColumn = column;

        while (nextToken < tokens.Count && Math.Min(tokens[nextToken].Start + tokens[nextToken].Length, text.Length) <= column)
            nextToken++;

        if (nextToken < tokens.Count && tokens[nextToken].Start <= column)
        {
            end = Math.Min(tokens[nextToken].Start + tokens[nextToken].Length, text.Length);
            return formatting.Get(tokens[nextToken].Style);
        }

        end = nextToken < tokens.Count ? Math.Min(tokens[nextToken].Start, text.Length) : text.Length;
        return formatting.Default;
    }
}
