namespace Runesmith.Languages.Java.Projects;

/// <summary>A library's Maven coordinates.</summary>
public sealed record ArtifactCoordinate(string GroupId, string ArtifactId, string Version, string? Classifier = null)
{
    /// <summary>Gets the key Maven mediates versions by: group and artifact.</summary>
    public string Key => GroupId + ":" + ArtifactId;

    public override string ToString() => $"{GroupId}:{ArtifactId}:{Version}" + (Classifier is null ? "" : ":" + Classifier);
}

/// <summary>A place on disk where downloaded libraries are: the local Maven repository or the Gradle cache.</summary>
internal interface IArtifactRepository
{
    string? FindPom(ArtifactCoordinate coordinate);

    string? FindJar(ArtifactCoordinate coordinate);
}

/// <summary>The local Maven repository: <c>group/as/folders/artifact/version/artifact-version.jar</c>.</summary>
internal sealed class MavenLocalRepository(string root) : IArtifactRepository
{
    public string? FindPom(ArtifactCoordinate coordinate) => Existing(Folder(coordinate), $"{coordinate.ArtifactId}-{coordinate.Version}.pom");

    public string? FindJar(ArtifactCoordinate coordinate) =>
        Existing(Folder(coordinate), $"{coordinate.ArtifactId}-{coordinate.Version}{(coordinate.Classifier is null ? "" : "-" + coordinate.Classifier)}.jar");

    private string Folder(ArtifactCoordinate coordinate) =>
        Path.Combine([root, .. coordinate.GroupId.Split('.'), coordinate.ArtifactId, coordinate.Version]);

    private static string? Existing(string folder, string file)
    {
        var path = Path.Combine(folder, file);
        return File.Exists(path) ? path : null;
    }
}

/// <summary>Gradle's module cache: <c>group/artifact/version/hash/artifact-version.jar</c>, one folder per file hash.</summary>
internal sealed class GradleCacheRepository(string root) : IArtifactRepository
{
    public string? FindPom(ArtifactCoordinate coordinate) => Find(coordinate, $"{coordinate.ArtifactId}-{coordinate.Version}.pom");

    public string? FindJar(ArtifactCoordinate coordinate) =>
        Find(coordinate, $"{coordinate.ArtifactId}-{coordinate.Version}{(coordinate.Classifier is null ? "" : "-" + coordinate.Classifier)}.jar");

    private string? Find(ArtifactCoordinate coordinate, string file)
    {
        var folder = Path.Combine(root, coordinate.GroupId, coordinate.ArtifactId, coordinate.Version);
        if (!Directory.Exists(folder))
            return null;

        try
        {
            foreach (var hash in Directory.EnumerateDirectories(folder))
            {
                var path = Path.Combine(hash, file);
                if (File.Exists(path))
                    return path;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }
}
