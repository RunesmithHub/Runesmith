using Avalonia.Threading;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Navigation;

/// <summary>Highlights the occurrences of the symbol under the caret a moment after the caret stops, through the decoration layer: reads and
/// plain uses in a neutral tint, writes in the information tint.</summary>
internal sealed class OccurrenceController : IDisposable
{
    /// <summary>How long the caret rests before the occurrences are asked for.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(250);

    private readonly TextArea area;
    private readonly INavigationFeatures features;
    private readonly DispatcherTimer timer;
    private CancellationTokenSource? request;
    private bool isEnabled = true;
    private bool disposed;

    public OccurrenceController(TextArea area, INavigationFeatures features)
    {
        this.area = area;
        this.features = features;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = Delay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = RefreshAsync();
        };
        area.SelectionChanged += OnSelectionChanged;
    }

    /// <summary>Gets or sets whether occurrences are highlighted; turning it off clears them.</summary>
    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            isEnabled = value;
            if (!value)
                Clear();
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        timer.Stop();
        Cancel();
        area.SelectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (!isEnabled || disposed)
            return;

        var selection = area.Selection;
        if (!selection.IsEmpty || !IsOnHighlight(selection.Caret))
            Clear();
        if (!selection.IsEmpty)
            return;

        timer.Stop();
        timer.Start();
    }

    private bool IsOnHighlight(int offset) => area.Decorations.Of(this).Any(p => p.Span.Start <= offset && offset <= p.Span.End);

    private async Task RefreshAsync()
    {
        Cancel();
        var selection = area.Selection;
        if (!isEnabled || disposed || !selection.IsEmpty)
            return;

        var snapshot = area.Snapshot;
        var caret = selection.Caret;
        if (WordBoundaries.GetWordAt(snapshot, caret).IsEmpty)
        {
            Clear();
            return;
        }

        request = new CancellationTokenSource();
        var token = request.Token;
        IReadOnlyList<DocumentHighlight> highlights;
        try
        {
            highlights = await Task.Run(() => features.GetDocumentHighlightsAsync(area.Document, snapshot, caret, token), token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or LanguageFeatureException)
        {
            return;
        }

        if (token.IsCancellationRequested || disposed || !ReferenceEquals(snapshot, area.Snapshot) || area.Selection != selection)
            return;

        area.SetDecorations(this, [.. highlights.Where(h => h.Span.End <= snapshot.Length).Select(ToDecoration)]);
    }

    private static TextHighlight ToDecoration(DocumentHighlight highlight) => new(highlight.Span)
    {
        Underline = UnderlineStyle.None,
        Background = true,
        Tone = highlight.Kind == DocumentHighlightKind.Write ? DecorationTone.Information : DecorationTone.Neutral,
        ShowsInOverviewRuler = true,
    };

    private void Clear()
    {
        Cancel();
        if (area.Decorations.Of(this).Count > 0)
            area.ClearDecorations(this);
    }

    private void Cancel()
    {
        request?.Cancel();
        request?.Dispose();
        request = null;
    }
}
