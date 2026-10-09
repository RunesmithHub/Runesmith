using Runesmith.Text;

namespace Runesmith.Editor;

/// <summary>How a run of lines or characters of one side of a diff differs from the other side.</summary>
public enum DiffKind
{
    /// <summary>Only the newer side has it.</summary>
    Added,

    /// <summary>Only the older side has it.</summary>
    Removed,

    /// <summary>Both sides have it, differently.</summary>
    Changed,
}

/// <summary>Lines of one side of a diff that differ from the other side.</summary>
/// <param name="Start">The first line, counted from zero.</param>
/// <param name="Count">The number of lines.</param>
public readonly record struct DiffLineRange(int Start, int Count, DiffKind Kind);

/// <summary>Characters within changed lines that differ from the other side.</summary>
public readonly record struct DiffSpan(TextSpan Span, DiffKind Kind);

/// <summary>Blank rows drawn above a line, standing for lines only the other side of a diff has, so both sides stay aligned.</summary>
/// <param name="Line">The line the rows stand above; the line count puts them after the last line.</param>
/// <param name="Rows">The number of rows.</param>
public readonly record struct LineGap(int Line, int Rows);

/// <summary>What an editor showing one side of a diff draws: tinted lines, highlighted characters and blank rows.</summary>
/// <param name="Lines">The changed lines, in order.</param>
/// <param name="Spans">The changed characters, in order.</param>
/// <param name="Gaps">The blank rows, in order of their lines.</param>
public sealed record DiffDecorations(IReadOnlyList<DiffLineRange> Lines, IReadOnlyList<DiffSpan> Spans, IReadOnlyList<LineGap> Gaps)
{
    /// <summary>Gets decorations that draw nothing.</summary>
    public static DiffDecorations Empty { get; } = new([], [], []);
}
