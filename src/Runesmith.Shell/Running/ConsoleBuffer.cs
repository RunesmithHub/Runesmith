using System.Globalization;
using System.Text;

namespace Runesmith.Shell.Running;

/// <summary>Where a piece of a run's console text came from.</summary>
public enum ConsoleSource
{
    /// <summary>The program's standard output.</summary>
    Output,

    /// <summary>The program's standard error.</summary>
    Error,

    /// <summary>Runesmith's own messages, such as the exit code.</summary>
    System,

    /// <summary>What the user typed into the program's standard input.</summary>
    Input,
}

/// <summary>How a span of console text looks: one of the 16 basic ANSI colors, or -1 for the default, and bold.</summary>
public readonly record struct ConsoleStyle(int Foreground = -1, int Background = -1, bool Bold = false)
{
    public static ConsoleStyle Default { get; } = new(-1, -1, false);
}

/// <summary>A span of a console line with one style.</summary>
public readonly record struct ConsoleSpan(int Start, int Length, ConsoleStyle Style);

/// <summary>A line of console text without its escape sequences, with the styles they set.</summary>
public sealed record ConsoleLine(string Text, IReadOnlyList<ConsoleSpan> Spans, ConsoleSource Source);

/// <summary>What changed in a console since it was last drained.</summary>
/// <param name="FirstIndex">The number of the first line the console still keeps; older ones were dropped.</param>
/// <param name="From">The number of the first line that changed; lines from it on are replaced by <paramref name="Lines"/>.</param>
/// <param name="Lines">The lines from <paramref name="From"/> to the end.</param>
public sealed record ConsoleDelta(long FirstIndex, long From, IReadOnlyList<ConsoleLine> Lines);

/// <summary>Collects a run's output from any thread, turning ANSI escape sequences into styles and keeping at most a set number of lines;
/// a line without its end yet shows as it is and is replaced as it grows.</summary>
public sealed class ConsoleBuffer
{
    /// <summary>The number of lines a console keeps by default.</summary>
    public const int DefaultMaximumLines = 100_000;

    private const int MaximumEscapeLength = 64;

    private readonly Lock gate = new();
    private readonly List<ConsoleLine> lines = [];
    private readonly Dictionary<ConsoleSource, LineBuilder> builders = [];
    private readonly int maximumLines;
    private long firstIndex;
    private long changedFrom;

    public ConsoleBuffer(int maximumLines = DefaultMaximumLines) => this.maximumLines = Math.Max(10, maximumLines);

    /// <summary>Gets the number of lines kept now.</summary>
    public int Count
    {
        get
        {
            lock (gate)
                return lines.Count;
        }
    }

    /// <summary>Raised, on the appending thread, after text arrives.</summary>
    public event EventHandler? Changed;

    /// <summary>Adds text that may hold several lines, a part of a line and escape sequences.</summary>
    public void Append(string text, ConsoleSource stream)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (gate)
        {
            if (!builders.TryGetValue(stream, out var builder))
                builders[stream] = builder = new LineBuilder(stream);

            foreach (var c in text)
            {
                if (builder.Feed(c))
                    Finish(builder);
            }

            if (builder.HasText)
                Publish(builder, isOpen: true);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Adds a whole line.</summary>
    public void AppendLine(string text, ConsoleSource stream) => Append(text + "\n", stream);

    /// <summary>Removes every line.</summary>
    public void Clear()
    {
        lock (gate)
        {
            firstIndex += lines.Count;
            lines.Clear();
            changedFrom = firstIndex;
            foreach (var builder in builders.Values)
                builder.Reset();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets what changed since the last call, or every line kept when <paramref name="all"/> is set.</summary>
    public ConsoleDelta Drain(bool all = false)
    {
        lock (gate)
        {
            var end = firstIndex + lines.Count;
            var from = all ? firstIndex : Math.Clamp(changedFrom, firstIndex, end);
            var changed = lines.GetRange((int)(from - firstIndex), (int)(end - from));
            changedFrom = end;
            return new ConsoleDelta(firstIndex, from, changed);
        }
    }

    /// <summary>Gets the text of every line kept, such as for copying.</summary>
    public string GetText()
    {
        lock (gate)
            return string.Join('\n', lines.Select(l => l.Text));
    }

    private void Finish(LineBuilder builder)
    {
        Publish(builder, isOpen: false);
        builder.Reset();
    }

    private void Publish(LineBuilder builder, bool isOpen)
    {
        var line = builder.ToLine();
        var at = builder.OpenIndex - firstIndex;
        if (builder.OpenIndex >= 0 && at >= 0 && at < lines.Count)
        {
            lines[(int)at] = line;
            changedFrom = Math.Min(changedFrom, builder.OpenIndex);
        }
        else
        {
            changedFrom = Math.Min(changedFrom, firstIndex + lines.Count);
            lines.Add(line);
            builder.OpenIndex = firstIndex + lines.Count - 1;
        }

        if (!isOpen)
            builder.OpenIndex = -1;
        Trim();
    }

    private void Trim()
    {
        if (lines.Count <= maximumLines)
            return;

        var remove = lines.Count - maximumLines + (maximumLines / 10);
        lines.RemoveRange(0, remove);
        firstIndex += remove;
        foreach (var builder in builders.Values)
        {
            if (builder.OpenIndex < firstIndex)
                builder.OpenIndex = -1;
        }
    }

    // Turns characters into the text and styles of one line; keeps its style and an unfinished escape sequence across lines and calls.
    private sealed class LineBuilder(ConsoleSource source)
    {
        private readonly StringBuilder text = new();
        private readonly List<ConsoleSpan> spans = [];
        private readonly StringBuilder escape = new();
        private ConsoleStyle style = ConsoleStyle.Default;
        private bool inEscape;
        private bool pendingReturn;

        public long OpenIndex { get; set; } = -1;

        public bool HasText => text.Length > 0;

        /// <summary>Adds a character; returns whether it ended the line.</summary>
        public bool Feed(char c)
        {
            if (inEscape)
            {
                FeedEscape(c);
                return false;
            }

            if (pendingReturn)
            {
                pendingReturn = false;
                if (c == '\n')
                    return true;

                // A carriage return alone starts the line again, as progress bars use it.
                ClearText();
            }

            switch (c)
            {
                case '\n':
                    return true;
                case '\r':
                    pendingReturn = true;
                    return false;
                case '\u001b':
                    inEscape = true;
                    escape.Clear();
                    return false;
                case '\t':
                    Add(c);
                    return false;
                case < ' ':
                    return false;
                default:
                    Add(c);
                    return false;
            }
        }

        public ConsoleLine ToLine() => new(text.ToString(), [.. spans], source);

        public void Reset() => ClearText();

        private void ClearText()
        {
            text.Clear();
            spans.Clear();
        }

        private void Add(char c)
        {
            if (spans.Count > 0 && spans[^1].Style == style && spans[^1].Start + spans[^1].Length == text.Length)
                spans[^1] = spans[^1] with { Length = spans[^1].Length + 1 };
            else
                spans.Add(new ConsoleSpan(text.Length, 1, style));
            text.Append(c);
        }

        private void FeedEscape(char c)
        {
            escape.Append(c);
            if (escape.Length == 1)
            {
                if (c is not ('[' or ']'))
                    inEscape = false;
                return;
            }

            var isOsc = escape[0] == ']';
            var ended = isOsc ? c == '\a' || (c == '\\' && escape.Length > 1 && escape[^2] == '\u001b') : c is >= '@' and <= '~';
            if (!ended && escape.Length < MaximumEscapeLength)
                return;

            inEscape = false;
            if (!isOsc && c == 'm')
                style = ApplySgr(style, escape.ToString(1, escape.Length - 2));
        }
    }

    /// <summary>Applies the parameters of a Select Graphic Rendition sequence, such as <c>1;31</c>, to a style.</summary>
    internal static ConsoleStyle ApplySgr(ConsoleStyle style, string parameters)
    {
        var codes = parameters.Length == 0 ? ["0"] : parameters.Split(';');
        for (var i = 0; i < codes.Length; i++)
        {
            if (!int.TryParse(codes[i], NumberStyles.None, CultureInfo.InvariantCulture, out var code))
                code = 0;
            style = code switch
            {
                0 => ConsoleStyle.Default,
                1 => style with { Bold = true },
                22 => style with { Bold = false },
                >= 30 and <= 37 => style with { Foreground = code - 30 },
                39 => style with { Foreground = -1 },
                >= 40 and <= 47 => style with { Background = code - 40 },
                49 => style with { Background = -1 },
                >= 90 and <= 97 => style with { Foreground = code - 90 + 8 },
                >= 100 and <= 107 => style with { Background = code - 100 + 8 },
                _ => style,
            };

            // 256-color and true-color forms take extra parameters, which are skipped.
            if (code is 38 or 48 && i + 1 < codes.Length)
                i += codes[i + 1] == "5" ? 2 : codes[i + 1] == "2" ? 4 : 1;
        }

        return style;
    }
}
