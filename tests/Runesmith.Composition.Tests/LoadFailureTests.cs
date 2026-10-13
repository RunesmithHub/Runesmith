namespace Runesmith.Composition.Tests;

public sealed class LoadFailureTests : IDisposable
{
    private const string FutureText = """
        [assembly: System.Reflection.AssemblyVersion("99.0.0.0")]
        namespace Runesmith.Text;
        public sealed class FromTheFuture { }
        """;

    private readonly TestPluginFolder folder = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public async Task APluginBuiltAgainstANewerSharedAssemblyFailsNamingBothVersions()
    {
        var futureText = folder.AddLibrary("Runesmith.Text", FutureText);
        folder.Add(new TestPlugin("tests.future", "Tests.Future")
        {
            References = [futureText],
            Implementation = """
                using System.Composition;
                namespace Tests.Future;
                [Export("future")]
                public sealed class Future { public Runesmith.Text.FromTheFuture? Value { get; } }
                """,
        });

        using var result = await ComposeAsync();

        var plugin = Assert.Single(result.Plugins);
        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains("Tests.Future.dll was built against Runesmith.Text 99.0.0.0", plugin.Error, StringComparison.Ordinal);
        Assert.Contains($"this Runesmith has Runesmith.Text {HostRuntime.VersionOf("Runesmith.Text")}", plugin.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ALibraryAPluginCarriesIsCheckedToo()
    {
        var futureText = folder.AddLibrary("Runesmith.Text", FutureText);
        var helper = folder.AddLibrary("Tests.Helper", """
            namespace Tests.Helper;
            public static class Help
            {
                public static string Name => "helper";
                public static Runesmith.Text.FromTheFuture Make() => new();
            }
            """, futureText);
        folder.Add(new TestPlugin("tests.carrier", "Tests.Carrier")
        {
            Bundled = [helper],
            Implementation = """
                using System.Composition;
                namespace Tests.Carrier;
                public sealed class Carrier { [Export("carrier")] public string Name => Tests.Helper.Help.Name; }
                """,
        });

        var plugin = Assert.Single(Load());

        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains("Tests.Helper.dll was built against Runesmith.Text 99.0.0.0", plugin.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void APluginThatCarriesItsOwnNewerCopyOfALibraryRunesmithAlsoHasLoads()
    {
        var immutable = typeof(System.Collections.Immutable.ImmutableArray).Assembly.GetName();
        var newer = folder.AddLibrary(immutable.Name!, $$"""
            [assembly: System.Reflection.AssemblyVersion("{{immutable.Version!.Major + 1}}.0.0.0")]
            namespace Tests.Newer;
            public static class Marker { public static string Name => "newer"; }
            """);
        folder.Add(new TestPlugin("tests.newer", "Tests.Newer")
        {
            Bundled = [newer],
            Implementation = """
                using System.Composition;
                namespace Tests.NewerUser;
                public sealed class User { [Export("newer")] public string Name => Tests.Newer.Marker.Name; }
                """,
        });

        var plugin = Assert.Single(Load());

        Assert.Equal(PluginState.Loaded, plugin.State);
    }

    [Fact]
    public async Task APluginWhoseTypesCannotBeScannedSaysWhyOnceForAllOfThem()
    {
        var missing = folder.AddLibrary("Tests.Missing", "namespace Tests.Missing; public interface IMissing { }");
        folder.Add(new TestPlugin("tests.broken", "Tests.Broken")
        {
            References = [missing],
            Implementation = """
                using System.Composition;
                namespace Tests.Broken;
                [Export("one")] public sealed class One : Tests.Missing.IMissing { }
                [Export("two")] public sealed class Two : Tests.Missing.IMissing { }
                """,
        });

        using var result = await ComposeAsync();

        var plugin = Assert.Single(result.Plugins);
        Assert.Equal(PluginState.Failed, plugin.State);
        Assert.Contains("Tests.Missing", plugin.Error, StringComparison.Ordinal);
        var error = Assert.Single(result.Errors);
        Assert.StartsWith("Tests.Broken: ", error, StringComparison.Ordinal);
        Assert.Contains("Tests.Missing", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCauseOfALoaderFailureIsTheMessageAtTheBottom()
    {
        var bottom = new FileNotFoundException("Could not load Tests.Missing.");
        var loader = new System.Reflection.ReflectionTypeLoadException([null], [bottom, null, bottom]);
        var top = new Microsoft.VisualStudio.Composition.PartDiscoveryException("Failure while scanning type \"Tests.Broken.One\".", loader);

        Assert.Equal("Could not load Tests.Missing.", DiscoveryFailures.Cause(top));
        Assert.Equal(["Failure while scanning type \"Tests.Broken.One\". Could not load Tests.Missing."], DiscoveryFailures.Describe([top]));
    }

    private List<PluginInfo> Load()
    {
        var plugins = folder.Discover();
        new PluginLoader(plugins).Load();
        return plugins;
    }

    private Task<CompositionResult> ComposeAsync() =>
        RunesmithComposition.CreateAsync(new CompositionOptions([], [(folder.PluginsPath, PluginSource.Local)], null), TestContext.Current.CancellationToken);
}
