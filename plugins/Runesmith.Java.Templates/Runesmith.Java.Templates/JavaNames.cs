using System.Text;

namespace Runesmith.Java.Templates;

/// <summary>Turns a project's name into the Java names, Maven coordinates and file names derived from it.</summary>
internal static class JavaNames
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "assert", "boolean", "break", "byte", "case", "catch", "char", "class", "const", "continue", "default", "do", "double",
        "else", "enum", "extends", "final", "finally", "float", "for", "goto", "if", "implements", "import", "instanceof", "int", "interface",
        "long", "native", "new", "package", "private", "protected", "public", "return", "short", "static", "strictfp", "super", "switch",
        "synchronized", "this", "throw", "throws", "transient", "try", "void", "volatile", "while", "true", "false", "null", "_", "var",
        "yield", "record", "sealed", "permits", "module", "exports", "requires", "open", "opens", "to", "uses", "provides", "with",
        "transitive",
    };

    /// <summary>Gets a Maven artifact id: lower case, with every character other than a letter, digit, dot or dash made a dash.</summary>
    public static string ArtifactId(string name)
    {
        var text = new StringBuilder();
        foreach (var c in name.Trim().ToLowerInvariant())
            text.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' ? c : '-');
        var artifact = text.ToString().Trim('-', '.');
        while (artifact.Contains("--", StringComparison.Ordinal))
            artifact = artifact.Replace("--", "-", StringComparison.Ordinal);
        return artifact.Length == 0 ? "app" : artifact;
    }

    /// <summary>Gets a package name segment: lower-case letters and digits only, never a keyword or starting with a digit.</summary>
    public static string PackageSegment(string text)
    {
        var segment = new string([.. text.ToLowerInvariant().Where(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')]);
        if (segment.Length == 0)
            return "app";
        if (char.IsAsciiDigit(segment[0]))
            segment = "_" + segment;
        return Keywords.Contains(segment) ? segment + "_" : segment;
    }

    /// <summary>Gets a valid package name from a dotted name, fixing each segment.</summary>
    public static string Package(string dotted) =>
        string.Join('.', dotted.Split('.', StringSplitOptions.RemoveEmptyEntries).Select(PackageSegment).DefaultIfEmpty("app"));

    /// <summary>Gets a class name in Pascal case, such as <c>MyLibrary</c> for "my-library".</summary>
    public static string ClassName(string name)
    {
        var text = new StringBuilder();
        var upper = true;
        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                text.Append(upper ? char.ToUpperInvariant(c) : c);
                upper = false;
            }
            else
            {
                upper = true;
            }
        }

        var result = text.ToString();
        if (result.Length == 0)
            return "App";
        return char.IsAsciiDigit(result[0]) ? "App" + result : result;
    }

    /// <summary>Escapes text for a Java string literal.</summary>
    public static string JavaString(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>Escapes text for XML content.</summary>
    public static string Xml(string text) => System.Security.SecurityElement.Escape(text) ?? "";
}
