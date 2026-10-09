using System.IO.Compression;
using Runesmith.Languages.Java.Syntax;
using Runesmith.Languages.Java.Tests.Jdk;
using Runesmith.LanguageServices;

namespace Runesmith.Languages.Java.Tests.Analysis;

/// <summary>Completes at every member access of some of the JDK's own sources and checks the member written there is offered, where the
/// receiver is understood: receivers of the JDK's internal packages, which are not public API, offer nothing.</summary>
public sealed class MemberAccessConformanceTests
{
    [Fact]
    public async Task MemberAccessesInJdkSourcesOfferTheMemberThatIsThere()
    {
        var jdk = TestJdk.Require();
        var sources = Path.Combine(jdk.HomePath, "lib", "src.zip");
        if (!File.Exists(sources))
            Assert.Skip("The JDK has no src.zip.");

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var archive = ZipFile.OpenRead(sources))
        {
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("java.base/java/util/", StringComparison.Ordinal)
                && e.FullName.EndsWith(".java", StringComparison.Ordinal) && e.FullName.Count(c => c == '/') == 3))
            {
                using var reader = new StreamReader(entry.Open());
                files[entry.FullName["java.base/".Length..]] = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            }
        }

        await using var project = await JavaTestProject.CreateAsync(files);
        var checkedAccesses = 0;
        var missing = new List<string>();
        foreach (var (path, text) in files.OrderBy(f => f.Key, StringComparer.Ordinal).Take(60))
        {
            project.Open(path, text);
            var tree = JavaSyntaxTree.Parse(text);
            foreach (var node in tree.Root.DescendantNodesAndSelf())
            {
                var name = node switch
                {
                    FieldAccessExpressionSyntax access when access.Target is not (ThisExpressionSyntax or SuperExpressionSyntax) => access.Name,
                    MethodInvocationExpressionSyntax { Target: { } target } call when target is not (ThisExpressionSyntax or SuperExpressionSyntax) => call.Name,
                    _ => null,
                };
                if (name is null || name.IsMissing || text[name.Start - 1] != '.')
                    continue;

                checkedAccesses++;
                var result = await project.CompleteAsync(path, name.Start, CompletionTriggerKind.Character, '.');
                if (result.Items.Count == 0)
                {
                    checkedAccesses--;
                    continue;
                }

                if (!result.Items.Any(i => i.Label == name.Text))
                    missing.Add($"{path}:{name.Start} {name.Text}");
            }
        }

        var rate = 1.0 - (double)missing.Count / checkedAccesses;
        Assert.True(checkedAccesses > 3000, $"Only {checkedAccesses} member accesses had a receiver the analyzer understood.");
        Assert.True(rate > 0.99, $"{missing.Count} of {checkedAccesses} member accesses lacked their member:\n" + string.Join('\n', missing.Take(60)));
    }
}
