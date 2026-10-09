using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;
using TextMateSharp.Grammars;
using TextMateSharp.Internal.Grammars;

namespace Runesmith.Languages.Highlighting;

/// <summary>Highlights one document with a TextMate grammar on a background thread, retokenizing only what an edit can affect.</summary>
/// <remarks>Each line keeps the grammar state at its end. After an edit, tokenizing resumes at the first edited line and stops at the first
/// line whose end state comes out the same as before, because every line below it would come out the same too.</remarks>
internal sealed class TextMateHighlighter : ISyntaxHighlighter
{
    private const int MaxLineLength = 10_000;
    private static readonly TimeSpan LineTimeLimit = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan BackgroundSlice = TimeSpan.FromMilliseconds(15);

    private readonly IDocument document;
    private readonly TextMateHighlighterProvider provider;
    private readonly Lock gate = new();
    private readonly List<LineState> lines = [];
    private IGrammar? grammar;
    private ThemedRegistry? registry;
    private StyleTable? styles;
    private long generation;
    private int firstDirty;
    private int visibleFirst;
    private int visibleLast = 100;
    private bool working;
    private bool disposed;

    private int changedFirst = int.MaxValue;
    private int changedLast = -1;
    private bool notifyScheduled;

    public TextMateHighlighter(IDocument document, TextMateHighlighterProvider provider)
    {
        this.document = document;
        this.provider = provider;
        document.Buffer.Changed += OnBufferChanged;
        document.PropertyChanged += OnDocumentPropertyChanged;
        Reset();
    }

    public event EventHandler<HighlightingChangedEventArgs>? Changed;

    public IReadOnlyList<SyntaxToken> GetTokens(int lineNumber)
    {
        SyntaxToken[] tokens;
        lock (gate)
        {
            if ((uint)lineNumber >= (uint)lines.Count)
                return [];
            tokens = lines[lineNumber].Tokens;
        }

        if (tokens.Length == 0)
            return tokens;

        // A line edited since it was tokenized keeps its old tokens until the new ones arrive, cut to its new length.
        var snapshot = document.Buffer.Current;
        var length = lineNumber < snapshot.LineCount ? snapshot.GetLine(lineNumber).Length : 0;
        var last = tokens[^1];
        return last.Start + last.Length <= length ? tokens : Clip(tokens, length);
    }

    public void Prioritize(int firstLine, int lastLine)
    {
        lock (gate)
        {
            visibleFirst = Math.Max(0, firstLine);
            visibleLast = Math.Max(visibleFirst, lastLine);
        }
    }

    /// <summary>Starts over with the current language and theme, keeping the old tokens on screen until new ones replace them.</summary>
    public void Reset()
    {
        var language = provider.Registry.Find(document.LanguageId);
        var themed = provider.Grammars.GetRegistry(provider.Scheme.FilePath);
        var newGrammar = language?.ScopeName is { } scope ? themed.GetGrammar(scope) : null;
        lock (gate)
        {
            registry = themed;
            styles = provider.GetStyles(themed);
            grammar = newGrammar;
            generation++;
            var count = document.Buffer.Current.LineCount;
            if (lines.Count > count)
                lines.RemoveRange(count, lines.Count - count);
            while (lines.Count < count)
                lines.Add(new LineState());
            foreach (var line in lines)
            {
                line.EndState = null;
                line.Dirty = true;
            }

            if (grammar is null)
            {
                foreach (var line in lines)
                {
                    line.Tokens = [];
                    line.Dirty = false;
                }
            }

            firstDirty = grammar is null ? lines.Count : 0;
        }

        if (newGrammar is null)
            MarkChanged(0, int.MaxValue);
        Schedule();
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            generation++;
        }

        document.Buffer.Changed -= OnBufferChanged;
        document.PropertyChanged -= OnDocumentPropertyChanged;
        provider.Remove(this);
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IDocument.LanguageId))
            Reset();
    }

    private void OnBufferChanged(object? sender, TextChangedEventArgs e)
    {
        var changeSet = e.ChangeSet;
        var before = changeSet.Before;
        lock (gate)
        {
            generation++;

            // In descending order, each change's lines are still where the old snapshot has them.
            for (var i = changeSet.Changes.Count - 1; i >= 0; i--)
            {
                var change = changeSet.Changes[i];
                var startLine = before.GetLineFromPosition(change.Span.Start).LineNumber;
                var endLine = before.GetLineFromPosition(change.Span.End).LineNumber;
                var inserted = change.NewText.AsSpan().Count('\n');

                // The last of the new lines ends where the old end line ended, so it keeps that line's end state for the early stop.
                lines.RemoveRange(startLine, Math.Min(endLine - startLine, lines.Count - startLine));
                for (var n = 0; n < inserted; n++)
                    lines.Insert(startLine, new LineState());
                for (var n = startLine; n <= startLine + inserted && n < lines.Count; n++)
                    lines[n].Dirty = true;
                firstDirty = Math.Min(firstDirty, startLine);
            }

            var count = changeSet.After.LineCount;
            if (lines.Count != count)
            {
                Debug.Fail($"Line states ({lines.Count}) do not match the text ({count}).");
                while (lines.Count < count)
                    lines.Add(new LineState { Dirty = true });
                if (lines.Count > count)
                    lines.RemoveRange(count, lines.Count - count);
            }

            if (grammar is null)
            {
                foreach (var line in lines)
                    line.Dirty = false;
                firstDirty = lines.Count;
            }
        }

        Schedule();
    }

    private void Schedule()
    {
        lock (gate)
        {
            if (working || disposed || firstDirty >= lines.Count)
                return;
            working = true;
        }

        _ = Task.Run(Work);
    }

    private async Task Work()
    {
        try
        {
            while (true)
            {
                var sliceStart = Stopwatch.GetTimestamp();
                bool inView;
                do
                {
                    if (!TokenizeNextLine(out inView))
                        return;
                }
                while (inView || Stopwatch.GetElapsedTime(sliceStart) < BackgroundSlice);

                Flush();
                // Lines below the view give way to other work between slices.
                await Task.Delay(1).ConfigureAwait(false);
            }
        }
        finally
        {
            bool again;
            lock (gate)
            {
                working = false;
                again = !disposed && firstDirty < lines.Count;
            }

            Flush();
            if (again)
                Schedule();
        }
    }

    /// <summary>Tokenizes the first line that needs it; returns false when no line does.</summary>
    private bool TokenizeNextLine(out bool inView)
    {
        IGrammar? currentGrammar;
        StyleTable? currentStyles;
        TextSnapshot snapshot;
        IStateStack? startState;
        long currentGeneration;
        int lineNumber;
        lock (gate)
        {
            inView = false;
            while (firstDirty < lines.Count && !lines[firstDirty].Dirty)
                firstDirty++;
            if (disposed || grammar is null || firstDirty >= lines.Count)
                return false;

            lineNumber = firstDirty;
            inView = lineNumber <= visibleLast;
            currentGrammar = grammar;
            currentStyles = styles;
            startState = lineNumber == 0 ? null : lines[lineNumber - 1].EndState;
            currentGeneration = generation;
            snapshot = document.Buffer.Current;
        }

        var line = snapshot.GetLine(lineNumber);
        SyntaxToken[] tokens;
        IStateStack endState;
        if (line.Length > MaxLineLength)
        {
            tokens = [];
            endState = startState ?? StateStack.NULL;
        }
        else
        {
            ITokenizeLineResult2 result;
            lock (currentGrammar)
                result = currentGrammar.TokenizeLine2(new LineText(snapshot.GetText(line.Span)), startState, LineTimeLimit);
            tokens = ToTokens(result.Tokens, line.Length, currentStyles!);
            endState = result.RuleStack;
        }

        lock (gate)
        {
            // An edit or a reset since this line was read makes the result stale; the line is dirty again and is redone.
            if (currentGeneration != generation || lineNumber >= lines.Count)
                return true;

            var state = lines[lineNumber];
            var stateChanged = !Equals(state.EndState, endState);
            if (!SameTokens(state.Tokens, tokens))
                MarkChangedLocked(lineNumber, lineNumber);
            state.Tokens = tokens;
            state.EndState = endState;
            state.Dirty = false;
            if (stateChanged && lineNumber + 1 < lines.Count)
                lines[lineNumber + 1].Dirty = true;
            firstDirty = lineNumber + 1;
        }

        return true;
    }

    private static SyntaxToken[] ToTokens(int[] encoded, int lineLength, StyleTable styles)
    {
        var tokens = new List<SyntaxToken>(encoded.Length / 2);
        for (var i = 0; i < encoded.Length; i += 2)
        {
            var start = Math.Min(encoded[i], lineLength);
            var end = i + 2 < encoded.Length ? Math.Min(encoded[i + 2], lineLength) : lineLength;
            var metadata = encoded[i + 1];
            var style = styles.Get(EncodedTokenAttributes.GetForeground(metadata), EncodedTokenAttributes.GetFontStyle(metadata));
            if (style is null || end <= start)
                continue;

            if (tokens.Count > 0 && tokens[^1] is var previous && previous.Style == style && previous.Start + previous.Length == start)
                tokens[^1] = previous with { Length = end - previous.Start };
            else
                tokens.Add(new SyntaxToken(start, end - start, style));
        }

        return [.. tokens];
    }

    private static bool SameTokens(SyntaxToken[] first, SyntaxToken[] second) => first.AsSpan().SequenceEqual(second);

    private static SyntaxToken[] Clip(SyntaxToken[] tokens, int length)
    {
        var clipped = new List<SyntaxToken>(tokens.Length);
        foreach (var token in tokens)
        {
            if (token.Start >= length)
                break;
            clipped.Add(token.Start + token.Length <= length ? token : token with { Length = length - token.Start });
        }

        return [.. clipped];
    }

    private void MarkChanged(int first, int last)
    {
        lock (gate)
            MarkChangedLocked(first, last);
        Flush();
    }

    private void MarkChangedLocked(int first, int last)
    {
        changedFirst = Math.Min(changedFirst, first);
        changedLast = Math.Max(changedLast, last);
    }

    private void Flush()
    {
        lock (gate)
        {
            if (changedLast < 0 || notifyScheduled || disposed)
                return;
            notifyScheduled = true;
        }

        Dispatcher.UIThread.Post(Notify, DispatcherPriority.Background);
    }

    private void Notify()
    {
        int first;
        int last;
        lock (gate)
        {
            notifyScheduled = false;
            if (disposed || changedLast < 0)
                return;
            first = changedFirst;
            last = Math.Min(changedLast, Math.Max(0, lines.Count - 1));
            changedFirst = int.MaxValue;
            changedLast = -1;
        }

        Changed?.Invoke(this, new HighlightingChangedEventArgs(first, last));
    }

    private sealed class LineState
    {
        public SyntaxToken[] Tokens { get; set; } = [];

        public IStateStack? EndState { get; set; }

        public bool Dirty { get; set; }
    }
}
