using System.Diagnostics;

namespace Runesmith.Languages.Java.Syntax.Tests;

public sealed class ConformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void EveryFileOfTheJdkSourcesParsesWithoutErrors()
    {
        if (JdkSources.Path is null)
            Assert.Skip("No JDK src.zip found through JAVA_HOME or java on the PATH");

        var files = JdkSources.ReadAll();
        var options = new JavaParseOptions { Version = new JavaVersion(JavaVersion.LatestRelease, isPreviewEnabled: true) };
        var failures = new List<string>();
        var errors = 0;
        long characters = 0;
        var stopwatch = Stopwatch.StartNew();
        var checkedTrees = new List<JavaSyntaxTree>();
        foreach (var (name, text) in files)
        {
            characters += text.Length;
            var tree = JavaSyntaxTree.Parse(text, options);
            if (characters % 50 == 0)
                checkedTrees.Add(tree);
            if (tree.Diagnostics.Length == 0)
                continue;

            errors += tree.Diagnostics.Length;
            var first = tree.Diagnostics[0];
            var line = text.AsSpan(0, first.Span.Start).Count('\n') + 1;
            failures.Add($"{name}:{line}: {first.Code} {first.Message}");
        }

        stopwatch.Stop();
        output.WriteLine($"{files.Count} files, {characters / 1_000_000.0:F1} M characters, {errors} errors, {stopwatch.ElapsedMilliseconds} ms");
        Assert.True(failures.Count == 0, $"{failures.Count} files with errors:\n{string.Join('\n', failures.Take(40))}");
        foreach (var tree in checkedTrees)
            Java.AssertWellFormed(tree);
    }
}
