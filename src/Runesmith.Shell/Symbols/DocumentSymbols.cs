using System.Composition;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Runesmith.Editor;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Shell.Symbols;

/// <summary>Keeps the symbol tree of each editor that something shows it for, such as the Outline or the breadcrumbs.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class DocumentSymbols(INavigationFeatures features)
{
    private readonly ConditionalWeakTable<TextEditor, EditorSymbols> trackers = [];

    /// <summary>Gets the symbols of an editor's document, asking for them the first time.</summary>
    public EditorSymbols For(TextEditor editor) => trackers.GetValue(editor, created => new EditorSymbols(created, features));

    /// <summary>Stops following an editor that closed.</summary>
    public void Release(TextEditor editor)
    {
        if (trackers.TryGetValue(editor, out var tracker))
        {
            tracker.Dispose();
            trackers.Remove(editor);
        }
    }
}

/// <summary>The symbol tree of one editor's document, asked for again a moment after typing stops and when a provider's symbols change.</summary>
public sealed class EditorSymbols : IDisposable
{
    /// <summary>How long after the last edit the symbols are asked for again.</summary>
    public static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(600);

    private readonly TextEditor editor;
    private readonly INavigationFeatures features;
    private readonly DispatcherTimer timer;
    private CancellationTokenSource? request;
    private readonly List<IReadOnlyList<TextChange>> pendingEdits = [];
    private IReadOnlyList<DocumentSymbol>? symbols;
    private bool disposed;

    internal EditorSymbols(TextEditor editor, INavigationFeatures features)
    {
        this.editor = editor;
        this.features = features;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = RefreshAsync();
        };
        editor.Document.Buffer.Changed += OnBufferChanged;
        features.Changed += OnFeaturesChanged;
        _ = RefreshAsync();
    }

    /// <summary>Gets the top-level symbols, moved through the edits made since they were found; null while none have been found or no provider
    /// serves the language.</summary>
    public IReadOnlyList<DocumentSymbol>? Symbols
    {
        get
        {
            if (symbols is not null && pendingEdits.Count > 0)
            {
                foreach (var changes in pendingEdits)
                    symbols = Map(symbols, changes);
                pendingEdits.Clear();
            }

            return symbols;
        }
    }

    /// <summary>Gets whether a provider answered for the document.</summary>
    public bool HasProvider => Symbols is not null;

    /// <summary>Gets whether a request is running.</summary>
    public bool IsLoading => request is not null;

    /// <summary>Raised on the UI thread when <see cref="Symbols"/> changes.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets the symbols that contain an offset, outermost first.</summary>
    public IReadOnlyList<DocumentSymbol> PathAt(int offset) => Symbols is { } symbols ? SymbolKinds.PathAt(symbols, offset) : [];

    /// <summary>Asks for the symbols now.</summary>
    public async Task RefreshAsync()
    {
        if (disposed)
            return;

        request?.Cancel();
        request?.Dispose();
        request = new CancellationTokenSource();
        var token = request.Token;
        var snapshot = editor.Area.Snapshot;
        IReadOnlyList<DocumentSymbol>? symbols;
        try
        {
            symbols = await Task.Run(() => features.GetDocumentSymbolsAsync(editor.Document, snapshot, token), token);
        }
        catch (Exception exception) when (exception is OperationCanceledException or LanguageFeatureException)
        {
            return;
        }

        if (token.IsCancellationRequested || disposed || !ReferenceEquals(snapshot, editor.Area.Snapshot))
            return;

        request.Dispose();
        request = null;
        pendingEdits.Clear();
        this.symbols = symbols is null ? null : Clamp(symbols, snapshot.Length);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        timer.Stop();
        request?.Cancel();
        request?.Dispose();
        request = null;
        editor.Document.Buffer.Changed -= OnBufferChanged;
        features.Changed -= OnFeaturesChanged;
    }

    private static List<DocumentSymbol> Clamp(IReadOnlyList<DocumentSymbol> symbols, int length) =>
    [
        .. symbols.Where(s => s.Range.Start <= length).OrderBy(s => s.Range.Start).Select(s => s with
        {
            Range = TextSpan.FromBounds(s.Range.Start, Math.Min(s.Range.End, length)),
            SelectionRange = TextSpan.FromBounds(Math.Min(s.SelectionRange.Start, length), Math.Min(s.SelectionRange.End, length)),
            Children = s.Children.Count == 0 ? s.Children : Clamp(s.Children, length),
        }),
    ];

    // Ranges move with the text until the next answer, so the breadcrumbs and the Outline follow the caret while typing.
    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        if (symbols is not null)
            pendingEdits.Add(e.ChangeSet.Changes);
        timer.Stop();
        timer.Start();
    }

    private static List<DocumentSymbol> Map(IReadOnlyList<DocumentSymbol> symbols, IReadOnlyList<TextChange> changes) =>
    [
        .. symbols.Select(s => s with
        {
            Range = OffsetMapping.Map(changes, s.Range),
            SelectionRange = OffsetMapping.Map(changes, s.SelectionRange),
            Children = s.Children.Count == 0 ? s.Children : Map(s.Children, changes),
        }),
    ];

    private void OnFeaturesChanged(object? sender, LanguageFeatureChangedEventArgs e)
    {
        if (e.FilePath is null || editor.Document.FilePath is { } path && string.Equals(Path.GetFullPath(e.FilePath), Path.GetFullPath(path), StringComparison.Ordinal))
            Dispatcher.UIThread.Post(() => _ = RefreshAsync(), DispatcherPriority.Background);
    }
}
