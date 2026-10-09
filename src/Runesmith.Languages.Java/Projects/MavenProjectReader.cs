namespace Runesmith.Languages.Java.Projects;

/// <summary>Reads a Maven build: the project in a folder's <c>pom.xml</c> and, recursively, its modules.</summary>
internal static class MavenProjectReader
{
    public static IReadOnlyList<JavaProject> Read(string rootPath, JavaProjectLoaderOptions options)
    {
        IArtifactRepository[] repositories = [new MavenLocalRepository(options.MavenRepository), new GradleCacheRepository(options.GradleCache)];
        var resolver = new MavenResolver(repositories, options.MaximumLibraries);
        var poms = new List<(string Folder, EffectivePom Pom)>();
        Collect(Path.GetFullPath(rootPath), resolver, poms, new HashSet<string>(StringComparer.Ordinal));

        var reactor = poms.ToDictionary(p => p.Pom.Key, p => p.Pom.ArtifactId, StringComparer.Ordinal);
        var projects = new List<JavaProject>();
        foreach (var (folder, pom) in poms)
        {
            if (pom.Packaging == "pom" && pom.Modules.Count > 0 && !HasSources(folder, pom))
                continue;

            var references = new List<string>();
            var jars = resolver.Resolve(pom, reactor.ContainsKey, references);
            projects.Add(new JavaProject(
                pom.ArtifactId.Length > 0 ? pom.ArtifactId : Path.GetFileName(folder),
                folder,
                JavaProjectKind.Maven,
                SourceRoots(folder, pom),
                JavaRelease.Parse(pom.CompilerRelease) ?? options.DefaultRelease,
                pom.CompilerEnablesPreview,
                jars,
                [.. references.Select(key => reactor[key])]));
        }

        return projects;
    }

    private static void Collect(string folder, MavenResolver resolver, List<(string, EffectivePom)> poms, HashSet<string> visited)
    {
        var pomPath = Path.Combine(folder, "pom.xml");
        if (!visited.Add(folder) || !File.Exists(pomPath))
            return;

        EffectivePom pom;
        try
        {
            pom = resolver.Load(pomPath);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return;
        }

        poms.Add((folder, pom));
        foreach (var module in pom.Modules)
            Collect(Path.GetFullPath(Path.Combine(folder, module)), resolver, poms, visited);
    }

    private static bool HasSources(string folder, EffectivePom pom) => SourceRoots(folder, pom).Count > 0;

    private static List<string> SourceRoots(string folder, EffectivePom pom)
    {
        string[] candidates =
        [
            pom.SourceDirectory ?? "src/main/java",
            pom.TestSourceDirectory ?? "src/test/java",
            Path.Combine("target", "generated-sources", "annotations"),
        ];
        return [.. candidates.Select(c => Path.GetFullPath(Path.Combine(folder, c))).Where(Directory.Exists).Distinct(StringComparer.Ordinal)];
    }
}
