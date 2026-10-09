namespace Runesmith.Languages.Java.Projects;

/// <summary>Finds the Java projects of a folder from its build files: Maven, Gradle, or none, when the folder itself is the project.</summary>
/// <remarks>It only reads files: it never runs a build tool and never downloads, so libraries that are not in the local Maven repository or
/// the Gradle cache are left out.</remarks>
public static class JavaProjectLoader
{
    /// <summary>Loads the projects of a folder.</summary>
    public static IReadOnlyList<JavaProject> Load(string rootPath, JavaProjectLoaderOptions? options = null)
    {
        options ??= new JavaProjectLoaderOptions();
        var root = Path.GetFullPath(rootPath);
        if (File.Exists(Path.Combine(root, "pom.xml")))
        {
            var maven = MavenProjectReader.Read(root, options);
            if (maven.Count > 0)
                return maven;
        }

        if (GradleProjectReader.IsGradleBuild(root))
        {
            var gradle = GradleProjectReader.Read(root, options);
            if (gradle.Count > 0)
                return gradle;
        }

        return [new JavaProject(Path.GetFileName(root), root, JavaProjectKind.Folder, [root], options.DefaultRelease, false, [], [])];
    }
}
