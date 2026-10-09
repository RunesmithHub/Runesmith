using Avalonia.Media.TextFormatting;
using Runesmith.Sdk.Languages;

namespace Runesmith.Editor.Rendering;

/// <summary>The text of one line, split into runs at the edges of its syntax tokens and of its inlay hints.</summary>
internal sealed class LineTextSource(string text, IReadOnlyList<SyntaxToken> tokens, TextFormatting formatting, LineInlays inlays, TextRunProperties? inlayProperties)
    : ITextSource
{
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

        var properties = formatting.Default;
        var end = Math.Min(limit, text.Length);
        foreach (var token in tokens)
        {
            var tokenEnd = Math.Min(token.Start + token.Length, text.Length);
            if (tokenEnd <= column)
                continue;

            if (token.Start > column)
            {
                end = Math.Min(end, token.Start);
                break;
            }

            properties = formatting.Get(token.Style);
            end = Math.Min(end, tokenEnd);
            break;
        }

        return new TextCharacters(text.AsMemory(column, end - column), properties);
    }
}
