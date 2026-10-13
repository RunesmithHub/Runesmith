namespace Runesmith.Composition.Tests;

public sealed class LoadFailureTests : IDisposable
{
    private readonly TestPluginFolder folder = new();

    public void Dispose() => folder.Dispose();

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

    private Task<CompositionResult> ComposeAsync() =>
        RunesmithComposition.CreateAsync(new CompositionOptions([], [(folder.PluginsPath, PluginSource.Local)], null), TestContext.Current.CancellationToken);
}
