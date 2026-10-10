using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using HammerUI;
using HammerUI.Controls;
using Runesmith.Shell.Running;

namespace Runesmith.Shell.Terminal;

/// <summary>Draws a terminal's screen and scrollback on a grid of cells, and turns keys, text, the mouse and the clipboard into the program's
/// input: selection with copy and paste, links to files and web pages, and the mouse reports full-screen programs ask for.</summary>
internal sealed partial class TerminalView : Control
{
    private const double Padding = 6;
    private const int WheelLines = 3;

    private readonly TerminalSession session;
    private readonly Func<KeyEventArgs, bool> isCommand;
    private readonly ConsoleLinkResolver resolver;
    private readonly Action<string, int, int> openFile;
    private readonly Action<string> openUrl;
    private readonly Dictionary<(int CodePoint, bool Bold, bool Italic), (GlyphTypeface Face, ushort Glyph)?> glyphs = [];
    private readonly DispatcherTimer blink = new() { Interval = TimeSpan.FromMilliseconds(530) };
    private readonly Dictionary<Color, IBrush> brushes = [];
    private GlyphTypeface?[] faces = new GlyphTypeface?[4];
    private string fontFamily = Editor.EditorOptions.BundledFontFamily;
    private double fontSize = 13;
    private double cellWidth = 8;
    private double cellHeight = 16;
    private double baseline = 12;
    private TerminalPalette palette;
    private long? topLine;
    private TerminalPoint? selectionStart;
    private TerminalPoint? selectionEnd;
    private SelectionUnit selectionUnit;
    private bool selecting;
    private HoveredLink? hovered;
    private int pendingRender;
    private bool cursorOn = true;
    private bool blinks;
    private int mouseButton = -1;

    public TerminalView(TerminalSession session, TerminalPalette palette, Func<KeyEventArgs, bool> isCommand, ConsoleLinkResolver resolver,
        Action<string, int, int> openFile, Action<string> openUrl)
    {
        this.session = session;
        this.palette = palette;
        this.isCommand = isCommand;
        this.resolver = resolver;
        this.openFile = openFile;
        this.openUrl = openUrl;
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        session.Changed += (_, _) => RequestRender();
        blink.Tick += (_, _) =>
        {
            cursorOn = !cursorOn;
            InvalidateVisual();
        };
        ContextMenu = BuildContextMenu();
        UpdateFont();
    }

    private enum SelectionUnit
    {
        Cell,
        Word,
        Line,
    }

    public TerminalSession Session => session;

    /// <summary>Gets the number of lines in the scrollback and on screen, the first line shown, and the number of rows, for the scroll bar.</summary>
    public (int Total, int First, int Rows) ScrollState
    {
        get
        {
            lock (session.Gate)
                return (session.Screen.TotalLines, FirstIndex(session.Screen), session.Screen.Rows);
        }
    }

    /// <summary>Raised when the lines shown or their number change, for the scroll bar.</summary>
    public event EventHandler? ScrollChanged;

    /// <summary>Changes the font; an empty family or a size of 0 keeps the editor's.</summary>
    public void SetFont(string family, double size)
    {
        fontFamily = string.IsNullOrWhiteSpace(family) || string.Equals(family.Trim(), "JetBrains Mono", StringComparison.OrdinalIgnoreCase)
            ? Editor.EditorOptions.BundledFontFamily
            : $"{family}, {Editor.EditorOptions.BundledFontFamily}";
        fontSize = size > 0 ? size : 13;
        UpdateFont();
    }

    /// <summary>Changes the colors.</summary>
    public void SetPalette(TerminalPalette value)
    {
        palette = value;
        InvalidateVisual();
    }

    /// <summary>Makes the cursor blink, or keeps it steady.</summary>
    public void SetCursorBlink(bool value)
    {
        blinks = value;
        UpdateBlink();
    }

    /// <summary>Shows the line at <paramref name="first"/> at the top, or follows the output when it is the last screenful.</summary>
    public void ScrollTo(int first)
    {
        lock (session.Gate)
        {
            var screen = session.Screen;
            var bottom = screen.ScrollbackCount;
            first = Math.Clamp(first, 0, bottom);
            topLine = first >= bottom ? null : screen.DroppedLines + first;
        }

        InvalidateVisual();
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Copies the selection to the clipboard; returns whether there was one.</summary>
    public bool CopySelection()
    {
        var text = SelectedText();
        if (string.IsNullOrEmpty(text))
            return false;

        _ = TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(text);
        return true;
    }

    /// <summary>Pastes the clipboard's text into the program.</summary>
    public async Task PasteAsync()
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
            return;

        var text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            FollowOutput();
            session.Paste(text);
        }
    }

    /// <summary>Selects the scrollback and the screen.</summary>
    public void SelectAll()
    {
        lock (session.Gate)
        {
            selectionStart = session.Screen.PointAt(0, 0);
            selectionEnd = session.Screen.PointAt(session.Screen.TotalLines - 1, session.Screen.Columns);
        }

        InvalidateVisual();
    }

    /// <summary>Clears the scrollback and the screen but the line the cursor is on.</summary>
    public void Clear()
    {
        lock (session.Gate)
            session.Screen.Clear();
        selectionStart = selectionEnd = null;
        topLine = null;
        RequestRender();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ResizeToFit();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        ReportFocus(true);
        UpdateBlink();
        InvalidateVisual();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        ReportFocus(false);
        UpdateBlink();
        InvalidateVisual();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        blink.Stop();
    }

    public override void Render(DrawingContext context)
    {
        Interlocked.Exchange(ref pendingRender, 0);
        context.FillRectangle(Brush(palette.Background), new Rect(Bounds.Size));
        if (faces[0] is null)
            return;

        lock (session.Gate)
        {
            var screen = session.Screen;
            var first = FirstIndex(screen);
            var rows = VisibleRows(screen);
            var (selectionFrom, selectionTo) = OrderedSelection();
            for (var row = 0; row < rows; row++)
            {
                var index = first + row;
                if (index >= screen.TotalLines)
                    break;

                var line = screen.LineAt(index);
                var top = Padding + (row * cellHeight);
                var point = screen.PointAt(index, 0).Line;
                DrawBackgrounds(context, line, top);
                if (selectionFrom is { } from && selectionTo is { } to && point >= from.Line && point <= to.Line)
                    DrawSelection(context, line, top, point == from.Line ? from.Column : 0, point == to.Line ? to.Column : line.Length);
                DrawText(context, line, top, index == screen.ScrollbackCount + screen.CursorRow && ShowsCursor(screen) ? screen.CursorColumn : -1);
                if (hovered is { } link && point >= link.Start.Line && point <= link.End.Line)
                    DrawLinkUnderline(context, link.Start.Line == point ? link.Start.Column : 0, link.End.Line == point ? link.End.Column : line.Length, top);
            }

            if (ShowsCursor(screen))
            {
                var cursorRow = screen.ScrollbackCount + screen.CursorRow - first;
                if (cursorRow >= 0 && cursorRow < rows)
                    DrawCursor(context, screen, cursorRow);
            }
        }
    }

    private bool ShowsCursor(TerminalScreen screen) => screen.CursorVisible && !session.HasExited && (cursorOn || !IsFocused);

    private void DrawBackgrounds(DrawingContext context, TerminalLine line, double top)
    {
        var cells = line.Cells;
        var start = 0;
        while (start < cells.Length)
        {
            var color = BackgroundOf(cells[start].Style);
            var end = start + 1;
            while (end < cells.Length && BackgroundOf(cells[end].Style) == color)
                end++;
            if (color != palette.Background)
                context.FillRectangle(Brush(color), new Rect(Padding + (start * cellWidth), top, (end - start) * cellWidth, cellHeight));
            start = end;
        }
    }

    private void DrawSelection(DrawingContext context, TerminalLine line, double top, int from, int to)
    {
        to = Math.Min(to, line.Length);
        if (to <= from)
            return;

        context.FillRectangle(Brush(palette.Selection), new Rect(Padding + (from * cellWidth), top, (to - from) * cellWidth, cellHeight));
    }

    // Glyphs of one face and color in a row are drawn as one run, each advancing by its cells, so the text stays on the grid.
    private void DrawText(DrawingContext context, TerminalLine line, double top, int cursorColumn)
    {
        var cells = line.Cells;
        var y = top + baseline;
        var glyphs = new List<GlyphInfo>();
        var characters = new System.Text.StringBuilder();
        GlyphTypeface? runFace = null;
        var runColor = default(Color);
        var runStart = 0;
        var runEnd = 0;

        void Flush()
        {
            if (runFace is not null && glyphs.Count > 0)
            {
                var run = new GlyphRun(runFace, fontSize, characters.ToString().AsMemory(), [.. glyphs], new Point(Padding + (runStart * cellWidth), y));
                context.DrawGlyphRun(Brush(runColor), run);
            }

            glyphs.Clear();
            characters.Clear();
            runFace = null;
        }

        for (var column = 0; column < cells.Length; column++)
        {
            var cell = cells[column];
            DrawDecorations(context, cell, column, top);
            if (cell.Width == 0 || cell.Rune is 0 or ' ' || cell.Style.Flags.HasFlag(CellFlags.Hidden))
            {
                if (cell.Width != 0)
                    Flush();
                continue;
            }

            var foreground = column == cursorColumn && IsFocused && session.Screen.CursorShape == CursorShape.Block ? palette.Background : ForegroundOf(cell.Style);
            var bold = cell.Style.Flags.HasFlag(CellFlags.Bold);
            var italic = cell.Style.Flags.HasFlag(CellFlags.Italic);
            var width = cell.Width * cellWidth;
            if (cell.Combining is null && Glyph(cell.Rune, bold, italic) is { } found)
            {
                if (runFace != found.Face || runColor != foreground || runEnd != column)
                {
                    Flush();
                    runFace = found.Face;
                    runColor = foreground;
                    runStart = column;
                }

                glyphs.Add(new GlyphInfo(found.Glyph, characters.Length, width, default));
                runEnd = column + cell.Width;
                characters.Append(char.ConvertFromUtf32(cell.Rune));
                continue;
            }

            Flush();
            var text = new TextLayout(cell.Text, Typeface(bold, italic), fontSize, Brush(foreground));
            text.Draw(context, new Point(Padding + (column * cellWidth) + Math.Max(0, (width - text.Width) / 2), top + Math.Max(0, (cellHeight - text.Height) / 2)));
        }

        Flush();
    }

    private IBrush Brush(Color color)
    {
        if (brushes.Count > 4096)
            brushes.Clear();
        if (!brushes.TryGetValue(color, out var brush))
            brushes[color] = brush = new Avalonia.Media.Immutable.ImmutableSolidColorBrush(color);
        return brush;
    }

    private void DrawDecorations(DrawingContext context, TerminalCell cell, int column, double top)
    {
        var flags = cell.Style.Flags;
        if ((flags & (CellFlags.Underline | CellFlags.DoubleUnderline | CellFlags.Strikethrough)) == 0 || cell.Width == 0)
            return;

        var pen = new Pen(Brush(ForegroundOf(cell.Style)), 1);
        var left = Padding + (column * cellWidth);
        var right = left + (cell.Width * cellWidth);
        if (flags.HasFlag(CellFlags.Underline) || flags.HasFlag(CellFlags.DoubleUnderline))
        {
            var y = Math.Round(top + baseline + 2) + 0.5;
            context.DrawLine(pen, new Point(left, y), new Point(right, y));
            if (flags.HasFlag(CellFlags.DoubleUnderline))
                context.DrawLine(pen, new Point(left, y + 2), new Point(right, y + 2));
        }

        if (flags.HasFlag(CellFlags.Strikethrough))
        {
            var y = Math.Round(top + (cellHeight / 2)) + 0.5;
            context.DrawLine(pen, new Point(left, y), new Point(right, y));
        }
    }

    private void DrawLinkUnderline(DrawingContext context, int from, int to, double top)
    {
        var y = Math.Round(top + baseline + 2) + 0.5;
        context.DrawLine(new Pen(Brush(palette.Link), 1), new Point(Padding + (from * cellWidth), y), new Point(Padding + (to * cellWidth), y));
    }

    private void DrawCursor(DrawingContext context, TerminalScreen screen, int row)
    {
        var column = Math.Min(screen.CursorColumn, screen.Columns - 1);
        var width = screen.Row(screen.CursorRow).Cells[column].Width == 2 ? 2 * cellWidth : cellWidth;
        var rect = new Rect(Padding + (column * cellWidth), Padding + (row * cellHeight), width, cellHeight);
        var brush = Brush(palette.Cursor);
        if (!IsFocused)
        {
            context.DrawRectangle(new Pen(brush, 1), rect.Deflate(0.5));
            return;
        }

        switch (screen.CursorShape)
        {
            case CursorShape.Bar:
                context.FillRectangle(brush, rect.WithWidth(2));
                break;
            case CursorShape.Underline:
                context.FillRectangle(brush, new Rect(rect.Left, rect.Bottom - 2, rect.Width, 2));
                break;
            default:
                context.FillRectangle(brush, rect);
                var cell = screen.Row(screen.CursorRow).Cells[column];
                if (cell.Rune is not (0 or ' ') && Glyph(cell.Rune, cell.Style.Flags.HasFlag(CellFlags.Bold), cell.Style.Flags.HasFlag(CellFlags.Italic)) is { } found)
                {
                    var run = new GlyphRun(found.Face, fontSize, char.ConvertFromUtf32(cell.Rune).AsMemory(), [new GlyphInfo(found.Glyph, 0, width, default)],
                        new Point(rect.Left, rect.Top + baseline));
                    context.DrawGlyphRun(Brush(palette.Background), run);
                }

                break;
        }
    }

    private Color ForegroundOf(CellStyle style)
    {
        var inverse = style.Flags.HasFlag(CellFlags.Inverse);
        var color = inverse ? palette.Resolve(style.Background, background: true) : palette.Resolve(style.Foreground, background: false);
        if (style.Flags.HasFlag(CellFlags.Bold) && !inverse && style.Foreground.IsIndexed && style.Foreground.Index < 8)
            color = palette[style.Foreground.Index + 8];
        return style.Flags.HasFlag(CellFlags.Dim) ? Color.FromArgb(0x99, color.R, color.G, color.B) : color;
    }

    private Color BackgroundOf(CellStyle style) =>
        style.Flags.HasFlag(CellFlags.Inverse) ? palette.Resolve(style.Foreground, background: false) : palette.Resolve(style.Background, background: true);

    private Typeface Typeface(bool bold, bool italic) =>
        new(new FontFamily(fontFamily), italic ? FontStyle.Italic : FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal);

    private (GlyphTypeface Face, ushort Glyph)? Glyph(int codePoint, bool bold, bool italic)
    {
        var key = (codePoint, bold, italic);
        if (glyphs.TryGetValue(key, out var cached))
            return cached;

        var face = faces[(bold ? 1 : 0) + (italic ? 2 : 0)] ?? faces[0];
        (GlyphTypeface, ushort)? found = null;
        if (face is not null && face.CharacterToGlyphMap.TryGetGlyph(codePoint, out var glyph) && glyph != 0)
            found = (face, glyph);
        else if (FontManager.Current.TryMatchCharacter(codePoint, italic ? FontStyle.Italic : FontStyle.Normal, bold ? FontWeight.Bold : FontWeight.Normal,
                     FontStretch.Normal, null, CultureInfo.CurrentUICulture, out var fallback)
                 && fallback.GlyphTypeface.CharacterToGlyphMap.TryGetGlyph(codePoint, out var fallbackGlyph) && fallbackGlyph != 0)
            found = (fallback.GlyphTypeface, fallbackGlyph);

        glyphs[key] = found;
        return found;
    }

    private void UpdateFont()
    {
        glyphs.Clear();
        faces = new GlyphTypeface?[4];
        for (var i = 0; i < 4; i++)
        {
            try
            {
                faces[i] = Typeface(bold: (i & 1) != 0, italic: (i & 2) != 0).GlyphTypeface;
            }
            catch (InvalidOperationException)
            {
                faces[i] = null;
            }
        }

        faces[0] ??= new Typeface(new FontFamily(Editor.EditorOptions.BundledFontFamily)).GlyphTypeface;
        var metrics = faces[0]!.Metrics;
        var scale = fontSize / metrics.DesignEmHeight;
        var ascent = -metrics.Ascent * scale;
        var descent = metrics.Descent * scale;
        cellHeight = Math.Ceiling(ascent + descent + (metrics.LineGap * scale));
        baseline = Math.Round(((cellHeight - ascent - descent) / 2) + ascent);
        using (var probe = new TextLayout(new string('M', 20), Typeface(bold: false, italic: false), fontSize, null))
            cellWidth = probe.WidthIncludingTrailingWhitespace > 0 ? probe.WidthIncludingTrailingWhitespace / 20 : fontSize * 0.6;
        ResizeToFit();
        InvalidateVisual();
    }

    private void ResizeToFit()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var columns = Math.Max(2, (int)Math.Floor((Bounds.Width - (2 * Padding)) / cellWidth));
        var rows = Math.Max(1, (int)Math.Floor((Bounds.Height - (2 * Padding)) / cellHeight));
        session.Resize(columns, rows);
        ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    private int VisibleRows(TerminalScreen screen) => Math.Min(screen.Rows, Math.Max(1, (int)Math.Ceiling((Bounds.Height - Padding) / cellHeight)));

    private int FirstIndex(TerminalScreen screen)
    {
        var bottom = screen.ScrollbackCount;
        if (topLine is not { } line)
            return bottom;

        var index = (int)Math.Clamp(line - screen.DroppedLines, 0, bottom);
        if (index >= bottom)
            topLine = null;
        return index;
    }

    private void FollowOutput()
    {
        if (topLine is null)
            return;

        topLine = null;
        ScrollChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private void RequestRender()
    {
        if (Interlocked.Exchange(ref pendingRender, 1) == 0)
            Dispatcher.UIThread.Post(() =>
            {
                InvalidateVisual();
                ScrollChanged?.Invoke(this, EventArgs.Empty);
            }, DispatcherPriority.Render);
    }

    private void UpdateBlink()
    {
        cursorOn = true;
        if (blinks && IsFocused)
            blink.Start();
        else
            blink.Stop();
    }

    private void ReportFocus(bool focused)
    {
        bool reports;
        lock (session.Gate)
            reports = session.Screen.ReportsFocus;
        if (reports)
            session.Write(focused ? "\u001b[I" : "\u001b[O");
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl && hovered is not null)
            Cursor = new Cursor(StandardCursorType.Hand);
        if (e.Handled)
            return;

        var modifiers = e.KeyModifiers & ~KeyModifiers.Meta;
        var control = modifiers.HasFlag(KeyModifiers.Control);
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        if (control && e.Key == Key.C && (shift || HasSelection))
        {
            CopySelection();
            if (!shift)
                ClearSelection();
            e.Handled = true;
            return;
        }

        if ((control && shift && e.Key == Key.V) || (shift && !control && e.Key == Key.Insert))
        {
            e.Handled = true;
            _ = PasteAsync();
            return;
        }

        if (shift && !control && e.Key is Key.PageUp or Key.PageDown && !IsAlternate())
        {
            e.Handled = true;
            var (_, first, rows) = ScrollState;
            ScrollTo(first + (e.Key == Key.PageUp ? -rows + 1 : rows - 1));
            return;
        }

        if (!TerminalKeys.BelongsToTerminal(e.Key, e.KeyModifiers) && isCommand(e))
            return;

        bool applicationCursor;
        lock (session.Gate)
            applicationCursor = session.Screen.ApplicationCursorKeys;
        if (TerminalKeys.Encode(e.Key, modifiers, e.KeySymbol, applicationCursor) is { } sequence)
        {
            e.Handled = true;
            FollowOutput();
            ClearSelection();
            session.Write(sequence);
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.LeftCtrl or Key.RightCtrl)
            Cursor = new Cursor(StandardCursorType.Ibeam);
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrEmpty(e.Text))
            return;

        e.Handled = true;
        FollowOutput();
        ClearSelection();
        session.Write(e.Text);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetCurrentPoint(this);
        var (row, column) = CellAt(point.Position);
        if (point.Properties.IsRightButtonPressed && !Tracks(e.KeyModifiers))
            return;

        if (Tracks(e.KeyModifiers))
        {
            mouseButton = point.Properties.IsLeftButtonPressed ? 0 : point.Properties.IsMiddleButtonPressed ? 1 : 2;
            SendMouse(mouseButton, row, column, e.KeyModifiers, press: true);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && hovered is { } link)
        {
            OpenLink(link);
            e.Handled = true;
            return;
        }

        var at = PointAt(row, column);
        selectionUnit = e.ClickCount switch
        {
            2 => SelectionUnit.Word,
            >= 3 => SelectionUnit.Line,
            _ => SelectionUnit.Cell,
        };
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && selectionStart is not null && selectionUnit == SelectionUnit.Cell)
            selectionEnd = at;
        else
        {
            selectionStart = selectionEnd = at;
            if (selectionUnit != SelectionUnit.Cell)
                ExpandSelection(at);
        }

        selecting = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        var (row, column) = CellAt(position);
        if (mouseButton >= 0 && Tracking() is MouseTracking.Drag or MouseTracking.Motion)
        {
            SendMouse(mouseButton + 32, row, column, e.KeyModifiers, press: true);
            return;
        }

        if (selecting)
        {
            if (position.Y < 0)
                ScrollTo(ScrollState.First - 1);
            else if (position.Y > Bounds.Height)
                ScrollTo(ScrollState.First + 1);
            var at = PointAt(row, column);
            if (selectionUnit == SelectionUnit.Cell)
                selectionEnd = at;
            else
                ExpandSelection(at, keepStart: true);
            InvalidateVisual();
            return;
        }

        UpdateHover(row, column, e.KeyModifiers);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (mouseButton >= 0)
        {
            var (row, column) = CellAt(e.GetPosition(this));
            if (Tracking() != MouseTracking.Press)
                SendMouse(mouseButton, row, column, e.KeyModifiers, press: false);
            mouseButton = -1;
            return;
        }

        if (!selecting)
            return;

        selecting = false;
        e.Pointer.Capture(null);
        if (selectionStart == selectionEnd)
            ClearSelection();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (hovered is not null)
        {
            hovered = null;
            ToolTip.SetTip(this, null);
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        e.Handled = true;
        var lines = (int)Math.Round(-e.Delta.Y * WheelLines);
        if (lines == 0)
            return;

        var (row, column) = CellAt(e.GetPosition(this));
        if (Tracks(e.KeyModifiers))
        {
            for (var i = 0; i < Math.Abs(lines) / WheelLines + 1; i++)
                SendMouse(lines < 0 ? 64 : 65, row, column, e.KeyModifiers, press: true);
            return;
        }

        if (IsAlternate())
        {
            bool application;
            lock (session.Gate)
                application = session.Screen.ApplicationCursorKeys;
            var key = lines < 0 ? (application ? "\u001bOA" : "\u001b[A") : (application ? "\u001bOB" : "\u001b[B");
            session.Write(string.Concat(Enumerable.Repeat(key, Math.Abs(lines))));
            return;
        }

        ScrollTo(ScrollState.First + lines);
    }

    private bool HasSelection => selectionStart is { } start && selectionEnd is { } end && start != end;

    private bool IsAlternate()
    {
        lock (session.Gate)
            return session.Screen.IsAlternateScreen;
    }

    private MouseTracking Tracking()
    {
        lock (session.Gate)
            return session.Screen.MouseTracking;
    }

    // Shift lets the user select text while the program reports the mouse.
    private bool Tracks(KeyModifiers modifiers) => Tracking() != MouseTracking.None && !modifiers.HasFlag(KeyModifiers.Shift);

    private void SendMouse(int button, int row, int column, KeyModifiers modifiers, bool press)
    {
        bool sgr;
        lock (session.Gate)
            sgr = session.Screen.SgrMouse;
        var code = button + (modifiers.HasFlag(KeyModifiers.Shift) ? 4 : 0) + (modifiers.HasFlag(KeyModifiers.Alt) ? 8 : 0) + (modifiers.HasFlag(KeyModifiers.Control) ? 16 : 0);
        if (sgr)
            session.Write(string.Create(CultureInfo.InvariantCulture, $"\u001b[<{code};{column + 1};{row + 1}{(press ? 'M' : 'm')}"));
        else if (column < 223 && row < 223)
            session.Write($"\u001b[M{(char)(32 + (press ? code : 3))}{(char)(33 + column)}{(char)(33 + row)}");
    }

    private (int Row, int Column) CellAt(Point position)
    {
        lock (session.Gate)
        {
            var row = Math.Clamp((int)Math.Floor((position.Y - Padding) / cellHeight), 0, session.Screen.Rows - 1);
            var column = Math.Clamp((int)Math.Round((position.X - Padding) / cellWidth), 0, session.Screen.Columns);
            return (row, column);
        }
    }

    private TerminalPoint PointAt(int row, int column)
    {
        lock (session.Gate)
            return session.Screen.PointAt(FirstIndex(session.Screen) + row, column);
    }

    private (TerminalPoint? From, TerminalPoint? To) OrderedSelection() =>
        selectionStart is { } start && selectionEnd is { } end && start != end ? (start <= end ? (start, end) : (end, start)) : (null, null);

    private string? SelectedText()
    {
        if (OrderedSelection() is not ({ } from, { } to))
            return null;

        lock (session.Gate)
            return session.Screen.GetText(from, to);
    }

    private void ClearSelection()
    {
        if (selectionStart is null)
            return;

        selectionStart = selectionEnd = null;
        InvalidateVisual();
    }

    // Grows the selection to whole words or lines around the point, keeping the start's word or line when dragging.
    private void ExpandSelection(TerminalPoint at, bool keepStart = false)
    {
        lock (session.Gate)
        {
            var screen = session.Screen;
            var index = screen.IndexOf(at);
            if (index < 0 || index >= screen.TotalLines)
                return;

            var line = screen.LineAt((int)index);
            var (from, to) = selectionUnit == SelectionUnit.Line ? (0, line.Length) : WordAround(line, Math.Min(at.Column, line.Length - 1));
            var start = new TerminalPoint(at.Line, from);
            var end = new TerminalPoint(at.Line, to);
            if (!keepStart || selectionStart is not { } anchor)
            {
                selectionStart = start;
                selectionEnd = end;
            }
            else if (start < anchor)
                selectionEnd = start;
            else
                selectionEnd = end;
        }
    }

    private static (int From, int To) WordAround(TerminalLine line, int column)
    {
        static bool IsWord(TerminalCell cell) => cell.Rune != 0 && (char.IsLetterOrDigit(char.ConvertFromUtf32(cell.Rune), 0) || "_-./~:@%+".Contains((char)cell.Rune, StringComparison.Ordinal));

        if (column < 0 || !IsWord(line.Cells[column]))
            return (Math.Max(0, column), Math.Max(0, column) + 1);

        var from = column;
        while (from > 0 && IsWord(line.Cells[from - 1]))
            from--;
        var to = column;
        while (to < line.Length - 1 && IsWord(line.Cells[to + 1]))
            to++;
        return (from, to + 1);
    }

    private void UpdateHover(int row, int column, KeyModifiers modifiers)
    {
        HoveredLink? found = null;
        lock (session.Gate)
        {
            var screen = session.Screen;
            var index = FirstIndex(screen) + row;
            if (index < screen.TotalLines)
                found = LinkAt(screen, index, column);
        }

        if (found == hovered)
            return;

        hovered = found;
        Cursor = new Cursor(found is not null && modifiers.HasFlag(KeyModifiers.Control) ? StandardCursorType.Hand : StandardCursorType.Ibeam);
        ToolTip.SetTip(this, found is null ? null : "Ctrl+click to open");
        InvalidateVisual();
    }

    // Finds the link at a cell in the line's text, joined with the lines it wrapped from and into, so a long path or address is one link.
    private HoveredLink? LinkAt(TerminalScreen screen, int index, int column)
    {
        var first = index;
        while (first > 0 && screen.LineAt(first - 1).IsWrapped)
            first--;
        var last = index;
        while (last < screen.TotalLines - 1 && screen.LineAt(last).IsWrapped)
            last++;

        var text = new System.Text.StringBuilder();
        var places = new List<(int Line, int Column)>();
        for (var i = first; i <= last; i++)
        {
            var cells = screen.LineAt(i).Cells;
            for (var c = 0; c < cells.Length; c++)
            {
                foreach (var character in cells[c].Text)
                {
                    text.Append(character);
                    places.Add((i, c));
                }
            }
        }

        foreach (var (start, length, url, file) in FindLinks(text.ToString()))
        {
            if (length <= 0 || start + length > places.Count)
                continue;

            var from = places[start];
            var to = places[start + length - 1];
            if ((index, column).CompareTo(from) < 0 || (index, column).CompareTo(to) > 0)
                continue;

            return new HoveredLink(screen.PointAt(from.Line, from.Column), screen.PointAt(to.Line, to.Column + 1), url, file);
        }

        return null;
    }

    private IEnumerable<(int Start, int Length, string? Url, ConsoleLink? File)> FindLinks(string text)
    {
        foreach (Match match in UrlPattern().Matches(text))
        {
            var url = match.Value.TrimEnd('.', ',', ')', ';', ':', '"', '\'');
            yield return (match.Index, url.Length, url, null);
        }

        foreach (var link in ConsoleLinks.Find(text))
        {
            if (resolver.Resolve(link) is not null)
                yield return (link.Start, link.Length, null, link);
        }
    }

    private void OpenLink(HoveredLink link)
    {
        if (link.Url is { } url)
            openUrl(url);
        else if (link.File is { } file && resolver.Resolve(file) is { } path)
            openFile(path, file.Line, file.Column);
    }

    private ContextMenu BuildContextMenu()
    {
        MenuItem Item(string header, Geometry? icon, string? gesture, Action action)
        {
            var item = new MenuItem { Header = header, Icon = icon is null ? null : new SymbolIcon { Data = icon, Size = 14 }, InputGesture = gesture is null ? null : KeyGesture.Parse(gesture) };
            item.Click += (_, _) => action();
            return item;
        }

        var copy = Item("Copy", Icons.Copy, "Ctrl+Shift+C", () => CopySelection());
        var menu = new ContextMenu
        {
            Items =
            {
                copy,
                Item("Paste", Icons.Paste, "Ctrl+Shift+V", () => _ = PasteAsync()),
                Item("Select All", null, null, SelectAll),
                new Separator(),
                Item("Clear", Icons.Eraser, null, Clear),
            },
        };
        menu.Opening += (_, _) => copy.IsEnabled = HasSelection;
        return menu;
    }

    [GeneratedRegex(@"\bhttps?://[^\s<>""'`]+", RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();

    private sealed record HoveredLink(TerminalPoint Start, TerminalPoint End, string? Url, ConsoleLink? File);
}
