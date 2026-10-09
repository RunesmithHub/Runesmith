namespace Runesmith.Languages.Java.Projects;

/// <summary>How a Java project is built.</summary>
public enum JavaProjectKind
{
    Maven,
    Gradle,

    /// <summary>A folder of Java files without a build file.</summary>
    Folder,
}

/// <summary>A Java project: where its sources are, which Java release it targets and which libraries it compiles against.</summary>
/// <param name="Name">The project's name, such as its artifact id or Gradle project name.</param>
/// <param name="RootPath">The folder that holds the project's build file.</param>
/// <param name="SourceRoots">The folders whose packages hold the project's sources, main and test.</param>
/// <param name="Release">The Java release the project targets, 8 to 25.</param>
/// <param name="PreviewEnabled">Whether the project enables the preview features of its release.</param>
/// <param name="ClassPathJars">The JARs of its libraries, direct and transitive, in class path order.</param>
/// <param name="ProjectReferences">The names of the other projects of the same build it depends on.</param>
public sealed record JavaProject(
    string Name,
    string RootPath,
    JavaProjectKind Kind,
    IReadOnlyList<string> SourceRoots,
    int Release,
    bool PreviewEnabled,
    IReadOnlyList<string> ClassPathJars,
    IReadOnlyList<string> ProjectReferences);

/// <summary>Where project loading looks for libraries, and what it assumes when a project does not say.</summary>
public sealed record JavaProjectLoaderOptions
{
    /// <summary>Gets the local Maven repository, by default <c>~/.m2/repository</c>.</summary>
    public string MavenRepository { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".m2", "repository");

    /// <summary>Gets the Gradle module cache, by default <c>caches/modules-2/files-2.1</c> under <c>GRADLE_USER_HOME</c> or <c>~/.gradle</c>.</summary>
    public string GradleCache { get; init; } = Path.Combine(
        Environment.GetEnvironmentVariable("GRADLE_USER_HOME") is { Length: > 0 } gradleHome
            ? gradleHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gradle"),
        "caches", "modules-2", "files-2.1");

    /// <summary>Gets the release assumed for a project that does not name one, normally the newest the JDK in use has.</summary>
    public int DefaultRelease { get; init; } = 25;

    /// <summary>Gets the most libraries resolved for one project, so a broken build file cannot make loading run away.</summary>
    public int MaximumLibraries { get; init; } = 2000;
}

/// <summary>Turns the release texts of build files into a release number: <c>1.8</c> and <c>8</c> are 8, <c>17</c> is 17.</summary>
internal static class JavaRelease
{
    public static int? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var value = text.Trim().Trim('"', '\'');
        if (value.StartsWith("VERSION_", StringComparison.Ordinal))
            value = value["VERSION_".Length..].Replace('_', '.');
        if (value.StartsWith("1.", StringComparison.Ordinal))
            value = value[2..];

        var end = 0;
        while (end < value.Length && char.IsAsciiDigit(value[end]))
            end++;
        return end > 0 && int.TryParse(value.AsSpan(0, end), System.Globalization.CultureInfo.InvariantCulture, out var release) && release is >= 1 and <= 99
            ? release
            : null;
    }
}
