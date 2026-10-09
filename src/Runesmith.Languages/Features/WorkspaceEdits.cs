using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Languages.Features;

/// <summary>Builds workspace edits from edits by line and column, which is how language servers and analyzers describe changes to files.</summary>
internal static class WorkspaceEdits
{
    /// <summary>Gets the text a file's line and column positions refer to: the open document's text, or the file on disk with its line
    /// breaks as <c>\n</c>; null when the file cannot be read.</summary>
    public static TextSnapshot? ReadBasis(IDocumentService documents, string path)
    {
        if (documents.Find(path) is { } document)
            return document.Buffer.Current;

        try
        {
            return TextSnapshot.Create(LineEndings.Normalize(File.ReadAllText(path)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Turns edits by position into changes of a snapshot, sorted by where they start; edits at the same place keep their order.</summary>
    public static IReadOnlyList<TextChange> ToChanges(TextSnapshot snapshot, IEnumerable<(TextPosition Start, TextPosition End, string NewText)> edits) =>
    [
        .. edits
            .Select((edit, index) =>
            {
                var start = Offset(snapshot, edit.Start);
                var end = Math.Max(start, Offset(snapshot, edit.End));
                return (Change: new TextChange(TextSpan.FromBounds(start, end), LineEndings.Normalize(edit.NewText)), Index: index);
            })
            .OrderBy(entry => entry.Change.Span.Start)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Change),
    ];

    /// <summary>Gets the offset of a position, clamped to the text, so a position past the end of a line is the line's end.</summary>
    public static int Offset(TextSnapshot snapshot, TextPosition position)
    {
        if (position.Line >= snapshot.LineCount)
            return snapshot.Length;

        var line = snapshot.GetLine(Math.Max(0, position.Line));
        return line.Start + Math.Clamp(position.Column, 0, line.Length);
    }
}
