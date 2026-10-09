using System.Text;
using RunesmithHub.Protocol.Manifests;

namespace Runesmith.Composition.Tests;

public sealed class DeveloperManifestTests
{
    private static readonly string Repository = FindRepository();

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
