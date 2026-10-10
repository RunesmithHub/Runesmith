using System.Collections.ObjectModel;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.Shell.Navigation;

/// <summary>A place in the References panel: where it is, and its line's text with the reference marked.</summary>
/// <param name="Preview">The line's text without its indentation.</param>
/// <param name="MatchStart">Where the reference starts in <paramref name="Preview"/>.</param>
public sealed record ReferenceItem(DocumentLocation Location, string Preview, int MatchStart, int MatchLength)
{
    public int Line => Location.Start.Line;
}

/// <summary>The places in one file.</summary>
/// <param name="Folder">The file's folder, relative to the open folder when it is inside.</param>
public sealed record ReferenceGroup(string FilePath, string Name, string Folder, IReadOnlyList<ReferenceItem> Items);

/// <summary>What the References panel lists: the places of one search, by file, with their lines.</summary>
public sealed class ReferencesModel
{
    /// <summary>The most lines read for previews; further places show without one.</summary>
    private const int MaxPreviews = 5000;

    public string Title { get; private set; } = "";

    /// <summary>Gets a line under the title, such as "12 references in 3 files".</summary>
    public string Summary { get; private set; } = "";

    public ObservableCollection<ReferenceGroup> Groups { get; } = [];

    /// <summary>Gets the number of places.</summary>
    public int Count { get; private set; }

    /// <summary>Raised after the model shows a new search.</summary>
    public event EventHandler? Changed;

    /// <summary>Shows the places of a search, reading their lines from the open documents or the files.</summary>
    /// <param name="title">What was searched for, such as "References to Main".</param>
    /// <param name="noun">The word for one place, such as "reference".</param>
    /// <param name="openText">Gets the text of an open document, or null for a file that is not open; called on the calling thread.</param>
    public async Task ShowAsync(string title, string noun, IReadOnlyList<DocumentLocation> locations, Func<string, TextSnapshot?> openText, string? root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentNullException.ThrowIfNull(openText);
        var open = locations.Select(l => l.FilePath).Distinct(StringComparer.Ordinal).ToDictionary(path => path, openText, StringComparer.Ordinal);
        var groups = await Task.Run(() => Group(locations, open, root), cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return;

        Title = title;
        Count = locations.Count;
        var places = Count == 1 ? $"1 {noun}" : $"{Count:N0} {noun}s";
        Summary = groups.Count == 1 ? $"{places} in 1 file" : $"{places} in {groups.Count:N0} files";
        Groups.Clear();
        foreach (var group in groups)
            Groups.Add(group);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Clears the list.</summary>
    public void Clear()
    {
        Title = "";
        Summary = "";
        Count = 0;
        Groups.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gets the place after or before one, going round at the ends, in the order the panel lists them.</summary>
    public ReferenceItem? Step(ReferenceItem? from, int direction)
    {
        var all = Groups.SelectMany(g => g.Items).ToList();
        if (all.Count == 0)
            return null;

        var index = from is null ? -1 : all.IndexOf(from);
        return index < 0 ? all[direction > 0 ? 0 : ^1] : all[((index + direction) % all.Count + all.Count) % all.Count];
    }

    private static List<ReferenceGroup> Group(IReadOnlyList<DocumentLocation> locations, Dictionary<string, TextSnapshot?> open, string? root)
    {
        var groups = new List<ReferenceGroup>();
        var previews = 0;
        foreach (var file in locations.GroupBy(l => l.FilePath, StringComparer.Ordinal).OrderBy(g => Relative(g.Key, root), StringComparer.OrdinalIgnoreCase))
        {
            var lines = previews < MaxPreviews ? Lines(file.Key, open.GetValueOrDefault(file.Key)) : null;
            var items = new List<ReferenceItem>();
            foreach (var location in file.OrderBy(l => l.Start))
            {
                previews++;
                items.Add(Item(location, lines?.Invoke(location.Start.Line)));
            }

            var relative = Relative(file.Key, root);
            groups.Add(new ReferenceGroup(file.Key, Path.GetFileName(file.Key), Path.GetDirectoryName(relative) ?? "", items));
        }

        return groups;
    }

    private static ReferenceItem Item(DocumentLocation location, string? line)
    {
        if (line is null)
            return new ReferenceItem(location, "", 0, 0);

        var indent = line.Length - line.TrimStart().Length;
        var text = line.Trim();
        var start = Math.Clamp(location.Start.Column - indent, 0, text.Length);
        var end = location.End is { } last && last.Line == location.Start.Line ? Math.Clamp(last.Column - indent, start, text.Length) : start;
        return new ReferenceItem(location, text, start, end - start);
    }

    // Gets a line of a file by its number, or null past its end.
    private static Func<int, string?>? Lines(string path, TextSnapshot? snapshot)
    {
        if (snapshot is not null)
            return line => line < snapshot.LineCount ? snapshot.GetLineText(line) : null;

        try
        {
            var lines = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n');
            return line => line < lines.Length ? lines[line] : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Relative(string path, string? root)
    {
        if (root is null)
            return path;

        var relative = Path.GetRelativePath(root, path);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
    }
}
