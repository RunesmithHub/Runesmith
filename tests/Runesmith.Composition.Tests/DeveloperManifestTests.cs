using System.Text;
using RunesmithHub.Protocol.Manifests;

namespace Runesmith.Composition.Tests;

public sealed class DeveloperManifestTests
{
    private static readonly string Repository = FindRepository();

    public static TheoryData<string> BundledPlugins() =>
        [.. Directory.EnumerateDirectories(Path.Combine(Repository, "plugins"), "Runesmith.*").Select(path => Path.GetFileName(path))];

    [Theory]
    [MemberData(nameof(BundledPlugins))]
    public void EveryBundledPluginHasAValidManifestWithItsProjectsAndIcon(string plugin)
    {
        var root = Path.Combine(Repository, "plugins", plugin);

        var result = ManifestValidator.Validate(File.ReadAllBytes(Path.Combine(root, "plugin.json")));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Problems));
        Assert.Equal($"{plugin}.Contracts/{plugin}.Contracts.csproj", result.Manifest.Projects?.Contracts);
        Assert.Equal($"{plugin}/{plugin}.csproj", result.Manifest.Projects?.Implementation);
        Assert.True(File.Exists(Path.Combine(root, result.Manifest.Projects!.Contracts!)));
        Assert.True(File.Exists(Path.Combine(root, result.Manifest.Projects.Implementation!)));
        Assert.True(File.Exists(Path.Combine(root, result.Manifest.Icon!)));
        Assert.Equal("https://github.com/RunesmithHub/Runesmith", result.Manifest.Repository);
    }

    [Fact]
    public void ThePluginTemplatesManifestIsValidOnceCreated()
    {
        var template = File.ReadAllText(Path.Combine(Repository, "templates", "Runesmith.Templates", "content", "runesmith-plugin", "plugin.json"));

        var result = ManifestValidator.Validate(Encoding.UTF8.GetBytes(template.Replace("PLUGIN_ID", "acme.todo", StringComparison.Ordinal)));

        Assert.True(result.IsValid, string.Join(Environment.NewLine, result.Problems));
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Runesmith.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("The tests do not run inside the Runesmith repository.");
    }
}
