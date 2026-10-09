using Runesmith.Composition;
using RunesmithHub.Protocol.Index;

namespace Runesmith.Shell.Tests.Plugins;

/// <summary>Plugins that call services in the tests, in place of the plugin on the call stack.</summary>
internal static class TestCallers
{
    /// <summary>A plugin with a packaged manifest that declares the given capabilities.</summary>
    public static PluginInfo Plugin(string id, params string[] capabilities) =>
        new(new PluginManifest(id, id, "1.0.0")
        {
            SchemaVersion = 1,
            Capabilities = [.. capabilities.Select(capability => new DeclaredCapability { Id = capability, Reason = "For the tests." })],
        }, Path.Combine(Path.GetTempPath(), id), PluginSource.Local);

    /// <summary>A plugin with a manifest in the older format, which declares nothing.</summary>
    public static PluginInfo Legacy(string id) =>
        new(new PluginManifest(id, id, "1.0") { ApiVersion = "0.1", Assembly = id + ".dll" }, Path.Combine(Path.GetTempPath(), id), PluginSource.Local);
}
