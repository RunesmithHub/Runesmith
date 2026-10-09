using System.Globalization;
using System.Text.RegularExpressions;

namespace Runesmith.Shell.Running;

/// <summary>A place in a source file that a console line names.</summary>
/// <param name="Start">Where the link starts in the line.</param>
/// <param name="Length">The link's length.</param>
/// <param name="Path">The path as the line writes it, or for a Java stack frame the source's path relative to a source root, such as
/// <c>com/example/Main.java</c>.</param>
/// <param name="Line">The line, counted from 1.</param>
/// <param name="Column">The column, counted from 1, or 0 when the line names none.</param>
/// <param name="IsSourceRelative">Whether <paramref name="Path"/> is relative to a source root rather than to a folder.</param>
public sealed record ConsoleLink(int Start, int Length, string Path, int Line, int Column, bool IsSourceRelative = false);

/// <summary>Finds file locations in console lines: compiler messages such as <c>File.cs(12,5)</c> and <c>File.java:12</c>, .NET stack frames
/// such as <c>in /src/File.cs:line 12</c>, and Java stack frames such as <c>at com.example.Main.run(Main.java:12)</c>.</summary>
public static partial class ConsoleLinks
{
    private const string Extensions = "cs|fs|vb|java|kt|kts|groovy|scala|xaml|axaml|razor|cshtml|csproj|fsproj|props|targets|json|xml|gradle|py|js|ts|go|rs|c|h|cpp|hpp";

    /// <summary>Finds the locations a line names, in the order they appear.</summary>
    public static IReadOnlyList<ConsoleLink> Find(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Length < 4 || (!line.Contains('(', StringComparison.Ordinal) && !line.Contains(':', StringComparison.Ordinal)))
            return [];

        var found = new List<ConsoleLink>();
        foreach (Match match in JavaFrame().Matches(line))
        {
            var file = match.Groups["file"];
            var frame = match.Groups["frame"].Value;
            var slash = frame.LastIndexOf('/');
            if (slash >= 0)
                frame = frame[(slash + 1)..];
            var parts = frame.Split('.');
            var package = parts.Length > 2 ? string.Join('/', parts[..^2]) + "/" : "";
            Add(found, new ConsoleLink(file.Index, match.Groups["end"].Index - file.Index, package + file.Value, Number(match, "line"), 0, IsSourceRelative: true));
        }

        foreach (Match match in DotnetFrame().Matches(line))
        {
            var path = match.Groups["path"];
            Add(found, new ConsoleLink(path.Index, match.Index + match.Length - path.Index, path.Value, Number(match, "line"), 0));
        }

        foreach (Match match in Compiler().Matches(line))
            Add(found, new ConsoleLink(match.Index, match.Length, match.Groups["path"].Value.Trim(), Number(match, "line"), Number(match, "column")));

        foreach (Match match in PathAndLine().Matches(line))
            Add(found, new ConsoleLink(match.Index, match.Length, match.Groups["path"].Value, Number(match, "line"), Number(match, "column")));

        found.Sort((a, b) => a.Start.CompareTo(b.Start));
        return found;
    }

    private static void Add(List<ConsoleLink> found, ConsoleLink link)
    {
        if (link.Line > 0 && !found.Any(f => link.Start < f.Start + f.Length && f.Start < link.Start + link.Length))
            found.Add(link);
    }

    private static int Number(Match match, string group) =>
        match.Groups[group].Success && int.TryParse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    [GeneratedRegex(@"\bat\s+(?<frame>[\w$.<>/-]+)\((?<file>[\w$-]+\.(?:java|kt|groovy|scala)):(?<line>\d+)(?<end>\))", RegexOptions.CultureInvariant)]
    private static partial Regex JavaFrame();

    [GeneratedRegex(@"\bin (?<path>(?:[A-Za-z]:)?[\\/][^:*?""<>|]+?):line (?<line>\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex DotnetFrame();

    [GeneratedRegex(@"(?<path>(?:[A-Za-z]:)?[\w.\\/-]*[\w-]+\.(?:" + Extensions + @"))\((?<line>\d+)(?:,(?<column>\d+))?(?:,\d+,\d+)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex Compiler();

    [GeneratedRegex(@"(?<path>(?:[A-Za-z]:)?[\w.\\/-]*[\w-]+\.(?:" + Extensions + @")):(?<line>\d+)(?::(?<column>\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex PathAndLine();
}

/// <summary>Turns the paths of console links into files of the open folder, remembering what it found.</summary>
/// <param name="roots">The folders relative paths are tried against, such as the run's working directory and the open folder.</param>
/// <param name="files">Gets the open folder's files, for Java stack frames, whose paths are relative to a source root, or null while they load.</param>
public sealed class ConsoleLinkResolver(Func<IReadOnlyList<string>> roots, Func<IReadOnlyList<string>?> files)
{
    private readonly Dictionary<(string, bool), string?> cache = [];

    /// <summary>Gets the full path of the file a link names, or null when there is none.</summary>
    public string? Resolve(ConsoleLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        var key = (link.Path, link.IsSourceRelative);
        lock (cache)
        {
            if (cache.TryGetValue(key, out var cached))
                return cached;
        }

        var known = link.IsSourceRelative ? files() : [];
        if (known is null)
            return null;

        var found = link.IsSourceRelative ? FindSource(link.Path, known) : FindFile(link.Path);
        lock (cache)
            cache[key] = found;
        return found;
    }

    private string? FindFile(string path)
    {
        try
        {
            if (Path.IsPathRooted(path))
                return File.Exists(path) ? Path.GetFullPath(path) : null;

            foreach (var root in roots())
            {
                var candidate = Path.GetFullPath(Path.Combine(root, path));
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
        }

        return null;
    }

    private static string? FindSource(string relativePath, IReadOnlyList<string> files)
    {
        var suffix = Path.DirectorySeparatorChar + relativePath.Replace('/', Path.DirectorySeparatorChar);
        var name = Path.GetFileName(relativePath);
        string? byName = null;
        foreach (var file in files)
        {
            if (file.EndsWith(suffix, StringComparison.Ordinal))
                return file;
            if (byName is null && string.Equals(Path.GetFileName(file), name, StringComparison.Ordinal) && !relativePath.Contains('/', StringComparison.Ordinal))
                byName = file;
        }

        return byName;
    }
}
