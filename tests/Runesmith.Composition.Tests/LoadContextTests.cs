using System.Reflection;
using System.Runtime.Loader;

namespace Runesmith.Composition.Tests;

public sealed class LoadContextTests : IDisposable
{
    private readonly TestPluginFolder folder = new();

    public void Dispose() => folder.Dispose();

    [Fact]
    public async Task APluginUsesTheContractsOfAPluginItDependsOn()
    {
        folder.Add(Greeters.Greeter);
        folder.Add(Greeters.Welcome);

        using var result = await ComposeAsync();

        Assert.All(result.Plugins, plugin => Assert.Equal(PluginState.Loaded, plugin.State));
        Assert.Empty(result.Errors);
        Assert.Equal("Hello from the greeter", result.Exports.GetExportedValue<string>("greeting"));
    }

    [Fact]
    public void EveryPluginSeesTheSameContractsAssembly()
    {
        folder.Add(Greeters.Greeter);
        folder.Add(Greeters.Welcome);
        var (_, loaded) = Load();

        var contracts = Context(loaded, "Tests.Welcome").LoadFromAssemblyName(new AssemblyName("Tests.Greeter.Contracts"));

        Assert.Same(contracts, Context(loaded, "Tests.Greeter").LoadFromAssemblyName(new AssemblyName("Tests.Greeter.Contracts")));
        Assert.IsNotType<PluginLoadContext>(AssemblyLoadContext.GetLoadContext(contracts));
    }

    [Fact]
    public void APluginCannotLoadAnotherPluginsImplementation()
    {
        folder.Add(Greeters.Greeter);
        folder.Add(Greeters.Welcome);
        var (_, loaded) = Load();

        var exception = Assert.ThrowsAny<IOException>(() => Context(loaded, "Tests.Welcome").LoadFromAssemblyName(new AssemblyName("Tests.Greeter")));

        Assert.Contains("implementation", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void APluginCannotUseTheContractsOfAPluginItDoesNotDependOn()
    {
        folder.Add(Greeters.Greeter);
        folder.Add(Greeters.Stowaway);
        var (plugins, loaded) = Load();

        Assert.All(plugins, plugin => Assert.Equal(PluginState.Loaded, plugin.State));
        var exception = Assert.ThrowsAny<IOException>(() => Context(loaded, "Tests.Stowaway").LoadFromAssemblyName(new AssemblyName("Tests.Greeter.Contracts")));
        Assert.Contains("does not depend on", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APluginWhosePartsNeedUndeclaredContractsDoesNotLoad()
    {
        folder.Add(Greeters.Greeter);
        folder.Add(Greeters.Stowaway);

        using var result = await ComposeAsync();

        Assert.Equal(PluginState.Loaded, result.Plugins.Single(p => p.Manifest.Id == "tests.greeter").State);
        Assert.Equal(PluginState.Failed, result.Plugins.Single(p => p.Manifest.Id == "tests.stowaway").State);
    }

    [Fact]
    public void ContractsCanOnlyUseTheContractsOfDeclaredDependencies()
    {
        folder.Add(Greeters.Greeter);
        folder.Add(Greeters.LeakyContracts);
        var (plugins, _) = Load();

        var leaky = plugins.Single(p => p.Manifest.Id == "tests.leaky");
        Assert.Equal(PluginState.Failed, leaky.State);
        Assert.Contains("Tests.Greeter.Contracts", leaky.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void APluginGetsTheLibrariesRunesmithSharesFromRunesmith()
    {
        folder.Add(Greeters.Greeter);
        var (_, loaded) = Load();

        var shared = Context(loaded, "Tests.Greeter").LoadFromAssemblyName(typeof(System.Composition.ExportAttribute).Assembly.GetName());

        Assert.Same(typeof(System.Composition.ExportAttribute).Assembly, shared);
    }

    [Fact]
    public void APluginCannotUseTheRestOfRunesmithButAnOlderManifestCan()
    {
        folder.Add(Greeters.Greeter);
        folder.AddLegacy("acme.todo", "Acme.Todo", "namespace Acme.Todo; public sealed class Todo { }");
        var (_, loaded) = Load();
        var runesmith = typeof(PluginLoader).Assembly.GetName();

        Assert.ThrowsAny<IOException>(() => Context(loaded, "Tests.Greeter").LoadFromAssemblyName(runesmith));
        Assert.Same(typeof(PluginLoader).Assembly, Context(loaded, "Acme.Todo").LoadFromAssemblyName(runesmith));
    }

    [Fact]
    public void CodeIsTracedToThePluginItBelongsTo()
    {
        folder.Add(Greeters.Greeter);
        var (_, loaded) = Load();
        var implementation = loaded.Single().Assembly;
        var contracts = AssemblyLoadContext.GetLoadContext(implementation)!.LoadFromAssemblyName(new AssemblyName("Tests.Greeter.Contracts"));

        Assert.Equal("tests.greeter", PluginCallers.Of(implementation)?.Manifest.Id);
        Assert.Equal("tests.greeter", PluginCallers.Of(contracts)?.Manifest.Id);
        Assert.Null(PluginCallers.Of(typeof(PluginLoader).Assembly));
        Assert.Null(PluginCallers.Current());
    }

    private (List<PluginInfo> Plugins, List<(int Index, Assembly Assembly)> Loaded) Load()
    {
        var plugins = folder.Discover();
        var loaded = new PluginLoader(plugins).Load();
        return (plugins, loaded);
    }

    private static AssemblyLoadContext Context(List<(int Index, Assembly Assembly)> loaded, string name) =>
        AssemblyLoadContext.GetLoadContext(loaded.Single(l => l.Assembly.GetName().Name == name).Assembly)!;

    private Task<CompositionResult> ComposeAsync() =>
        RunesmithComposition.CreateAsync(new CompositionOptions([], [(folder.PluginsPath, PluginSource.Local)], null), TestContext.Current.CancellationToken);
}
