using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Runesmith.Sdk.Plugins;
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

    [Fact]
    public void TheSdkPackagesAndNewPluginsHaveTheApisVersion()
    {
        var api = RunesmithApi.Version.ToString(3);
        var props = XDocument.Load(Path.Combine(Repository, "Directory.Build.props"));
        var content = Path.Combine(Repository, "templates", "Runesmith.Templates", "content", "runesmith-plugin");
        using var template = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, ".template.config", "template.json")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(content, "plugin.json")));

        Assert.Equal(api, props.Descendants("RunesmithSdkVersion").Single().Value);
        Assert.Equal(api, template.RootElement.GetProperty("symbols").GetProperty("sdkVersion").GetProperty("defaultValue").GetString());
        Assert.Equal("^" + api, manifest.RootElement.GetProperty("runesmithApi").GetString());
        Assert.Equal(api, typeof(RunesmithApi).Assembly.GetName().Version?.ToString(3));
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
