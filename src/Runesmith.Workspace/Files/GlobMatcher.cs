using System.Text;
using System.Text.RegularExpressions;

namespace Runesmith.Workspace.Files;

/// <summary>Matches relative paths, with <c>/</c> separators, against semicolon-separated globs such as <c>**/bin;*.tmp</c>.</summary>
/// <remarks><c>**</c> matches any number of folders, <c>*</c> any part of a name and <c>?</c> one character. A glob without a <c>/</c> matches a
/// name at any depth, so <c>bin</c> means <c>**/bin</c>.</remarks>
internal sealed class GlobMatcher
{
    private readonly Regex? _regex;

    private GlobMatcher(Regex? regex) => _regex = regex;

    public static GlobMatcher Empty { get; } = new(null);

    public bool IsEmpty => _regex is null;

    public static GlobMatcher Parse(string? globs)
    {
        var parts = (globs ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return Empty;

        var pattern = string.Join('|', parts.Select(ToRegex));
        var options = RegexOptions.CultureInvariant | (PathComparison.Comparison == StringComparison.Ordinal ? RegexOptions.None : RegexOptions.IgnoreCase);
        return new GlobMatcher(new Regex($"^(?:{pattern})$", options | RegexOptions.Compiled));
    }

    /// <summary>Whether the path itself matches.</summary>
    public bool IsMatch(string relativePath) => _regex?.IsMatch(relativePath) ?? false;

    /// <summary>Whether the path or one of its folders matches, which is what excluding a folder means for what is inside it.</summary>
    public bool IsMatchOrInside(string relativePath)
    {
        if (_regex is null)
            return false;

        for (var end = relativePath.IndexOf('/'); end >= 0; end = relativePath.IndexOf('/', end + 1))
        {
            if (_regex.IsMatch(relativePath.AsSpan(0, end)))
                return true;
        }

        return _regex.IsMatch(relativePath);
    }

    /// <summary>Turns a relative path with system separators into the form globs are matched against.</summary>
    public static string ToGlobPath(string relativePath) =>
        Path.DirectorySeparatorChar == '/' ? relativePath : relativePath.Replace(Path.DirectorySeparatorChar, '/');

    private static string ToRegex(string glob)
    {
        glob = glob.Replace('\\', '/').TrimStart('/');
        if (glob.StartsWith("./", StringComparison.Ordinal))
            glob = glob[2..];
        if (!glob.Contains('/'))
            glob = "**/" + glob;
        glob = glob.TrimEnd('/');

        var regex = new StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                var slashAfter = i + 2 < glob.Length && glob[i + 2] == '/';
                regex.Append(slashAfter ? "(?:.*/)?" : ".*");
                i += slashAfter ? 2 : 1;
            }
            else if (c == '*')
            {
                regex.Append("[^/]*");
            }
            else if (c == '?')
            {
                regex.Append("[^/]");
            }
            else
            {
                regex.Append(Regex.Escape(c.ToString()));
            }
        }

        return regex.ToString();
    }
}
