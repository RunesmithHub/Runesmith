namespace Runesmith.Composition.Tests;

public sealed class HostIntegrityTests : IDisposable
{
    private static readonly Version Api = new(0, 2, 1);

    private readonly TestPluginFolder folder = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public void AnAssemblyRunesmithSharesThatIsNotAtThePluginApiVersionIsReported()
    {
        var text = Text("0.1.3.0");

        var mismatch = Assert.Single(HostIntegrity.Check([("Runesmith.Text", text)], Api, Versions(("Runesmith.Text", "0.1.3.0"))));

        Assert.Equal(new HostMismatch("Runesmith.Text", new Version(0, 1, 3, 0), Api, null), mismatch);
    }

    [Fact]
    public void AnAssemblyBuiltAgainstANewerVersionThanTheBuildHasIsReported()
    {
        var newerText = Text("0.2.1.0");
        var shell = folder.AddLibrary("Runesmith.Shell", "namespace Runesmith.Shell; public static class Uses { public static object Make() => new Runesmith.Text.Buffer(); }", newerText);

        var mismatch = Assert.Single(HostIntegrity.Check([("Runesmith.Shell", shell)], Api, Versions(("Runesmith.Text", "0.1.3.0"))));

        Assert.Equal(new HostMismatch("Runesmith.Text", new Version(0, 1, 3, 0), new Version(0, 2, 1, 0), "Runesmith.Shell"), mismatch);
    }

    [Fact]
    public void ABuildWhoseAssembliesFitTogetherHasNoMismatches()
    {
        var text = Text("0.2.1.0");
        var shell = folder.AddLibrary("Runesmith.Shell", "namespace Runesmith.Shell; public static class Uses { public static object Make() => new Runesmith.Text.Buffer(); }", text);

        Assert.Empty(HostIntegrity.Check([("Runesmith.Text", text), ("Runesmith.Shell", shell)], Api, Versions(("Runesmith.Text", "0.2.1.0"))));
    }

    [Fact]
    public void AnAssemblyThatCannotBeReadIsLeftToFailWhereItLoads() =>
        Assert.Empty(HostIntegrity.Check([("Runesmith.Shell", Path.Combine(folder.LibrariesPath, "missing.dll"))], Api, Versions()));

    private string Text(string version) => folder.AddLibrary("Runesmith.Text", $$"""
        [assembly: System.Reflection.AssemblyVersion("{{version}}")]
        namespace Runesmith.Text;
        public sealed class Buffer { }
        """);

    private static Func<string, Version?> Versions(params (string Name, string Version)[] versions) =>
        name => versions.FirstOrDefault(v => v.Name == name) is { Name: not null } found ? Version.Parse(found.Version) : null;
}
