using System.Globalization;
using System.Security.Cryptography;
using Runesmith.Text;

namespace Runesmith.Sdk.Documents.Snippets;

/// <summary>What a snippet's variables are read from where it is inserted.</summary>
/// <param name="Clipboard">The clipboard's text, or null when it was not read.</param>
internal sealed record SnippetContext(string? FilePath, string DocumentName, TextSnapshot Snapshot, TextSpan Selection, string? Clipboard = null)
{
    public string? LineComment { get; init; }

    public (string Open, string Close)? BlockComment { get; init; }

    public DateTimeOffset Now { get; init; } = DateTimeOffset.Now;
}

/// <summary>The variables snippets can use, such as <c>TM_FILENAME</c> and <c>CLIPBOARD</c>.</summary>
internal static class SnippetVariables
{
    public const string Clipboard = "CLIPBOARD";

    /// <summary>Gets a variable's value, or null when there is no such variable.</summary>
    public static string? Resolve(string name, SnippetContext context)
    {
        var snapshot = context.Snapshot;
        var caret = Math.Clamp(context.Selection.Start, 0, snapshot.Length);
        var line = snapshot.GetLineFromPosition(caret);
        var path = context.FilePath;
        var invariant = CultureInfo.InvariantCulture;
        var now = context.Now;
        return name switch
        {
            "TM_SELECTED_TEXT" or "SELECTED_TEXT" => snapshot.GetText(context.Selection),
            "TM_CURRENT_LINE" => snapshot.GetLineText(line.LineNumber),
            "TM_CURRENT_WORD" => snapshot.GetText(WordBoundaries.GetWordAt(snapshot, caret)),
            "TM_LINE_INDEX" => line.LineNumber.ToString(invariant),
            "TM_LINE_NUMBER" => (line.LineNumber + 1).ToString(invariant),
            "TM_FILENAME" => path is null ? context.DocumentName : Path.GetFileName(path),
            "TM_FILENAME_BASE" => Path.GetFileNameWithoutExtension(path ?? context.DocumentName),
            "TM_DIRECTORY" => path is null ? "" : Path.GetDirectoryName(path) ?? "",
            "TM_FILEPATH" => path ?? context.DocumentName,
            Clipboard => context.Clipboard ?? "",
            "CURRENT_YEAR" => now.ToString("yyyy", invariant),
            "CURRENT_YEAR_SHORT" => now.ToString("yy", invariant),
            "CURRENT_MONTH" => now.ToString("MM", invariant),
            "CURRENT_MONTH_NAME" => now.ToString("MMMM", invariant),
            "CURRENT_MONTH_NAME_SHORT" => now.ToString("MMM", invariant),
            "CURRENT_DATE" => now.ToString("dd", invariant),
            "CURRENT_DAY_NAME" => now.ToString("dddd", invariant),
            "CURRENT_DAY_NAME_SHORT" => now.ToString("ddd", invariant),
            "CURRENT_HOUR" => now.ToString("HH", invariant),
            "CURRENT_MINUTE" => now.ToString("mm", invariant),
            "CURRENT_SECOND" => now.ToString("ss", invariant),
            "CURRENT_SECONDS_UNIX" => now.ToUnixTimeSeconds().ToString(invariant),
            "RANDOM" => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", invariant),
            "RANDOM_HEX" => RandomNumberGenerator.GetHexString(6, lowercase: true),
            "UUID" => Guid.NewGuid().ToString(),
            "LINE_COMMENT" => context.LineComment ?? context.BlockComment?.Open ?? "",
            "BLOCK_COMMENT_START" => context.BlockComment?.Open ?? context.LineComment ?? "",
            "BLOCK_COMMENT_END" => context.BlockComment?.Close ?? "",
            _ => null,
        };
    }

    /// <summary>Gets the indentation at the start of the line an offset is on.</summary>
    public static string IndentOf(TextSnapshot snapshot, int offset)
    {
        var text = snapshot.GetLineText(snapshot.GetLineFromPosition(Math.Clamp(offset, 0, snapshot.Length)).LineNumber);
        var length = 0;
        while (length < text.Length && text[length] is ' ' or '\t')
            length++;
        return text[..length];
    }
}
