using System.Composition;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Workspace;
using Runesmith.Text;
using Runesmith.Workspace.Files;

namespace Runesmith.Workspace.Search;

/// <summary>One match in a file.</summary>
/// <param name="Line">The line, counted from zero.</param>
/// <param name="Column">The match's column in the line, counted from zero.</param>
/// <param name="Preview">The line, or the part of a long line around the match, for showing in a list.</param>
/// <param name="PreviewColumn">Where the match starts in <paramref name="Preview"/>.</param>
public sealed record FileSearchMatch(int Line, int Column, int Length, string Preview, int PreviewColumn);

/// <summary>The matches in one file.</summary>
/// <param name="IsLimitReached">Whether the search stopped after this file because it found <see cref="FindInFiles.MaxMatches"/> matches.</param>
public sealed record FileSearchResult(string FilePath, IReadOnlyList<FileSearchMatch> Matches, bool IsLimitReached = false);

/// <summary>Searches the files of the open folder.</summary>
[Export(typeof(FindInFiles))]
[Shared]
public sealed class FindInFiles
{
    /// <summary>How many matches a search finds at most.</summary>
    public const int MaxMatches = 10_000;

    /// <summary>Files larger than this, in bytes, are not searched.</summary>
    public const long MaxFileSize = 10 * 1024 * 1024;

    private const int PreviewLength = 200;
    private const int PreviewLead = 60;

    private readonly IWorkspace _workspace;
    private readonly IDocumentService _documents;

    [ImportingConstructor]
    public FindInFiles(IWorkspace workspace, IDocumentService documents)
    {
        _workspace = workspace;
        _documents = documents;
    }

    /// <summary>Searches every file that is not excluded, as each file finishes; open documents are searched as they are in the editor.</summary>
    /// <param name="includeGlobs">Semicolon-separated globs a file must match, such as <c>*.cs;src/**</c>, or null for every file.</param>
    /// <param name="excludeGlobs">Semicolon-separated globs of files to leave out, on top of <c>files.exclude</c>.</param>
    /// <exception cref="ArgumentException">The query is an invalid regular expression.</exception>
    /// <remarks>Call it on the UI thread, which is where the open documents' text is taken from.</remarks>
    public IAsyncEnumerable<FileSearchResult> SearchAsync(
        string query, TextSearchOptions options, string? includeGlobs = null, string? excludeGlobs = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (options.HasFlag(TextSearchOptions.Regex) && !TextSearch.TryCreateRegex(query, options, out _, out var error))
            throw new ArgumentException(error, nameof(query));

        var open = _documents.Documents
            .Where(d => d.FilePath is not null)
            .ToDictionary(d => d.FilePath!, d => d.Buffer.Current, PathComparison.Comparer);
        return Search(query, options, GlobMatcher.Parse(includeGlobs), GlobMatcher.Parse(excludeGlobs), open, cancellationToken);
    }

    private async IAsyncEnumerable<FileSearchResult> Search(
        string query, TextSearchOptions options, GlobMatcher include, GlobMatcher exclude, Dictionary<string, TextSnapshot> open,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (query.Length == 0 || _workspace.RootPath is not { } root)
            yield break;

        var files = await _workspace.GetFilesAsync(cancellationToken).ConfigureAwait(false);
        var channel = Channel.CreateBounded<FileSearchResult>(64);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var found = 0;
        var producer = Task.Run(async () =>
        {
            try
            {
                var parallel = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = stop.Token };
                await Parallel.ForEachAsync(files, parallel, async (path, token) =>
                {
                    var relative = GlobMatcher.ToGlobPath(Path.GetRelativePath(root, path));
                    if ((!include.IsEmpty && !include.IsMatch(relative)) || exclude.IsMatchOrInside(relative))
                        return;

                    var snapshot = open.TryGetValue(path, out var current) ? current : ReadSnapshot(path);
                    if (snapshot is null || FindMatches(snapshot, query, options) is not { Count: > 0 } matches)
                        return;

                    var total = Interlocked.Add(ref found, matches.Count);
                    var limitReached = total >= MaxMatches;
                    if (limitReached)
                        matches = matches[..Math.Max(0, matches.Count - (total - MaxMatches))];
                    if (matches.Count > 0)
                        await channel.Writer.WriteAsync(new FileSearchResult(path, matches, limitReached), token).ConfigureAwait(false);
                    if (limitReached)
                        await stop.CancelAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                // The search stopped at the match limit.
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        await foreach (var result in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return result;
        await producer.ConfigureAwait(false);
    }

    private static TextSnapshot? ReadSnapshot(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaxFileSize)
                return null;
            return TextFileReader.Decode(File.ReadAllBytes(path)) is { } file ? TextSnapshot.Create(file.Text) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static List<FileSearchMatch> FindMatches(TextSnapshot snapshot, string query, TextSearchOptions options)
    {
        IReadOnlyList<TextSpan> spans;
        try
        {
            spans = TextSearch.FindAll(snapshot, query, options);
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }

        var matches = new List<FileSearchMatch>(spans.Count);
        foreach (var span in spans)
        {
            var line = snapshot.GetLineFromPosition(span.Start);
            var column = span.Start - line.Start;
            var previewStart = line.Length <= PreviewLength ? 0 : Math.Clamp(column - PreviewLead, 0, line.Length - PreviewLength);
            var previewLength = Math.Min(line.Length - previewStart, PreviewLength);
            var preview = snapshot.GetText(new TextSpan(line.Start + previewStart, previewLength));
            var trimmed = preview.TrimStart();
            var leading = preview.Length - trimmed.Length;
            if (leading > column - previewStart)
                (trimmed, leading) = (preview, 0);
            matches.Add(new FileSearchMatch(line.LineNumber, column, Math.Min(span.Length, line.End - span.Start), trimmed.TrimEnd(), column - previewStart - leading));
        }

        return matches;
    }
}
