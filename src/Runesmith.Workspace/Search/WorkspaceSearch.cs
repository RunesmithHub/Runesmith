using System.Composition;
using System.Runtime.CompilerServices;
using Runesmith.Sdk.Workspace;
using Runesmith.Text;
using Runesmith.Workspace.Files;

namespace Runesmith.Workspace.Search;

/// <summary>Plugins' search of the open folder, with the Search panel's engine.</summary>
[Export(typeof(IWorkspaceSearch))]
[Shared]
[method: ImportingConstructor]
public sealed class WorkspaceSearch(FindInFiles engine, IWorkspace workspace) : IWorkspaceSearch
{
    public IAsyncEnumerable<WorkspaceSearchResult> SearchAsync(WorkspaceSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        FindInFiles.Validate(query.Text, query.Options);
        return Search(query, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> FindFilesAsync(string glob, int maxResults = 10_000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(glob);
        if (workspace.RootPath is not { } root || maxResults <= 0)
            return [];

        var matcher = GlobMatcher.Parse(glob);
        var files = await workspace.GetFilesAsync(cancellationToken).ConfigureAwait(false);
        var found = new List<string>();
        foreach (var file in files)
        {
            if (!matcher.IsEmpty && !matcher.IsMatch(GlobMatcher.ToGlobPath(Path.GetRelativePath(root, file))))
                continue;

            found.Add(file);
            if (found.Count >= maxResults)
                break;
        }

        return found;
    }

    private async IAsyncEnumerable<WorkspaceSearchResult> Search(WorkspaceSearchQuery query, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var open = await UiThread.InvokeAsync(engine.OpenSnapshots).ConfigureAwait(false);
        var results = engine.Search(query.Text, query.Options, Join(query.Include), Join(query.Exclude), open, query.MaxResults, ToMatches,
            (path, matches, limitReached) => new WorkspaceSearchResult(path, matches) { IsLimitReached = limitReached }, cancellationToken);
        await foreach (var result in results.ConfigureAwait(false))
            yield return result;
    }

    private static List<WorkspaceSearchMatch> ToMatches(TextSnapshot snapshot, IReadOnlyList<TextSpan> spans)
    {
        var matches = new List<WorkspaceSearchMatch>(spans.Count);
        var lineNumber = -1;
        var lineText = "";
        foreach (var span in spans)
        {
            var line = snapshot.GetLineFromPosition(span.Start);
            if (line.LineNumber != lineNumber)
                (lineNumber, lineText) = (line.LineNumber, snapshot.GetText(new TextSpan(line.Start, line.Length)));
            matches.Add(new WorkspaceSearchMatch(lineNumber, span.Start - line.Start, Math.Min(span.Length, line.End - span.Start), lineText));
        }

        return matches;
    }

    private static string? Join(IReadOnlyList<string>? globs) => globs is { Count: > 0 } ? string.Join(';', globs) : null;
}
