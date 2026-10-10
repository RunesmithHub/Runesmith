using Runesmith.Text;

namespace Runesmith.Sdk.Workspace;

/// <summary>What <see cref="IWorkspaceSearch.SearchAsync"/> looks for and where.</summary>
/// <param name="Text">The text to find, or a .NET regular expression with <see cref="TextSearchOptions.Regex"/>.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record WorkspaceSearchQuery(string Text)
{
    /// <summary>Gets whether case must match, whole words only and whether <see cref="Text"/> is a regular expression.</summary>
    public TextSearchOptions Options { get; init; }

    /// <summary>Gets globs relative to the open folder that a file must match, such as <c>*.cs</c> or <c>src/**</c>; empty for every
    /// file.</summary>
    public IReadOnlyList<string> Include { get; init; } = [];

    /// <summary>Gets globs of files and folders to leave out, on top of <c>files.exclude</c>.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = [];

    /// <summary>Gets how many matches the search finds at most; it stops there.</summary>
    public int MaxResults { get; init; } = 10_000;
}

/// <summary>One match of a search.</summary>
/// <param name="Line">The line, counted from zero.</param>
/// <param name="Column">Where the match starts in <paramref name="LineText"/>, counted from zero.</param>
/// <param name="Length">The match's length; a match that goes past the end of the line ends there.</param>
/// <param name="LineText">The whole line, without its line break.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record WorkspaceSearchMatch(int Line, int Column, int Length, string LineText);

/// <summary>The matches in one file.</summary>
/// <param name="FilePath">The file's full path.</param>
/// <param name="Matches">The matches, in the order they appear in the file.</param>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record WorkspaceSearchResult(string FilePath, IReadOnlyList<WorkspaceSearchMatch> Matches)
{
    /// <summary>Gets whether the search stopped after this file because it found <see cref="WorkspaceSearchQuery.MaxResults"/>
    /// matches.</summary>
    public bool IsLimitReached { get; init; }
}

/// <summary>Searches the files of the open folder the way the <b>Search</b> panel does.</summary>
/// <remarks>Added in plugin API 0.1.2. Its members can be called from any thread. Searching reads only files inside the open folder, so it
/// needs no capability.</remarks>
public interface IWorkspaceSearch
{
    /// <summary>Finds text in every file of the open folder that <c>files.exclude</c> does not leave out, and returns the matches of each file
    /// as soon as the file is searched, so files arrive in no set order. Open documents are searched as they are in the editor, with
    /// unsaved changes; files larger than 10 MB and binary files are skipped.</summary>
    /// <exception cref="ArgumentException">The query is an invalid regular expression.</exception>
    IAsyncEnumerable<WorkspaceSearchResult> SearchAsync(WorkspaceSearchQuery query, CancellationToken cancellationToken = default);

    /// <summary>Finds the files of the open folder whose paths match a glob, from the list Runesmith keeps up to date, so it does not read the
    /// disk; the paths are full paths, sorted.</summary>
    /// <param name="glob">One or more globs relative to the open folder, separated by semicolons, such as <c>**/*.csproj</c> or
    /// <c>*.json;*.yaml</c>.</param>
    /// <param name="maxResults">How many paths to return at most.</param>
    Task<IReadOnlyList<string>> FindFilesAsync(string glob, int maxResults = 10_000, CancellationToken cancellationToken = default);
}
