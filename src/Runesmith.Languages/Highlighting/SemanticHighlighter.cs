using System.ComponentModel;
using Avalonia.Media;
using Avalonia.Threading;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Languages.Highlighting;

/// <summary>A semantic token of one line, styled: in <see cref="Style"/>, or, when it is null, in the style beneath struck through.</summary>
internal readonly record struct OverlayToken(int Start, int Length, SyntaxStyle? Style)
{
    public int End => Start + Length;
}

/// <summary>Draws a document's semantic tokens over its TextMate highlighting. It asks for them when the document opens, a moment after
/// typing stops, and when a provider says they changed; until an answer comes, the tokens it has follow the edits, so colors do not flicker.</summary>
internal sealed class SemanticHighlighter : ISyntaxHighlighter
{
    /// <summary>How long after the last edit the tokens are asked for again.</summary>
    public static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Documents longer than this get the tokens of the lines on screen first.</summary>
    private const int RangeFirstLineCount = 1500;

    private readonly ISyntaxHighlighter inner;
    private readonly IDocument document;
    private readonly ISyntaxFeatures features;
    private readonly Func<SemanticTokenStyles?> styles;
    private readonly Action<SemanticHighlighter>? onDisposed;
    private readonly DispatcherTimer timer;
    private readonly List<OverlayToken[]> lines = [];
    private CancellationTokenSource? request;
    private int visibleFirst;
    private int visibleLast = 100;
    private bool disposed;

    public SemanticHighlighter(ISyntaxHighlighter inner, IDocument document, ISyntaxFeatures features, Func<SemanticTokenStyles?> styles,
        Action<SemanticHighlighter>? onDisposed = null)
    {
        this.onDisposed = onDisposed;
        this.inner = inner;
        this.document = document;
        this.features = features;
        this.styles = styles;
        timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = RefreshDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            Refresh();
        };
        inner.Changed += OnInnerChanged;
        document.Buffer.Changed += OnBufferChanged;
        document.PropertyChanged += OnDocumentPropertyChanged;
        features.Changed += OnFeaturesChanged;
        Reset();
        Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Background);
    }

    public event EventHandler<HighlightingChangedEventArgs>? Changed;

    /// <summary>Gets the semantic tokens of a line, for tests.</summary>
    internal IReadOnlyList<OverlayToken> OverlayOf(int lineNumber) => (uint)lineNumber < (uint)lines.Count ? lines[lineNumber] : [];

    public IReadOnlyList<SyntaxToken> GetTokens(int lineNumber)
    {
        var tokens = inner.GetTokens(lineNumber);
        if ((uint)lineNumber >= (uint)lines.Count || lines[lineNumber].Length == 0)
            return tokens;

        var length = lineNumber < document.Buffer.Current.LineCount ? document.Buffer.Current.GetLine(lineNumber).Length : 0;
        return Merge(tokens, lines[lineNumber], length, styles()?.PlainStruck);
    }

    public void Prioritize(int firstLine, int lastLine)
    {
        visibleFirst = firstLine;
        visibleLast = lastLine;
        inner.Prioritize(firstLine, lastLine);
    }

    /// <summary>Restyles the tokens, such as after the color scheme changed, by asking for them again.</summary>
    public void Restyle()
    {
        timer.Stop();
        Refresh();
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
        inner.Changed -= OnInnerChanged;
        document.Buffer.Changed -= OnBufferChanged;
        document.PropertyChanged -= OnDocumentPropertyChanged;
        features.Changed -= OnFeaturesChanged;
        onDisposed?.Invoke(this);
        inner.Dispose();
    }

    /// <summary>Lays semantic tokens over a line's TextMate tokens: each semantic token replaces what is beneath it.</summary>
    internal static IReadOnlyList<SyntaxToken> Merge(IReadOnlyList<SyntaxToken> below, IReadOnlyList<OverlayToken> above, int lineLength, SyntaxStyle? plainStruck)
    {
        var merged = new List<SyntaxToken>(below.Count + above.Count * 2);
        var cursor = 0;
        foreach (var token in above)
        {
            var start = Math.Max(token.Start, cursor);
            var end = Math.Min(token.End, lineLength);
            if (end <= start)
                continue;

            Copy(below, cursor, start, merged, null, null);
            if (token.Style is { } style)
                merged.Add(new SyntaxToken(start, end - start, style));
            else
                Copy(below, start, end, merged, s => s with { IsStrikethrough = true }, plainStruck);
            cursor = end;
        }

        Copy(below, cursor, int.MaxValue, merged, null, null);
        return merged;
    }

    // Copies the parts of the tokens between two columns, changed by restyle; with a gap style, the columns no token covers get it.
    private static void Copy(IReadOnlyList<SyntaxToken> tokens, int from, int to, List<SyntaxToken> into, Func<SyntaxStyle, SyntaxStyle>? restyle, SyntaxStyle? gap)
    {
        if (to <= from)
            return;

        var cursor = from;
        foreach (var token in tokens)
        {
            var start = Math.Max(token.Start, from);
            var end = Math.Min(token.Start + token.Length, to);
            if (end <= start)
            {
                if (token.Start >= to)
                    break;
                continue;
            }

            if (gap is not null && start > cursor)
                into.Add(new SyntaxToken(cursor, start - cursor, gap));
            into.Add(new SyntaxToken(start, end - start, restyle is null ? token.Style : restyle(token.Style)));
            cursor = end;
        }

        if (gap is not null && to != int.MaxValue && cursor < to)
            into.Add(new SyntaxToken(cursor, to - cursor, gap));
    }

    private void Reset()
    {
        lines.Clear();
        for (var i = 0; i < document.Buffer.Current.LineCount; i++)
            lines.Add([]);
    }

    private void Refresh()
    {
        if (disposed)
            return;

        request?.Cancel();
        request?.Dispose();
        if (!features.HasSemanticTokens(document) || styles() is not { } current)
        {
            request = null;
            Apply(null, (0, int.MaxValue));
            return;
        }

        request = new CancellationTokenSource();
        var token = request.Token;
        var snapshot = document.Buffer.Current;
        TextSpan? visible = null;
        if (snapshot.LineCount > RangeFirstLineCount)
        {
            var first = Math.Clamp(visibleFirst, 0, snapshot.LineCount - 1);
            var last = Math.Clamp(visibleLast, first, snapshot.LineCount - 1);
            visible = TextSpan.FromBounds(snapshot.GetLine(first).Start, snapshot.GetLine(last).EndIncludingBreak);
        }

        _ = Task.Run(async () =>
        {
            if (visible is { } span)
                await RequestAsync(snapshot, span, current, token).ConfigureAwait(false);
            await RequestAsync(snapshot, null, current, token).ConfigureAwait(false);
        }, token);
    }

    private async Task RequestAsync(TextSnapshot snapshot, TextSpan? span, SemanticTokenStyles current, CancellationToken token)
    {
        IReadOnlyList<SemanticToken>? tokens;
        try
        {
            tokens = await features.GetSemanticTokensAsync(document, snapshot, span, token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or LanguageFeatureException)
        {
            return;
        }

        if (token.IsCancellationRequested)
            return;

        var byLine = tokens is null ? null : Group(snapshot, tokens, current);
        var lineRange = span is { } value ? (snapshot.GetLineFromPosition(value.Start).LineNumber, snapshot.GetLineFromPosition(value.End).LineNumber) : (0, snapshot.LineCount - 1);
        Dispatcher.UIThread.Post(() =>
        {
            if (!token.IsCancellationRequested && !disposed && ReferenceEquals(snapshot, document.Buffer.Current))
                Apply(byLine, lineRange);
        }, DispatcherPriority.Background);
    }

    // Styles the tokens and sorts them into their lines.
    private static Dictionary<int, OverlayToken[]> Group(TextSnapshot snapshot, IReadOnlyList<SemanticToken> tokens, SemanticTokenStyles styles)
    {
        var byLine = new Dictionary<int, List<OverlayToken>>();
        foreach (var token in tokens)
        {
            if (token.Span.Start < 0 || token.Span.End > snapshot.Length || token.Span.IsEmpty || styles.Get(token.Type, token.Modifiers) is not { } style)
                continue;

            var line = snapshot.GetLineFromPosition(token.Span.Start);
            var end = Math.Min(token.Span.End, line.End);
            if (end <= token.Span.Start)
                continue;

            if (!byLine.TryGetValue(line.LineNumber, out var list))
                byLine[line.LineNumber] = list = [];
            list.Add(new OverlayToken(token.Span.Start - line.Start, end - token.Span.Start, style.Style));
        }

        return byLine.ToDictionary(pair => pair.Key, pair => pair.Value.OrderBy(token => token.Start).ToArray());
    }

    // Replaces the tokens of the lines in a range; null clears them.
    private void Apply(Dictionary<int, OverlayToken[]>? byLine, (int First, int Last) range)
    {
        if (lines.Count != document.Buffer.Current.LineCount)
            Reset();

        var changedFirst = int.MaxValue;
        var changedLast = -1;
        var last = Math.Min(range.Last, lines.Count - 1);
        for (var number = Math.Max(0, range.First); number <= last; number++)
        {
            OverlayToken[] tokens = byLine?.GetValueOrDefault(number) ?? [];
            if (lines[number].AsSpan().SequenceEqual(tokens))
                continue;

            lines[number] = tokens;
            changedFirst = Math.Min(changedFirst, number);
            changedLast = number;
        }

        if (changedLast >= 0)
            Changed?.Invoke(this, new HighlightingChangedEventArgs(changedFirst, changedLast));
    }

    // Tokens follow the edits: a change within one line moves the tokens after it, and lines a change spans lose theirs until the answer.
    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        var changeSet = e.ChangeSet;
        var before = changeSet.Before;
        if (lines.Count != before.LineCount)
            Reset();

        for (var i = changeSet.Changes.Count - 1; i >= 0; i--)
        {
            var change = changeSet.Changes[i];
            var startLine = before.GetLineFromPosition(change.Span.Start);
            var endLine = before.GetLineFromPosition(change.Span.End).LineNumber;
            var inserted = change.NewText.AsSpan().Count('\n');
            if (startLine.LineNumber == endLine && inserted == 0)
            {
                var column = change.Span.Start - startLine.Start;
                lines[endLine] = Shift(lines[endLine], column, column + change.Span.Length, change.NewText.Length - change.Span.Length);
                continue;
            }

            lines.RemoveRange(startLine.LineNumber, endLine - startLine.LineNumber);
            for (var n = 0; n < inserted; n++)
                lines.Insert(startLine.LineNumber, []);
            for (var n = startLine.LineNumber; n <= startLine.LineNumber + inserted && n < lines.Count; n++)
                lines[n] = [];
        }

        if (lines.Count != changeSet.After.LineCount)
            Reset();

        timer.Stop();
        timer.Start();
    }

    private static OverlayToken[] Shift(OverlayToken[] tokens, int start, int end, int delta)
    {
        if (tokens.Length == 0)
            return tokens;

        var shifted = new List<OverlayToken>(tokens.Length);
        foreach (var token in tokens)
        {
            if (token.End <= start)
                shifted.Add(token);
            else if (token.Start >= end)
                shifted.Add(token with { Start = token.Start + delta });
        }

        return [.. shifted];
    }

    private void OnInnerChanged(object? sender, HighlightingChangedEventArgs e) => Changed?.Invoke(this, e);

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IDocument.LanguageId) or nameof(IDocument.FilePath))
            Dispatcher.UIThread.Post(Restyle, DispatcherPriority.Background);
    }

    private void OnFeaturesChanged(object? sender, LanguageFeatureChangedEventArgs e)
    {
        if (e.FilePath is null || document.FilePath is { } path && string.Equals(Path.GetFullPath(e.FilePath), Path.GetFullPath(path), StringComparison.Ordinal))
            Dispatcher.UIThread.Post(Restyle, DispatcherPriority.Background);
    }
}
