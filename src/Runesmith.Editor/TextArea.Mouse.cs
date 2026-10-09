using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Runesmith.Text;

namespace Runesmith.Editor;

public sealed partial class TextArea
{
    private enum DragUnit
    {
        None,
        Character,
        Word,
        Line,
    }

    private DragUnit dragUnit;
    private TextSpan dragOrigin;
    private Point lastPointer;
    private DispatcherTimer? autoScrollTimer;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsRightButtonPressed)
        {
            // The context menu acts where the user clicked, unless the click is in the selection.
            Focus();
            var clicked = GetOffsetFromPoint(point.Position, allowGutter: true) ?? 0;
            if (clicked < selection.Start || clicked > selection.End)
                Select(EditorSelection.At(clicked), scrollIntoView: false);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
            return;

        if (ClickDecoration(point.Position))
        {
            e.Handled = true;
            return;
        }

        if (ChangeMarkerAt(point.Position) is { } change)
        {
            LineChangeClicked?.Invoke(this, change);
            e.Handled = true;
            return;
        }

        Focus();
        BreakUndoGroup();
        var position = point.Position;
        var offset = GetOffsetFromPoint(position, allowGutter: true) ?? 0;
        var command = (e.KeyModifiers & Hotkeys.CommandModifiers) != 0;
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (command && !shift && position.X >= GutterWidth && DefinitionRequested is not null)
        {
            Select(EditorSelection.At(offset));
            DefinitionRequested(this, offset);
            e.Handled = true;
            return;
        }

        if (position.X < GutterWidth)
        {
            dragUnit = DragUnit.Line;
            dragOrigin = LineSpan(offset);
            Select(shift ? Extend(dragOrigin) : new EditorSelection(dragOrigin.Start, dragOrigin.End));
        }
        else if (e.ClickCount >= 3)
        {
            dragUnit = DragUnit.Line;
            dragOrigin = LineSpan(offset);
            Select(new EditorSelection(dragOrigin.Start, dragOrigin.End));
        }
        else if (e.ClickCount == 2)
        {
            dragUnit = DragUnit.Word;
            dragOrigin = WordBoundaries.GetWordAt(Snapshot, offset);
            Select(new EditorSelection(dragOrigin.Start, dragOrigin.End));
        }
        else
        {
            dragUnit = DragUnit.Character;
            dragOrigin = new TextSpan(shift ? selection.Anchor : offset, 0);
            Select(shift ? selection with { Caret = offset } : EditorSelection.At(offset));
        }

        lastPointer = position;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        Cursor = new Cursor(ChangeMarkerAt(position) is not null || IsOverClickableDecoration(position) ? StandardCursorType.Hand : position.X < GutterWidth ? StandardCursorType.Arrow : Link is null ? StandardCursorType.Ibeam : StandardCursorType.Hand);
        if (dragUnit == DragUnit.None)
            return;

        lastPointer = position;
        DragTo(position);
        var outside = position.Y < 0 || position.Y > Bounds.Height;
        if (outside && autoScrollTimer is null)
        {
            autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            autoScrollTimer.Tick += (_, _) => AutoScroll();
            autoScrollTimer.Start();
        }
        else if (!outside)
        {
            StopAutoScroll();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        EndDrag(e.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        dragUnit = DragUnit.None;
        StopAutoScroll();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if ((e.KeyModifiers & Hotkeys.CommandModifiers) != 0)
        {
            if (e.Delta.Y != 0)
                ZoomRequested?.Invoke(this, Math.Sign(e.Delta.Y));
            e.Handled = true;
            return;
        }

        var lines = 3 * LineHeight;
        var delta = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? new Vector(e.Delta.Y, 0) : e.Delta;
        ScrollOffset -= new Vector(delta.X * lines, delta.Y * lines);
        e.Handled = true;
    }

    private void EndDrag(IPointer pointer)
    {
        dragUnit = DragUnit.None;
        StopAutoScroll();
        if (pointer.Captured == this)
            pointer.Capture(null);
    }

    private void DragTo(Point position)
    {
        var offset = GetOffsetFromPoint(new Point(Math.Max(position.X, GutterWidth), position.Y), allowGutter: true) ?? 0;
        var span = dragUnit switch
        {
            DragUnit.Word => WordBoundaries.GetWordAt(Snapshot, offset),
            DragUnit.Line => LineSpan(offset),
            _ => new TextSpan(offset, 0),
        };
        Select(Extend(span));
    }

    // Extends from the span the drag started on to the span under the pointer, keeping the start span selected either way.
    private EditorSelection Extend(TextSpan span) =>
        span.Start < dragOrigin.Start
            ? new EditorSelection(dragOrigin.End, span.Start)
            : new EditorSelection(dragOrigin.Start, Math.Max(span.End, dragOrigin.End));

    private TextSpan LineSpan(int offset)
    {
        var line = Snapshot.GetLineFromPosition(offset);
        return TextSpan.FromBounds(line.Start, line.EndIncludingBreak);
    }

    private void AutoScroll()
    {
        if (dragUnit == DragUnit.None)
        {
            StopAutoScroll();
            return;
        }

        var distance = lastPointer.Y < 0 ? lastPointer.Y : lastPointer.Y - Bounds.Height;
        ScrollOffset += new Vector(0, Math.Clamp(distance, -LineHeight * 4, LineHeight * 4));
        DragTo(lastPointer);
    }

    private void StopAutoScroll()
    {
        autoScrollTimer?.Stop();
        autoScrollTimer = null;
    }
}
