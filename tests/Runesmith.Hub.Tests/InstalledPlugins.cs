using Runesmith.Hub.State;
using Runesmith.Tests.Hub;

namespace Runesmith.Hub.Tests;

/// <summary>Puts hub plugins in a fixture's hub folder as if the hub had installed them earlier.</summary>
internal static class InstalledPlugins
{
    public static void Write(HubFixture fixture, params (string Id, string Version, string Tier, bool Requested)[] plugins)
    {
        var entries = new List<InstalledHubPlugin>();
        foreach (var (id, version, tier, requested) in plugins)
        {
            var folder = Directory.CreateDirectory(Path.Combine(fixture.Paths.PluginFolder(id), "lib")).Parent!.FullName;
            File.WriteAllText(Path.Combine(folder, "plugin.json"), $$"""{ "id": "{{id}}", "version": "{{version}}" }""");
            File.WriteAllBytes(Path.Combine(folder, "lib", id + ".dll"), [1, 2, 3, 4]);
            entries.Add(new InstalledHubPlugin(id, version, new string('0', 64), tier, requested) { Files = PluginFiles.Record(folder) });
        }

        new HubStateStore(fixture.Paths.StateFile).Save(new HubState { Plugins = entries });
    }
}
