using System.Composition;
using System.Globalization;
using System.Text.RegularExpressions;
using Runesmith.Sdk.Running;

namespace Runesmith.Shell.Running;

/// <summary>Finds file locations in console lines: the forms every tool writes, <c>path:line</c>, <c>path:line:column</c> and
/// <c>path(line,column)</c>, and the ones plugins know, such as the stack frames of their languages.</summary>
public static partial class ConsoleLinks
{
    /// <summary>Gets or sets the plugins' providers, asked before Runesmith's own patterns.</summary>
    internal static IReadOnlyList<IConsoleLinkProvider> Providers { get; set; } = [];

    /// <summary>Finds the locations a line names, in the order they appear.</summary>
    public static IReadOnlyList<ConsoleLink> Find(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Length < 4)
            return [];

        var found = new List<ConsoleLink>();
        foreach (var provider in Providers)
        {
            try
            {
                foreach (var link in provider.FindLinks(line))
                    Add(found, link, line.Length);
            }
#pragma warning disable CA1031 // A plugin that fails on a line leaves the line to the other providers.
            catch (Exception)
#pragma warning restore CA1031
            {
            }
        }

        if (line.Contains('(', StringComparison.Ordinal) || line.Contains(':', StringComparison.Ordinal))
        {
            foreach (Match match in PathLineAndColumn().Matches(line))
                Add(found, new ConsoleLink(match.Index, match.Length, match.Groups["path"].Value.Trim(), Number(match, "line"), Number(match, "column")), line.Length);

            foreach (Match match in PathAndLine().Matches(line))
                Add(found, new ConsoleLink(match.Index, match.Length, match.Groups["path"].Value, Number(match, "line"), Number(match, "column")), line.Length);
        }

        found.Sort((a, b) => a.Start.CompareTo(b.Start));
        return found;
    }

    private static void Add(List<ConsoleLink> found, ConsoleLink link, int lineLength)
    {
        if (link.Line > 0 && link.Start >= 0 && link.Length > 0 && link.Start + link.Length <= lineLength
            && !found.Any(f => link.Start < f.Start + f.Length && f.Start < link.Start + link.Length))
        {
            found.Add(link);
        }
    }

    private static int Number(Match match, string group) =>
        match.Groups[group].Success && int.TryParse(match.Groups[group].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : 0;

    // A file name with an extension of up to eight characters, whatever the language: a link shows only when the file exists.
    private const string FilePath = @"(?<path>(?:[A-Za-z]:)?[\w.\\/-]*[\w-]+\.[A-Za-z][\w]{0,7})";

    [GeneratedRegex(FilePath + @"\((?<line>\d+)(?:,(?<column>\d+))?(?:,\d+,\d+)?\)", RegexOptions.CultureInvariant)]
    private static partial Regex PathLineAndColumn();

    [GeneratedRegex(FilePath + @":(?<line>\d+)(?::(?<column>\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex PathAndLine();
}

/// <summary>Hands the plugins' console link providers to <see cref="ConsoleLinks"/> when Runesmith starts.</summary>
[Export]
[Shared]
public sealed class ConsoleLinkProviders
{
    [ImportingConstructor]
    public ConsoleLinkProviders([ImportMany] IEnumerable<IConsoleLinkProvider> providers) => ConsoleLinks.Providers = [.. providers];
}

/// <summary>Turns the paths of console links into files of the open folder, remembering what it found.</summary>
/// <param name="roots">The folders relative paths are tried against, such as the run's working directory and the open folder.</param>
/// <param name="files">Gets the open folder's files, for links whose path is the end of a file's path, or null while they load.</param>
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
