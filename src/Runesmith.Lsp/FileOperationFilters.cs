using System.Text;
using System.Text.RegularExpressions;
using Runesmith.Lsp.Protocol;

namespace Runesmith.Lsp;

/// <summary>The files and folders a server asked to hear of for each file operation, from the <c>workspace.fileOperations</c> of its
/// capabilities.</summary>
internal sealed class FileOperationFilters
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    private readonly Dictionary<(FileOperationKind Kind, bool Before), List<Filter>> _filters = [];

    public FileOperationFilters(FileOperationOptions? options)
    {
        Add(FileOperationKind.Create, before: true, options?.WillCreate);
        Add(FileOperationKind.Create, before: false, options?.DidCreate);
        Add(FileOperationKind.Rename, before: true, options?.WillRename);
        Add(FileOperationKind.Rename, before: false, options?.DidRename);
        Add(FileOperationKind.Delete, before: true, options?.WillDelete);
        Add(FileOperationKind.Delete, before: false, options?.DidDelete);
    }

    /// <summary>Gets the files and folders the server wants to hear of before or after an operation; a rename matches by its old or its new
    /// path.</summary>
    public IReadOnlyList<FileOperationTarget> Match(FileOperationKind kind, bool before, IReadOnlyList<FileOperationTarget> targets) =>
        _filters.TryGetValue((kind, before), out var filters)
            ? [.. targets.Where(t => filters.Any(f => f.IsMatch(t.Path, t.IsFolder) || (t.NewPath is { } newPath && f.IsMatch(newPath, t.IsFolder))))]
            : [];

    /// <summary>Turns a glob into a regular expression over a whole path with <c>/</c> separators.</summary>
    internal static Regex ToRegex(string glob, bool ignoreCase)
    {
        var pattern = new StringBuilder("^");
        var i = 0;
        AppendGlob(pattern, glob, ref i, nested: false);
        pattern.Append('$');
        return new Regex(pattern.ToString(), RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None), MatchTimeout);
    }

    private void Add(FileOperationKind kind, bool before, FileOperationRegistrationOptions? registration)
    {
        if (registration is not null)
            _filters[(kind, before)] = [.. (registration.Filters ?? []).Select(Filter.Create).OfType<Filter>()];
    }

    // Appends the glob from i to its end or, inside braces, to the ',' or '}' that ends the alternative.
    private static void AppendGlob(StringBuilder pattern, string glob, ref int i, bool nested)
    {
        while (i < glob.Length)
        {
            var c = glob[i];
            if (nested && c is ',' or '}')
                return;

            i++;
            switch (c)
            {
                case '*' when i < glob.Length && glob[i] == '*':
                    i++;
                    var folders = i < glob.Length && glob[i] == '/';
                    if (folders)
                        i++;
                    pattern.Append(folders ? "(?:.*/)?" : ".*");
                    break;
                case '*':
                    pattern.Append("[^/]*");
                    break;
                case '?':
                    pattern.Append("[^/]");
                    break;
                case '{':
                    pattern.Append("(?:");
                    AppendGlob(pattern, glob, ref i, nested: true);
                    while (i < glob.Length && glob[i] == ',')
                    {
                        i++;
                        pattern.Append('|');
                        AppendGlob(pattern, glob, ref i, nested: true);
                    }

                    if (i < glob.Length)
                        i++;
                    pattern.Append(')');
                    break;
                case '[' when ClassEnd(glob, i) is var end and >= 0:
                    var negated = glob[i] == '!';
                    pattern.Append(negated ? "[^/" : "[");
                    foreach (var member in glob[(negated ? i + 1 : i)..end])
                        pattern.Append(member is '\\' or '[' or ']' or '^' ? $"\\{member}" : member.ToString());
                    pattern.Append(']');
                    i = end + 1;
                    break;
                default:
                    pattern.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
    }

    // Finds the ']' that closes a class starting at i, after at least one member, or -1 when there is none.
    private static int ClassEnd(string glob, int i)
    {
        var first = i < glob.Length && glob[i] == '!' ? i + 1 : i;
        return first < glob.Length ? glob.IndexOf(']', first + 1) : -1;
    }

    private sealed record Filter(Regex Glob, string? Matches)
    {
        public static Filter? Create(FileOperationFilter filter)
        {
            // Runesmith's files are local, so filters for other schemes, such as untitled, never match them.
            if (filter.Pattern?.Glob is not { } glob || (filter.Scheme is { } scheme && !string.Equals(scheme, "file", StringComparison.OrdinalIgnoreCase)))
                return null;
            return new Filter(ToRegex(glob, filter.Pattern.Options?.IgnoreCase == true), filter.Pattern.Matches);
        }

        public bool IsMatch(string path, bool? isFolder)
        {
            if (isFolder is { } folder && Matches is FileOperationPatternKind.File or FileOperationPatternKind.Folder
                && folder != (Matches == FileOperationPatternKind.Folder))
            {
                return false;
            }

            var slashed = Path.DirectorySeparatorChar == '/' ? path : path.Replace(Path.DirectorySeparatorChar, '/');
            try
            {
                return Glob.IsMatch(slashed) || (isFolder == true && Glob.IsMatch(slashed + "/"));
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }
    }
}
