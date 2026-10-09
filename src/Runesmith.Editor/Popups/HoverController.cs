using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Threading;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Popups;

/// <summary>Shows what the language knows about the symbol under the pointer, and the problems there, after the pointer rests on it.</summary>
internal sealed class HoverController : IDisposable
{
    private readonly TextArea area;
    private readonly ILanguageFeatures features;
    private readonly EditorPopup popup;
    private readonly DispatcherTimer restTimer;
    private CancellationTokenSource? request;
    private Point pointer;
    private TextSpan? shownSpan;
    private Rect? shownGlyph;

    public HoverController(TextArea area, ILanguageFeatures features)
    {
        this.area = area;
        this.features = features;
        popup = new EditorPopup(area, above: false);
        restTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        restTimer.Tick += (_, _) =>
        {
            restTimer.Stop();
            _ = ShowAsync(pointer, Cancellation.Renew(ref request));
        };
        popup.PointerExited += (_, _) => Hide();
    }

    public EditorPopup Popup => popup;

    public void OnPointerMoved(Point position)
    {
        pointer = position;
        if (area.GetGlyphToolTip(position) is var (toolTip, bounds))
        {
            restTimer.Stop();
            if (shownGlyph != bounds)
            {
                Cancellation.Cancel(ref request);
                shownSpan = null;
                shownGlyph = bounds;
                popup.Show(MarkdownView.Create(toolTip), bounds);
            }

            return;
        }

        if (shownGlyph is not null)
            Hide();

        if (shownSpan is { } span && area.GetOffsetFromPoint(position) is { } offset && offset >= span.Start && offset <= span.End)
            return;

        if (popup.IsOpen && !popup.IsPointerOverPopup)
            Hide();

        restTimer.Stop();
        restTimer.Start();
    }

    public void Hide()
    {
        Cancellation.Cancel(ref request);
        restTimer.Stop();
        shownSpan = null;
        shownGlyph = null;
        popup.Hide();
    }

    public void Dispose() => Hide();

    private async Task ShowAsync(Point position, CancellationToken token)
    {
        if (area.GetOffsetFromPoint(position) is not { } offset || !area.IsOverText(position, offset))
            return;

        var snapshot = area.Snapshot;
        var problems = area.Diagnostics.Where(d => d.Span.Start <= offset && offset <= Math.Max(d.Span.End, d.Span.Start + 1)).ToList();
        HoverInfo? hover = null;
        try
        {
            hover = await features.GetHoverAsync(area.Document, snapshot, offset, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var notes = area.GetDecorationToolTips(offset);
        if (token.IsCancellationRequested || hover is null && problems.Count == 0 && notes.Count == 0)
            return;

        var panel = new StackPanel { Spacing = 8 };
        foreach (var problem in problems)
            panel.Children.Add(Problem(problem));
        foreach (var note in notes)
            panel.Children.Add(MarkdownView.Create(note));
        if (problems.Count + notes.Count > 0 && hover is not null)
        {
            var line = new Border { Height = 1 };
            line[!Border.BackgroundProperty] = new DynamicResourceExtension("BorderSubtleBrush");
            panel.Children.Add(line);
        }

        if (hover is not null)
            panel.Children.Add(MarkdownView.Create(hover.Markdown));

        var span = hover?.Span ?? WordBoundaries.GetWordAt(snapshot, offset);
        if (span.IsEmpty)
            span = new TextSpan(offset, 0);
        shownSpan = span;
        popup.Show(panel, area.GetCharacterRect(span.Start));
    }

    private static DockPanel Problem(DiagnosticMarker marker)
    {
        var (icon, brush) = marker.Severity switch
        {
            DiagnosticSeverity.Error => (HammerUI.Icons.AlertCircle, "DangerBrush"),
            DiagnosticSeverity.Warning => (HammerUI.Icons.AlertTriangle, "WarningBrush"),
            _ => (HammerUI.Icons.Info, "InfoBrush"),
        };
        var symbol = new HammerUI.Controls.SymbolIcon { Data = icon, Size = 14, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        symbol[!HammerUI.Controls.SymbolIcon.ForegroundProperty] = new DynamicResourceExtension(brush);
        var source = marker.Code is null ? marker.Source : $"{marker.Source} {marker.Code}";
        var text = new SelectableTextBlock { Text = $"{marker.Message}  ({source})", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
        var row = new DockPanel();
        DockPanel.SetDock(symbol, Dock.Left);
        row.Children.Add(symbol);
        row.Children.Add(text);
        return row;
    }
}
