using System.ComponentModel;
using Avalonia.Threading;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Folding;

/// <summary>Keeps an editor's folding regions up to date: it asks the folding providers when the document opens, a moment after typing
/// stops, and when a provider says its ranges changed, and folds by indentation when no provider answers.</summary>
internal sealed class FoldingController : IDisposable
{
    /// <summary>How long after the last edit the ranges are asked for again.</summary>
    public static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(500);

    private readonly TextArea area;
    private readonly ISyntaxFeatures features;
    private readonly IDocument document;
    private readonly DispatcherTimer timer;
    private CancellationTokenSource? request;
    private bool isEnabled = true;
    private bool disposed;

    public FoldingController(TextArea area, ISyntaxFeatures features)
    {
        this.area = area;
        this.features = features;
        document = area.Document;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Refresh();
        };
        document.Buffer.Changed += OnBufferChanged;
        document.PropertyChanged += OnDocumentPropertyChanged;
        features.Changed += OnFeaturesChanged;
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }

    /// <summary>Gets or sets whether the editor folds; turning it off unfolds everything and forgets the regions.</summary>
    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (isEnabled == value)
                return;

            isEnabled = value;
            if (value)
            {
                Refresh();
            }
            else
            {
                Cancel();
                area.Folding.SetRanges([], area.Snapshot.LineCount);
            }
        }
    }

    /// <summary>Asks for the ranges now.</summary>
    public void Refresh()
    {
        if (!isEnabled || disposed)
            return;

        Cancel();
        request = new CancellationTokenSource();
        var token = request.Token;
        var snapshot = document.Buffer.Current;
        var tabSize = area.Options.TabSize;
        _ = RunAsync(snapshot, tabSize, token);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        timer.Stop();
        Cancel();
        document.Buffer.Changed -= OnBufferChanged;
        document.PropertyChanged -= OnDocumentPropertyChanged;
        features.Changed -= OnFeaturesChanged;
    }

    private async Task RunAsync(TextSnapshot snapshot, int tabSize, CancellationToken token)
    {
        IReadOnlyList<FoldingRange> ranges;
        try
        {
            ranges = await Task.Run(async () =>
                await features.GetFoldingRangesAsync(document, snapshot, token).ConfigureAwait(false) ?? IndentationFolding.Compute(snapshot, tabSize, token), token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or LanguageFeatureException)
        {
            return;
        }

        if (token.IsCancellationRequested || disposed || !isEnabled || !ReferenceEquals(snapshot, document.Buffer.Current))
            return;

        area.Folding.SetRanges(ranges, snapshot.LineCount);
    }

    private void Cancel()
    {
        request?.Cancel();
        request?.Dispose();
        request = null;
    }

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        if (!isEnabled)
            return;

        timer.Stop();
        timer.Start();
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IDocument.LanguageId))
            Refresh();
    }

    private void OnFeaturesChanged(object? sender, LanguageFeatureChangedEventArgs e)
    {
        if (e.FilePath is null || document.FilePath is { } path && string.Equals(Path.GetFullPath(e.FilePath), Path.GetFullPath(path), StringComparison.Ordinal))
            Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }
}
