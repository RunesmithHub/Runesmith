namespace Runesmith.Shell.Editors.Custom;

/// <summary>Matches file names against patterns such as <c>*.png</c>, where <c>*</c> is any run of characters and <c>?</c> one character.</summary>
internal static class FilePatterns
{
    public static bool Matches(string pattern, string fileName) => Matches(pattern.AsSpan(), fileName.AsSpan());

    /// <summary>Gets the pattern a user's choice of default editor is kept under: <c>*.ext</c> for a file with an extension, its name
    /// otherwise.</summary>
    public static string KeyOf(string filePath)
    {
        var name = Path.GetFileName(filePath);
        var extension = Path.GetExtension(name);
        return extension.Length > 1 && extension.Length < name.Length ? "*" + extension.ToLowerInvariant() : name;
    }

    private static bool Matches(ReadOnlySpan<char> pattern, ReadOnlySpan<char> name)
    {
        int p = 0, n = 0, star = -1, resume = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(name[n])))
            {
                p++;
                n++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = n;
            }
            else if (star >= 0)
            {
                p = star + 1;
                n = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;
        return p == pattern.Length;
    }
}
