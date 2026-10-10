using System.Globalization;
using System.Text;

namespace Runesmith.Shell.Terminal;

/// <summary>How the program wants mouse events reported, as DEC private modes 9, 1000, 1002 and 1003 set it.</summary>
internal enum MouseTracking
{
    None,
    Press,
    PressAndRelease,
    Drag,
    Motion,
}

/// <summary>How the cursor is drawn, as DECSCUSR sets it.</summary>
internal enum CursorShape
{
    Block,
    Underline,
    Bar,
}

/// <summary>A point in the terminal's text: a line counted from the first line ever written, so it stays put while the scrollback grows,
/// and a column.</summary>
internal readonly record struct TerminalPoint(long Line, int Column) : IComparable<TerminalPoint>
{
    public int CompareTo(TerminalPoint other) => Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);

    public static bool operator <(TerminalPoint left, TerminalPoint right) => left.CompareTo(right) < 0;

    public static bool operator >(TerminalPoint left, TerminalPoint right) => left.CompareTo(right) > 0;

    public static bool operator <=(TerminalPoint left, TerminalPoint right) => left.CompareTo(right) <= 0;

    public static bool operator >=(TerminalPoint left, TerminalPoint right) => left.CompareTo(right) >= 0;
}

/// <summary>An xterm-compatible terminal screen: the grid of cells and the scrollback, the cursor, the alternate screen, scroll regions,
/// modes and the replies programs ask for, driven by the output of the program in the terminal.</summary>
/// <remarks>It is not thread-safe; its owner feeds it and reads it under one lock.</remarks>
internal sealed class TerminalScreen : IVtHandler
{
    private const string DecSpecialGraphics = "◆▒␉␌␍␊°±␤␋┘┐┌└┼⎺⎻─⎼⎽├┤┴┬│≤≥π≠£·";

    private readonly VtParser parser;
    private readonly Decoder decoder = new UTF8Encoding(false).GetDecoder();
    private readonly char[] decoded = new char[16384];
    private TerminalLine[] primary;
    private TerminalLine[] alternate;
    private TerminalLine[] scrollback;
    private int scrollbackStart;
    private int scrollbackCount;
    private bool[] tabStops;
    private CellStyle style;
    private int cursorRow;
    private int cursorColumn;
    private bool wrapPending;
    private int scrollTop;
    private int scrollBottom;
    private SavedCursor savedPrimary;
    private SavedCursor savedAlternate;
    private char[] charsets = ['B', 'B'];
    private int activeCharset;
    private int lastPrinted = ' ';

    /// <summary>Creates a screen of <paramref name="columns"/> × <paramref name="rows"/> that keeps up to <paramref name="scrollbackLines"/>
    /// lines that scrolled off its top.</summary>
    public TerminalScreen(int columns, int rows, int scrollbackLines = 10_000)
    {
        Columns = Math.Max(2, columns);
        Rows = Math.Max(1, rows);
        primary = NewLines(Rows);
        alternate = NewLines(Rows);
        scrollback = new TerminalLine[Math.Max(0, scrollbackLines)];
        tabStops = DefaultTabStops(Columns);
        scrollBottom = Rows - 1;
        parser = new VtParser(this);
    }

    public int Columns { get; private set; }

    public int Rows { get; private set; }

    public int CursorRow => cursorRow;

    public int CursorColumn => cursorColumn;

    public bool IsAlternateScreen { get; private set; }

    public bool CursorVisible { get; private set; } = true;

    public CursorShape CursorShape { get; private set; }

    public bool ApplicationCursorKeys { get; private set; }

    public bool ApplicationKeypad { get; private set; }

    public bool BracketedPaste { get; private set; }

    public bool AutoWrap { get; private set; } = true;

    public bool OriginMode { get; private set; }

    public bool InsertMode { get; private set; }

    public bool LineFeedNewLine { get; private set; }

    public bool ReportsFocus { get; private set; }

    public MouseTracking MouseTracking { get; private set; }

    public bool SgrMouse { get; private set; }

    public int ScrollTop => scrollTop;

    public int ScrollBottom => scrollBottom;

    /// <summary>Gets the window title the program set, or null.</summary>
    public string? Title { get; private set; }

    /// <summary>Gets the folder the shell is in, when its shell integration says so, or null.</summary>
    public string? WorkingDirectory { get; private set; }

    /// <summary>Gets the exit code of the last command, when the shell integration says so, or null.</summary>
    public int? LastExitCode { get; private set; }

    /// <summary>Gets the number of lines in the scrollback.</summary>
    public int ScrollbackCount => scrollbackCount;

    /// <summary>Gets the number of lines dropped from the start of the scrollback since the screen was made, so that
    /// <see cref="TerminalPoint.Line"/> stays put.</summary>
    public long DroppedLines { get; private set; }

    /// <summary>Gets the number of lines there are: the scrollback's and the screen's.</summary>
    public int TotalLines => scrollbackCount + Rows;

    /// <summary>Gets or sets what receives the replies the program asks for, such as the cursor's position.</summary>
    public Action<string>? Reply { get; set; }

    /// <summary>Gets or sets what rings the bell.</summary>
    public Action? Bell { get; set; }

    /// <summary>Gets or sets what answers queries for the default foreground (10) and background (11) colors, as <c>rgb:rrrr/gggg/bbbb</c>.</summary>
    public Func<int, string?>? ColorQuery { get; set; }

    private TerminalLine[] Screen => IsAlternateScreen ? alternate : primary;

    /// <summary>Feeds the program's output, as UTF-8 that may split characters across calls.</summary>
    public void Feed(ReadOnlySpan<byte> bytes)
    {
        while (bytes.Length > 0)
        {
            var take = Math.Min(bytes.Length, decoded.Length / 2);
            var count = decoder.GetChars(bytes[..take], decoded, flush: false);
            parser.Feed(decoded.AsSpan(0, count));
            bytes = bytes[take..];
        }
    }

    /// <summary>Feeds text, as from a test.</summary>
    public void Feed(string text) => parser.Feed(text.AsSpan());

    /// <summary>Gets a line by its index among <see cref="TotalLines"/>: the scrollback's first, then the screen's.</summary>
    public TerminalLine LineAt(int index) =>
        index < scrollbackCount ? scrollback[(scrollbackStart + index) % scrollback.Length] : Screen[Math.Clamp(index - scrollbackCount, 0, Rows - 1)];

    /// <summary>Gets a row of the screen.</summary>
    public TerminalLine Row(int row) => Screen[row];

    /// <summary>Gets a row's text without trailing spaces.</summary>
    public string RowText(int row) => Screen[row].GetText();

    /// <summary>Gets a cell of the screen.</summary>
    public TerminalCell Cell(int row, int column) => Screen[row].Cells[column];

    /// <summary>Gets the index among <see cref="TotalLines"/> of a point's line, which may be before the first line or after the last.</summary>
    public long IndexOf(TerminalPoint point) => point.Line - DroppedLines;

    /// <summary>Gets the point of a line by its index among <see cref="TotalLines"/>.</summary>
    public TerminalPoint PointAt(int index, int column) => new(DroppedLines + index, column);

    /// <summary>Gets the text between two points, joining wrapped lines and ending other lines with a line break.</summary>
    public string GetText(TerminalPoint start, TerminalPoint end)
    {
        if (end < start)
            (start, end) = (end, start);

        var text = new StringBuilder();
        var first = Math.Max(0, IndexOf(start));
        var last = Math.Min(TotalLines - 1, IndexOf(end));
        for (var index = first; index <= last; index++)
        {
            var line = LineAt((int)index);
            var from = index == IndexOf(start) ? start.Column : 0;
            var to = index == IndexOf(end) ? end.Column : line.Length;
            text.Append(line.GetText(from, to));
            if (index < last && !line.IsWrapped)
                text.Append('\n');
        }

        return text.ToString();
    }

    /// <summary>Gets the whole text, scrollback and screen, without the blank lines at the end.</summary>
    public string GetAllText() => GetText(PointAt(0, 0), PointAt(TotalLines - 1, Columns)).TrimEnd('\n');

    /// <summary>Clears the scrollback and the screen but the cursor's line, which moves to the top, such as for a Clear command.</summary>
    public void Clear()
    {
        scrollbackCount = 0;
        scrollbackStart = 0;
        var screen = Screen;
        var current = screen[cursorRow];
        for (var i = 0; i < Rows; i++)
            screen[i] = i == 0 ? current : new TerminalLine(Columns, CellStyle.Default);
        cursorRow = 0;
    }

    /// <summary>Changes the size of the screen. Lines are cut or padded, not wrapped again; when the screen gets shorter, lines move into the
    /// scrollback so the cursor stays on screen, and when it gets taller, they come back.</summary>
    public void Resize(int columns, int rows)
    {
        columns = Math.Max(2, columns);
        rows = Math.Max(1, rows);
        if (columns == Columns && rows == Rows)
            return;

        foreach (var line in primary.Concat(alternate))
            line.Resize(columns, CellStyle.Default);

        var primaryRow = IsAlternateScreen ? savedPrimary.Row : cursorRow;
        primary = ResizePrimary(primary, rows, ref primaryRow);
        if (IsAlternateScreen)
            savedPrimary = savedPrimary with { Row = primaryRow };
        else
            cursorRow = primaryRow;

        var resized = new TerminalLine[rows];
        for (var i = 0; i < rows; i++)
            resized[i] = i < alternate.Length ? alternate[i] : new TerminalLine(columns, CellStyle.Default);
        alternate = resized;

        Columns = columns;
        Rows = rows;
        var stops = DefaultTabStops(columns);
        Array.Copy(tabStops, stops, Math.Min(tabStops.Length, columns));
        tabStops = stops;
        scrollTop = 0;
        scrollBottom = rows - 1;
        wrapPending = false;
        cursorRow = Math.Clamp(cursorRow, 0, rows - 1);
        cursorColumn = Math.Clamp(cursorColumn, 0, columns - 1);
        savedPrimary = savedPrimary.Clamp(columns, rows);
        savedAlternate = savedAlternate.Clamp(columns, rows);
    }

    private TerminalLine[] ResizePrimary(TerminalLine[] lines, int rows, ref int row)
    {
        var list = lines.ToList();
        if (rows < list.Count)
        {
            var remove = list.Count - rows;
            while (remove > 0 && list.Count - 1 > row && list[^1].IsBlank)
            {
                list.RemoveAt(list.Count - 1);
                remove--;
            }

            for (var i = 0; i < remove; i++)
            {
                PushScrollback(list[0]);
                list.RemoveAt(0);
                row--;
            }
        }
        else
        {
            var add = rows - list.Count;
            while (add > 0 && scrollbackCount > 0)
            {
                var line = scrollback[(scrollbackStart + scrollbackCount - 1) % scrollback.Length];
                scrollbackCount--;
                line.Resize(lines.Length > 0 ? lines[0].Length : Columns, CellStyle.Default);
                list.Insert(0, line);
                row++;
                add--;
            }

            var columns = list.Count > 0 ? list[0].Length : Columns;
            for (var i = 0; i < add; i++)
                list.Add(new TerminalLine(columns, CellStyle.Default));
        }

        row = Math.Clamp(row, 0, rows - 1);
        return [.. list];
    }

    void IVtHandler.Print(int codePoint) => Print(codePoint);

    void IVtHandler.Execute(int control)
    {
        switch (control)
        {
            case 0x07:
                Bell?.Invoke();
                break;
            case 0x08:
                wrapPending = false;
                if (cursorColumn > 0)
                    cursorColumn--;
                break;
            case 0x09:
                cursorColumn = NextTabStop(cursorColumn);
                wrapPending = false;
                break;
            case 0x0A or 0x0B or 0x0C:
                LineFeed();
                if (LineFeedNewLine)
                    cursorColumn = 0;
                break;
            case 0x0D:
                cursorColumn = 0;
                wrapPending = false;
                break;
            case 0x0E:
                activeCharset = 1;
                break;
            case 0x0F:
                activeCharset = 0;
                break;
        }
    }

    void IVtHandler.Escape(string intermediates, char final)
    {
        if (intermediates.Length == 1 && intermediates[0] is '(' or ')' or '*' or '+')
        {
            var index = intermediates[0] is '(' or '*' ? 0 : 1;
            charsets[index] = final;
            return;
        }

        if (intermediates == "#" && final == '8')
        {
            foreach (var line in Screen)
                Array.Fill(line.Cells, new TerminalCell { Rune = 'E', Width = 1 });
            return;
        }

        if (intermediates.Length > 0)
            return;

        switch (final)
        {
            case '7':
                SaveCursor();
                break;
            case '8':
                RestoreCursor();
                break;
            case 'D':
                LineFeed();
                break;
            case 'E':
                LineFeed();
                cursorColumn = 0;
                break;
            case 'H':
                tabStops[cursorColumn] = true;
                break;
            case 'M':
                ReverseIndex();
                break;
            case 'c':
                FullReset();
                break;
            case '=':
                ApplicationKeypad = true;
                break;
            case '>':
                ApplicationKeypad = false;
                break;
        }
    }

    void IVtHandler.Csi(VtParameters p, char marker, string intermediates, char final)
    {
        if (marker == '?')
        {
            if (intermediates.Length == 0 && final is 'h' or 'l')
            {
                for (var i = 0; i < p.Count; i++)
                    SetPrivateMode(p[i], final == 'h');
            }

            return;
        }

        if (marker == '>')
        {
            if (final == 'c' && p.Get(0, 0, zeroIsDefault: false) == 0)
                Reply?.Invoke("\u001b[>1;10;0c");
            return;
        }

        if (marker != '\0')
            return;

        if (intermediates == " " && final == 'q')
        {
            CursorShape = p.Get(0, 0, zeroIsDefault: false) switch
            {
                3 or 4 => CursorShape.Underline,
                5 or 6 => CursorShape.Bar,
                _ => CursorShape.Block,
            };
            return;
        }

        if (intermediates == "!" && final == 'p')
        {
            SoftReset();
            return;
        }

        if (intermediates.Length > 0)
            return;

        var n = p.Get(0, 1);
        switch (final)
        {
            case '@':
                InsertCharacters(n);
                break;
            case 'A':
                MoveCursor(cursorRow - n, cursorColumn, withinRegion: true);
                break;
            case 'B':
            case 'e':
                MoveCursor(cursorRow + n, cursorColumn, withinRegion: true);
                break;
            case 'C':
            case 'a':
                MoveCursor(cursorRow, cursorColumn + n, withinRegion: true);
                break;
            case 'D':
                MoveCursor(cursorRow, cursorColumn - n, withinRegion: true);
                break;
            case 'E':
                MoveCursor(cursorRow + n, 0, withinRegion: true);
                break;
            case 'F':
                MoveCursor(cursorRow - n, 0, withinRegion: true);
                break;
            case 'G':
            case '`':
                MoveCursor(cursorRow, n - 1, withinRegion: true);
                break;
            case 'H':
            case 'f':
                SetCursor(p.Get(0, 1) - 1, p.Get(1, 1) - 1);
                break;
            case 'I':
                for (var i = 0; i < n; i++)
                    cursorColumn = NextTabStop(cursorColumn);
                wrapPending = false;
                break;
            case 'J':
                EraseInDisplay(p.Get(0, 0, zeroIsDefault: false));
                break;
            case 'K':
                EraseInLine(p.Get(0, 0, zeroIsDefault: false));
                break;
            case 'L':
                InsertLines(n);
                break;
            case 'M':
                DeleteLines(n);
                break;
            case 'P':
                DeleteCharacters(n);
                break;
            case 'S':
                ScrollUp(n);
                break;
            case 'T':
                ScrollDown(n);
                break;
            case 'X':
                EraseCharacters(n);
                break;
            case 'Z':
                for (var i = 0; i < n; i++)
                    cursorColumn = PreviousTabStop(cursorColumn);
                wrapPending = false;
                break;
            case 'b':
                for (var i = 0; i < Math.Min(n, Columns * Rows); i++)
                    Print(lastPrinted);
                break;
            case 'c':
                if (p.Get(0, 0, zeroIsDefault: false) == 0)
                    Reply?.Invoke("\u001b[?62;22c");
                break;
            case 'd':
                SetCursor(n - 1, cursorColumn, keepColumn: true);
                break;
            case 'g':
                ClearTabStops(p.Get(0, 0, zeroIsDefault: false));
                break;
            case 'h':
            case 'l':
                for (var i = 0; i < p.Count; i++)
                {
                    if (p[i] == 4)
                        InsertMode = final == 'h';
                    else if (p[i] == 20)
                        LineFeedNewLine = final == 'h';
                }

                break;
            case 'm':
                SelectGraphicRendition(p);
                break;
            case 'n':
                ReportStatus(p.Get(0, 0, zeroIsDefault: false));
                break;
            case 'r':
                SetScrollRegion(p.Get(0, 1) - 1, p.Get(1, Rows) - 1);
                break;
            case 's':
                SaveCursor();
                break;
            case 'u':
                RestoreCursor();
                break;
            case 't':
                if (p.Get(0, 0, zeroIsDefault: false) == 18)
                    Reply?.Invoke(string.Create(CultureInfo.InvariantCulture, $"\u001b[8;{Rows};{Columns}t"));
                break;
        }
    }

    void IVtHandler.OperatingSystemCommand(string text)
    {
        var separator = text.IndexOf(';', StringComparison.Ordinal);
        var command = separator < 0 ? text : text[..separator];
        var argument = separator < 0 ? "" : text[(separator + 1)..];
        switch (command)
        {
            case "0" or "2":
                Title = argument.Length == 0 ? null : argument;
                break;
            case "7":
                if (Uri.TryCreate(argument, UriKind.Absolute, out var uri) && uri.IsFile)
                    WorkingDirectory = Uri.UnescapeDataString(uri.AbsolutePath);
                break;
            case "133":
                if (argument.StartsWith("D;", StringComparison.Ordinal) && int.TryParse(argument.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                    LastExitCode = code;
                break;
            case "10" or "11":
                if (argument == "?" && ColorQuery?.Invoke(command == "10" ? 10 : 11) is { } color)
                    Reply?.Invoke($"\u001b]{command};{color}\u001b\\");
                break;
        }
    }

    private void Print(int codePoint)
    {
        var charset = charsets[activeCharset];
        if (charset == '0' && codePoint is >= 0x60 and <= 0x7E)
            codePoint = DecSpecialGraphics[codePoint - 0x60];
        else if (charset == 'A' && codePoint == '#')
            codePoint = '£';

        var width = CharacterWidth.Of(codePoint);
        var screen = Screen;
        if (width == 0)
        {
            var column = wrapPending ? cursorColumn : cursorColumn - 1;
            if (column >= 0 && column < Columns)
            {
                ref var cell = ref screen[cursorRow].Cells[column];
                if (cell.Width == 0 && column > 0)
                    cell = ref screen[cursorRow].Cells[column - 1];
                if (cell.Rune != 0)
                    cell.Combining += char.ConvertFromUtf32(codePoint);
            }

            return;
        }

        if (wrapPending && AutoWrap)
        {
            screen[cursorRow].IsWrapped = true;
            LineFeed();
            cursorColumn = 0;
        }

        wrapPending = false;
        if (width == 2 && cursorColumn == Columns - 1)
        {
            if (!AutoWrap)
                return;

            ClearWideAt(screen[cursorRow], cursorColumn);
            screen[cursorRow].Cells[cursorColumn] = TerminalCell.Blank(style.Erased);
            screen[cursorRow].IsWrapped = true;
            LineFeed();
            cursorColumn = 0;
            screen = Screen;
        }

        var line = screen[cursorRow];
        if (InsertMode)
            ShiftRight(line, cursorColumn, width);

        ClearWideAt(line, cursorColumn);
        if (width == 2)
            ClearWideAt(line, cursorColumn + 1);
        line.Cells[cursorColumn] = new TerminalCell { Rune = codePoint, Width = (byte)width, Style = style };
        if (width == 2)
            line.Cells[cursorColumn + 1] = new TerminalCell { Width = 0, Style = style };
        lastPrinted = codePoint;

        if (cursorColumn + width >= Columns)
        {
            cursorColumn = Columns - 1;
            wrapPending = AutoWrap;
        }
        else
            cursorColumn += width;
    }

    // Overwriting half of a wide character leaves the other half as an empty cell.
    private static void ClearWideAt(TerminalLine line, int column)
    {
        if (column < 0 || column >= line.Length)
            return;

        var cell = line.Cells[column];
        if (cell.Width == 0 && column > 0 && line.Cells[column - 1].Width == 2)
            line.Cells[column - 1] = TerminalCell.Blank(line.Cells[column - 1].Style);
        else if (cell.Width == 2 && column + 1 < line.Length)
            line.Cells[column + 1] = TerminalCell.Blank(cell.Style);
    }

    private void ShiftRight(TerminalLine line, int column, int count)
    {
        var cells = line.Cells;
        count = Math.Min(count, Columns - column);
        Array.Copy(cells, column, cells, column + count, Columns - column - count);
        for (var i = column; i < column + count; i++)
            cells[i] = TerminalCell.Blank(style.Erased);
        if (cells[Columns - 1].Width == 2)
            cells[Columns - 1] = TerminalCell.Blank(cells[Columns - 1].Style);
    }

    private void LineFeed()
    {
        wrapPending = false;
        if (cursorRow == scrollBottom)
            ScrollUp(1);
        else if (cursorRow < Rows - 1)
            cursorRow++;
    }

    private void ReverseIndex()
    {
        wrapPending = false;
        if (cursorRow == scrollTop)
            ScrollDown(1);
        else if (cursorRow > 0)
            cursorRow--;
    }

    private void ScrollUp(int count)
    {
        var screen = Screen;
        count = Math.Min(count, scrollBottom - scrollTop + 1);
        for (var i = 0; i < count; i++)
        {
            var gone = screen[scrollTop];
            if (scrollTop == 0 && !IsAlternateScreen)
                PushScrollback(gone);
            Array.Copy(screen, scrollTop + 1, screen, scrollTop, scrollBottom - scrollTop);
            screen[scrollBottom] = new TerminalLine(Columns, style.Erased);
        }
    }

    private void ScrollDown(int count)
    {
        var screen = Screen;
        count = Math.Min(count, scrollBottom - scrollTop + 1);
        for (var i = 0; i < count; i++)
        {
            Array.Copy(screen, scrollTop, screen, scrollTop + 1, scrollBottom - scrollTop);
            screen[scrollTop] = new TerminalLine(Columns, style.Erased);
        }
    }

    private void PushScrollback(TerminalLine line)
    {
        if (scrollback.Length == 0)
        {
            DroppedLines++;
            return;
        }

        if (scrollbackCount < scrollback.Length)
        {
            scrollback[(scrollbackStart + scrollbackCount) % scrollback.Length] = line;
            scrollbackCount++;
        }
        else
        {
            scrollback[scrollbackStart] = line;
            scrollbackStart = (scrollbackStart + 1) % scrollback.Length;
            DroppedLines++;
        }
    }

    private void InsertLines(int count)
    {
        if (cursorRow < scrollTop || cursorRow > scrollBottom)
            return;

        var screen = Screen;
        count = Math.Min(count, scrollBottom - cursorRow + 1);
        Array.Copy(screen, cursorRow, screen, cursorRow + count, scrollBottom - cursorRow + 1 - count);
        for (var i = cursorRow; i < cursorRow + count; i++)
            screen[i] = new TerminalLine(Columns, style.Erased);
        cursorColumn = 0;
        wrapPending = false;
    }

    private void DeleteLines(int count)
    {
        if (cursorRow < scrollTop || cursorRow > scrollBottom)
            return;

        var screen = Screen;
        count = Math.Min(count, scrollBottom - cursorRow + 1);
        Array.Copy(screen, cursorRow + count, screen, cursorRow, scrollBottom - cursorRow + 1 - count);
        for (var i = scrollBottom - count + 1; i <= scrollBottom; i++)
            screen[i] = new TerminalLine(Columns, style.Erased);
        cursorColumn = 0;
        wrapPending = false;
    }

    private void InsertCharacters(int count)
    {
        var line = Screen[cursorRow];
        ClearWideAt(line, cursorColumn);
        ShiftRight(line, cursorColumn, count);
        wrapPending = false;
    }

    private void DeleteCharacters(int count)
    {
        var line = Screen[cursorRow];
        var cells = line.Cells;
        count = Math.Min(count, Columns - cursorColumn);
        ClearWideAt(line, cursorColumn);
        ClearWideAt(line, cursorColumn + count);
        Array.Copy(cells, cursorColumn + count, cells, cursorColumn, Columns - cursorColumn - count);
        for (var i = Columns - count; i < Columns; i++)
            cells[i] = TerminalCell.Blank(style.Erased);
        line.IsWrapped = false;
        wrapPending = false;
    }

    private void EraseCharacters(int count)
    {
        var line = Screen[cursorRow];
        count = Math.Min(count, Columns - cursorColumn);
        ClearWideAt(line, cursorColumn);
        ClearWideAt(line, cursorColumn + count - 1);
        Erase(line, cursorColumn, cursorColumn + count);
        wrapPending = false;
    }

    private void Erase(TerminalLine line, int from, int to)
    {
        for (var i = Math.Max(0, from); i < Math.Min(to, line.Length); i++)
            line.Cells[i] = TerminalCell.Blank(style.Erased);
        if (to >= line.Length)
            line.IsWrapped = false;
    }

    private void EraseInLine(int mode)
    {
        var line = Screen[cursorRow];
        switch (mode)
        {
            case 0:
                ClearWideAt(line, cursorColumn);
                Erase(line, cursorColumn, Columns);
                break;
            case 1:
                ClearWideAt(line, cursorColumn);
                Erase(line, 0, cursorColumn + 1);
                break;
            case 2:
                Erase(line, 0, Columns);
                break;
        }
    }

    private void EraseInDisplay(int mode)
    {
        var screen = Screen;
        switch (mode)
        {
            case 0:
                EraseInLine(0);
                for (var i = cursorRow + 1; i < Rows; i++)
                    Erase(screen[i], 0, Columns);
                break;
            case 1:
                EraseInLine(1);
                for (var i = 0; i < cursorRow; i++)
                    Erase(screen[i], 0, Columns);
                break;
            case 2:
                for (var i = 0; i < Rows; i++)
                    Erase(screen[i], 0, Columns);
                break;
            case 3:
                scrollbackCount = 0;
                scrollbackStart = 0;
                break;
        }
    }

    private void MoveCursor(int row, int column, bool withinRegion)
    {
        var (top, bottom) = withinRegion && cursorRow >= scrollTop && cursorRow <= scrollBottom ? (scrollTop, scrollBottom) : (0, Rows - 1);
        cursorRow = Math.Clamp(row, top, bottom);
        cursorColumn = Math.Clamp(column, 0, Columns - 1);
        wrapPending = false;
    }

    private void SetCursor(int row, int column, bool keepColumn = false)
    {
        var (top, bottom) = OriginMode ? (scrollTop, scrollBottom) : (0, Rows - 1);
        cursorRow = Math.Clamp(row + top, top, bottom);
        if (!keepColumn)
            cursorColumn = Math.Clamp(column, 0, Columns - 1);
        wrapPending = false;
    }

    private void SetScrollRegion(int top, int bottom)
    {
        bottom = Math.Min(bottom, Rows - 1);
        if (top < 0 || top >= bottom)
            return;

        scrollTop = top;
        scrollBottom = bottom;
        SetCursor(0, 0);
    }

    private int NextTabStop(int column)
    {
        for (var i = column + 1; i < Columns; i++)
        {
            if (tabStops[i])
                return i;
        }

        return Columns - 1;
    }

    private int PreviousTabStop(int column)
    {
        for (var i = column - 1; i > 0; i--)
        {
            if (tabStops[i])
                return i;
        }

        return 0;
    }

    private void ClearTabStops(int mode)
    {
        if (mode == 0)
            tabStops[cursorColumn] = false;
        else if (mode == 3)
            Array.Clear(tabStops);
    }

    private void ReportStatus(int what)
    {
        if (what == 5)
            Reply?.Invoke("\u001b[0n");
        else if (what == 6)
        {
            var row = cursorRow - (OriginMode ? scrollTop : 0) + 1;
            Reply?.Invoke(string.Create(CultureInfo.InvariantCulture, $"\u001b[{row};{cursorColumn + 1}R"));
        }
    }

    private void SetPrivateMode(int mode, bool on)
    {
        switch (mode)
        {
            case 1:
                ApplicationCursorKeys = on;
                break;
            case 6:
                OriginMode = on;
                SetCursor(0, 0);
                break;
            case 7:
                AutoWrap = on;
                if (!on)
                    wrapPending = false;
                break;
            case 9:
                MouseTracking = on ? MouseTracking.Press : MouseTracking.None;
                break;
            case 25:
                CursorVisible = on;
                break;
            case 47:
            case 1047:
                SwitchScreen(on, clear: mode == 1047 && on);
                break;
            case 1048:
                if (on)
                    SaveCursor();
                else
                    RestoreCursor();
                break;
            case 1049:
                if (on)
                {
                    SaveCursor();
                    SwitchScreen(true, clear: true);
                }
                else
                {
                    SwitchScreen(false, clear: false);
                    RestoreCursor();
                }

                break;
            case 1000:
                MouseTracking = on ? MouseTracking.PressAndRelease : MouseTracking.None;
                break;
            case 1002:
                MouseTracking = on ? MouseTracking.Drag : MouseTracking.None;
                break;
            case 1003:
                MouseTracking = on ? MouseTracking.Motion : MouseTracking.None;
                break;
            case 1004:
                ReportsFocus = on;
                break;
            case 1006:
                SgrMouse = on;
                break;
            case 2004:
                BracketedPaste = on;
                break;
        }
    }

    private void SwitchScreen(bool toAlternate, bool clear)
    {
        if (toAlternate == IsAlternateScreen)
        {
            if (toAlternate && clear)
                ClearAlternate();
            return;
        }

        if (toAlternate)
        {
            savedPrimary = savedPrimary with { Row = cursorRow };
            IsAlternateScreen = true;
            if (clear)
                ClearAlternate();
        }
        else
            IsAlternateScreen = false;

        scrollTop = 0;
        scrollBottom = Rows - 1;
        wrapPending = false;
    }

    private void ClearAlternate()
    {
        for (var i = 0; i < Rows; i++)
            alternate[i] = new TerminalLine(Columns, style.Erased);
    }

    private void SaveCursor()
    {
        var saved = new SavedCursor(cursorRow, cursorColumn, style, OriginMode, wrapPending, [.. charsets], activeCharset);
        if (IsAlternateScreen)
            savedAlternate = saved;
        else
            savedPrimary = saved;
    }

    private void RestoreCursor()
    {
        var saved = IsAlternateScreen ? savedAlternate : savedPrimary;
        cursorRow = Math.Clamp(saved.Row, 0, Rows - 1);
        cursorColumn = Math.Clamp(saved.Column, 0, Columns - 1);
        style = saved.Style;
        OriginMode = saved.OriginMode;
        wrapPending = saved.WrapPending;
        charsets = [.. saved.Charsets ?? ['B', 'B']];
        activeCharset = saved.ActiveCharset;
    }

    private void SoftReset()
    {
        InsertMode = false;
        OriginMode = false;
        AutoWrap = true;
        CursorVisible = true;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        scrollTop = 0;
        scrollBottom = Rows - 1;
        style = CellStyle.Default;
        charsets = ['B', 'B'];
        activeCharset = 0;
        savedPrimary = new SavedCursor();
        savedAlternate = new SavedCursor();
        wrapPending = false;
    }

    private void FullReset()
    {
        SoftReset();
        IsAlternateScreen = false;
        primary = NewLines(Rows);
        alternate = NewLines(Rows);
        scrollbackCount = 0;
        scrollbackStart = 0;
        tabStops = DefaultTabStops(Columns);
        cursorRow = 0;
        cursorColumn = 0;
        MouseTracking = MouseTracking.None;
        SgrMouse = false;
        BracketedPaste = false;
        ReportsFocus = false;
        LineFeedNewLine = false;
        CursorShape = CursorShape.Block;
        Title = null;
    }

    private void SelectGraphicRendition(VtParameters p)
    {
        for (var i = 0; i < Math.Max(1, p.Count); i++)
        {
            var code = Math.Max(0, p[i]);
            var flags = style.Flags;
            switch (code)
            {
                case 0:
                    style = CellStyle.Default;
                    continue;
                case 1:
                    flags |= CellFlags.Bold;
                    break;
                case 2:
                    flags |= CellFlags.Dim;
                    break;
                case 3:
                    flags |= CellFlags.Italic;
                    break;
                case 4:
                    var underline = p.IsSub(i + 1) ? p[i + 1] : 1;
                    while (p.IsSub(i + 1))
                        i++;
                    flags &= ~(CellFlags.Underline | CellFlags.DoubleUnderline);
                    if (underline == 2)
                        flags |= CellFlags.DoubleUnderline;
                    else if (underline != 0)
                        flags |= CellFlags.Underline;
                    break;
                case 5 or 6:
                    flags |= CellFlags.Blink;
                    break;
                case 7:
                    flags |= CellFlags.Inverse;
                    break;
                case 8:
                    flags |= CellFlags.Hidden;
                    break;
                case 9:
                    flags |= CellFlags.Strikethrough;
                    break;
                case 21:
                    flags = (flags & ~CellFlags.Underline) | CellFlags.DoubleUnderline;
                    break;
                case 22:
                    flags &= ~(CellFlags.Bold | CellFlags.Dim);
                    break;
                case 23:
                    flags &= ~CellFlags.Italic;
                    break;
                case 24:
                    flags &= ~(CellFlags.Underline | CellFlags.DoubleUnderline);
                    break;
                case 25:
                    flags &= ~CellFlags.Blink;
                    break;
                case 27:
                    flags &= ~CellFlags.Inverse;
                    break;
                case 28:
                    flags &= ~CellFlags.Hidden;
                    break;
                case 29:
                    flags &= ~CellFlags.Strikethrough;
                    break;
                case >= 30 and <= 37:
                    style = style with { Foreground = TerminalColor.Indexed(code - 30) };
                    continue;
                case 38:
                    style = style with { Foreground = ExtendedColor(p, ref i) ?? style.Foreground };
                    continue;
                case 39:
                    style = style with { Foreground = TerminalColor.Default };
                    continue;
                case >= 40 and <= 47:
                    style = style with { Background = TerminalColor.Indexed(code - 40) };
                    continue;
                case 48:
                    style = style with { Background = ExtendedColor(p, ref i) ?? style.Background };
                    continue;
                case 49:
                    style = style with { Background = TerminalColor.Default };
                    continue;
                case 58:
                    ExtendedColor(p, ref i);
                    continue;
                case >= 90 and <= 97:
                    style = style with { Foreground = TerminalColor.Indexed(code - 90 + 8) };
                    continue;
                case >= 100 and <= 107:
                    style = style with { Background = TerminalColor.Indexed(code - 100 + 8) };
                    continue;
            }

            style = style with { Flags = flags };
        }
    }

    // Reads 5;n or 2;r;g;b after 38 or 48, in either the semicolon or the colon form, moving past what it read.
    private static TerminalColor? ExtendedColor(VtParameters p, ref int i)
    {
        if (p.IsSub(i + 1))
        {
            var subs = 0;
            while (p.IsSub(i + 1 + subs))
                subs++;
            var start = i + 1;
            i += subs;
            if (p[start] == 5 && subs >= 2)
                return TerminalColor.Indexed(Math.Max(0, p[start + 1]));
            if (p[start] == 2 && subs >= 4)
            {
                var rgb = subs >= 5 ? start + 2 : start + 1;
                return TerminalColor.Rgb(Math.Max(0, p[rgb]), Math.Max(0, p[rgb + 1]), Math.Max(0, p[rgb + 2]));
            }

            return null;
        }

        if (p[i + 1] == 5 && i + 2 < p.Count)
        {
            i += 2;
            return TerminalColor.Indexed(Math.Max(0, p[i]));
        }

        if (p[i + 1] == 2 && i + 4 < p.Count)
        {
            i += 4;
            return TerminalColor.Rgb(Math.Max(0, p[i - 2]), Math.Max(0, p[i - 1]), Math.Max(0, p[i]));
        }

        i = p.Count;
        return null;
    }

    private TerminalLine[] NewLines(int count)
    {
        var lines = new TerminalLine[count];
        for (var i = 0; i < count; i++)
            lines[i] = new TerminalLine(Columns, CellStyle.Default);
        return lines;
    }

    private static bool[] DefaultTabStops(int columns)
    {
        var stops = new bool[columns];
        for (var i = 8; i < columns; i += 8)
            stops[i] = true;
        return stops;
    }

    private readonly record struct SavedCursor(int Row, int Column, CellStyle Style, bool OriginMode, bool WrapPending, char[]? Charsets, int ActiveCharset)
    {
        public SavedCursor Clamp(int columns, int rows) => this with { Row = Math.Clamp(Row, 0, rows - 1), Column = Math.Clamp(Column, 0, columns - 1) };
    }
}
