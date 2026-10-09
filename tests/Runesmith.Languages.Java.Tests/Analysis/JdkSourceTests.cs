using System.IO.Compression;
using Runesmith.Languages.Java.Semantics;
using Runesmith.Languages.Java.Tests.Jdk;

namespace Runesmith.Languages.Java.Tests.Analysis;

/// <summary>Runs the semantic checks over whole packages of the JDK's own sources, as one project, where every problem would be a false one.</summary>
public sealed class JdkSourceTests
{
    private static readonly string[] Packages =
    [
        "java/util/", "java/util/function/", "java/util/stream/", "java/util/regex/", "java/util/concurrent/", "java/util/concurrent/atomic/",
        "java/util/concurrent/locks/", "java/time/", "java/time/format/", "java/time/temporal/", "java/time/chrono/", "java/io/",
    ];

    [Fact]
    public async Task JdkSourcesHaveNoSemanticErrors()
    {
        var jdk = TestJdk.Require();
        var sources = Path.Combine(jdk.HomePath, "lib", "src.zip");
        if (!File.Exists(sources))
            Assert.Skip("The JDK has no src.zip.");

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var archive = ZipFile.OpenRead(sources))
        {
            foreach (var entry in archive.Entries)
            {
                const string Module = "java.base/";
                if (!entry.FullName.StartsWith(Module, StringComparison.Ordinal) || !entry.FullName.EndsWith(".java", StringComparison.Ordinal))
                    continue;

                var path = entry.FullName[Module.Length..];
                var folder = path[..(path.LastIndexOf('/') + 1)];
                if (!Packages.Contains(folder))
                    continue;

                using var reader = new StreamReader(entry.Open());
                files[path] = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            }
        }

        await using var project = await JavaTestProject.CreateAsync(files);
        var snapshot = Assert.Single(project.Analyzer.Workspace.Projects).Snapshot;
        var problems = new List<string>();
        foreach (var file in snapshot.Sources.Files)
        {
            foreach (var problem in SemanticDiagnostics.Find(snapshot.ModelFor(file), TestContext.Current.CancellationToken))
                problems.Add($"{Path.GetFileName(file.Path)} {problem.Code} {problem.Message} at {problem.Span}");
        }


        Assert.True(problems.Count == 0, $"{problems.Count} problems in {files.Count} files:\n" + string.Join('\n', problems.Take(40)));
        Assert.True(files.Count > 400, $"Only {files.Count} files were checked.");
    }
}
