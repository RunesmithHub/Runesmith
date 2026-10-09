using System.IO.Compression;

namespace Runesmith.Languages.Java.Syntax.Tests;

/// <summary>Finds the JDK's <c>src.zip</c>, through <c>JAVA_HOME</c> or <c>java</c> on the <c>PATH</c>.</summary>
internal static class JdkSources
{
    private static readonly Lazy<string?> ZipPath = new(Find);

    public static string? Path => ZipPath.Value;

    /// <summary>Gets every Java file of <c>src.zip</c>, read into memory.</summary>
    public static List<(string Name, string Text)> ReadAll()
    {
        using var zip = ZipFile.OpenRead(Path!);
        var files = new List<(string, string)>();
        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.EndsWith(".java", StringComparison.Ordinal))
                continue;

            using var reader = new StreamReader(entry.Open());
            files.Add((entry.FullName, reader.ReadToEnd()));
        }

        return files;
    }

    private static string? Find()
    {
        foreach (var home in Homes())
        {
            foreach (var candidate in new[] { System.IO.Path.Combine(home, "lib", "src.zip"), System.IO.Path.Combine(home, "src.zip") })
            {
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> Homes()
    {
        if (Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } javaHome)
            yield return javaHome;

        var executable = OperatingSystem.IsWindows() ? "java.exe" : "java";
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator))
        {
            var java = System.IO.Path.Combine(folder, executable);
            if (!File.Exists(java))
                continue;

            var resolved = new FileInfo(java).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? java;
            if (System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(resolved)) is { } home)
                yield return home;
        }
    }
}
