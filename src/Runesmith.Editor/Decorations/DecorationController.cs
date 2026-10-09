using System.ComponentModel;
using Avalonia.Threading;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Editor.Decorations;

/// <summary>Asks the decoration providers of an editor's document for their decorations: when the document opens, a moment after typing
/// stops, and when a provider says its decorations changed. Each provider's answer replaces only its own decorations.</summary>
/// <remarks>Requests run on the thread pool and are cancelled by newer ones. Edits made while a request runs are recorded, and its answer is
/// moved through them before it is shown.</remarks>
internal sealed class DecorationController : IDisposable
{
    /// <summary>How long after the last edit the providers are asked again.</summary>
    public static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(400);

    private readonly TextArea area;
    private readonly IEditorFeatures features;
    private readonly IDocument document;
    private readonly DispatcherTimer timer;
    private readonly Dictionary<IDecorationProvider, Request> requests = [];
    private IReadOnlyList<IDecorationProvider> providers = [];
    private bool isEnabled = true;
    private bool disposed;

    public DecorationController(TextArea area, IEditorFeatures features)
    {
        this.area = area;
        this.features = features;
        document = area.Document;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RefreshAll();
        };
        document.Buffer.Changed += OnBufferChanged;
        document.PropertyChanged += OnDocumentPropertyChanged;
        Attach();
    }

    /// <summary>Gets or sets whether the editor shows decorations; turning them off clears them.</summary>
    public bool IsEnabled
    {
        get => isEnabled;
        set
        {
            if (isEnabled == value)
                return;

            isEnabled = value;
            if (value)
                RefreshAll();
            else
                Clear();
        }
    }

    /// <summary>Asks every provider again.</summary>
    public void RefreshAll()
    {
        if (!isEnabled || disposed)
            return;

        foreach (var provider in providers)
            Refresh(provider);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        timer.Stop();
        document.Buffer.Changed -= OnBufferChanged;
        document.PropertyChanged -= OnDocumentPropertyChanged;
        Detach();
    }

    private void Attach()
    {
        providers = features.GetDecorationProviders(document);
        foreach (var provider in providers)
            provider.Changed += OnProviderChanged;
        Dispatcher.UIThread.Post(RefreshAll, DispatcherPriority.Background);
    }

    private void Detach()
    {
        foreach (var provider in providers)
            provider.Changed -= OnProviderChanged;
        Clear();
        providers = [];
    }

    private void Clear()
    {
        foreach (var request in requests.Values)
            request.Dispose();
        requests.Clear();
        foreach (var provider in providers)
            area.ClearDecorations(provider);
    }

    private void Refresh(IDecorationProvider provider)
    {
        if (requests.Remove(provider, out var previous))
            previous.Dispose();

        var snapshot = document.Buffer.Current;
        var request = new Request();
        requests[provider] = request;
        _ = RunAsync(provider, snapshot, request);
    }

    private async Task RunAsync(IDecorationProvider provider, TextSnapshot snapshot, Request request)
    {
        IReadOnlyList<Decoration> decorations;
        try
        {
            var token = request.Token;
            decorations = await Task.Run(() => provider.GetDecorationsAsync(new DecorationRequest(document, snapshot), token), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (request.IsCancelled || disposed || !requests.TryGetValue(provider, out var current) || current != request)
            return;

        requests.Remove(provider);
        area.SetDecorations(provider, DecorationStore.Place(decorations, snapshot.Length, request.Edits));
        request.Dispose();
    }

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        if (!isEnabled || providers.Count == 0)
            return;

        foreach (var request in requests.Values)
            request.Edits.Add(e.ChangeSet.Changes);
        timer.Stop();
        timer.Start();
    }

    private void OnProviderChanged(object? sender, DecorationsChangedEventArgs e)
    {
        if (sender is not IDecorationProvider provider || e.FilePath is { } path && !IsSamePath(path, document.FilePath))
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (isEnabled && !disposed && providers.Contains(provider))
                Refresh(provider);
        });
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IDocument.LanguageId))
            return;

        Detach();
        Attach();
    }

    private static bool IsSamePath(string a, string? b) =>
        b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class Request : IDisposable
    {
        private readonly CancellationTokenSource source = new();

        public CancellationToken Token => source.Token;

        public bool IsCancelled => source.IsCancellationRequested;

        /// <summary>Gets the edits made since the request's snapshot, in order.</summary>
        public List<IReadOnlyList<TextChange>> Edits { get; } = [];

        public void Dispose()
        {
            source.Cancel();
            source.Dispose();
        }
    }
}
