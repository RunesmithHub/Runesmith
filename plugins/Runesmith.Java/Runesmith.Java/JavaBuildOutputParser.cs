using System.Globalization;
using System.Text.RegularExpressions;
using Runesmith.Sdk.Build;
using Runesmith.Text;

namespace Runesmith.Java;

/// <summary>Reads the errors and warnings of Java builds: javac's <c>File.java:12: error: message</c>, as Gradle prints them, and Maven's
/// <c>[ERROR] /path/File.java:[12,5] message</c>.</summary>
public static partial class JavaBuildOutputParser
{
    /// <summary>The source diagnostics from a build carry.</summary>
    public const string Source = "build";

    /// <summary>Parses one line of build output, or returns null when it is not a diagnostic.</summary>
    /// <param name="rootPath">The folder being built; relative paths are resolved against it.</param>
    public static Diagnostic? Parse(string line, string rootPath)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (Maven().Match(line) is { Success: true } maven)
        {
            var start = new TextPosition(Number(maven, "line"), Number(maven, "column"));
            return new Diagnostic(FullPath(maven.Groups["path"].Value, rootPath), start, null, Severity(maven.Groups["severity"].Value),
                maven.Groups["message"].Value.Trim(), Source);
        }

        if (Javac().Match(line) is { Success: true } javac)
        {
            var start = new TextPosition(Number(javac, "line"), 0);
            return new Diagnostic(FullPath(javac.Groups["path"].Value, rootPath), start, null, Severity(javac.Groups["severity"].Value),
                javac.Groups["message"].Value.Trim(), Source);
        }

        return null;
    }

    private static DiagnosticSeverity Severity(string text) =>
        text.Equals("error", StringComparison.OrdinalIgnoreCase) ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning;

    private static string FullPath(string path, string rootPath) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(rootPath, path.Trim()));

    // Build tools count lines and columns from 1; a missing column means the start of the line.
    private static int Number(Match match, string group) =>
        match.Groups[group].Success ? Math.Max(0, int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture) - 1) : 0;

    [GeneratedRegex(@"^\s*\[(?<severity>ERROR|WARNING)\]\s+(?<path>.+?\.java):\[(?<line>\d+)(,(?<column>\d+))?\]\s*(?<message>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Maven();

    [GeneratedRegex(@"^(?<path>[^\s\[].*?\.java):(?<line>\d+):\s*(?<severity>error|warning):\s*(?<message>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Javac();
}
