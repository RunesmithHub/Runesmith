using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Runesmith.Composition.Tests;

/// <summary>A plugin to build for a test: its id, the root name of its two assemblies and their code.</summary>
/// <param name="Root">The implementation assembly's name; the contracts assembly is this with <c>.Contracts</c>.</param>
internal sealed record TestPlugin(string Id, string Root)
{
    public string Version { get; init; } = "1.0.0";

    public string RunesmithApi { get; init; } = "^0.2.0";

    public string Contracts { get; init; } = "";

    public string Implementation { get; init; } = "";

    /// <summary>Gets the plugins it declares, with their ranges.</summary>
    public IReadOnlyList<(string Id, string Range)> Dependencies { get; init; } = [];

    /// <summary>Gets the plugins whose contracts its code is built against, declared or not.</summary>
    public IReadOnlyList<TestPlugin> BuiltAgainst { get; init; } = [];

    /// <summary>Gets libraries from <see cref="TestPluginFolder.AddLibrary"/> that its code is built against without carrying them.</summary>
    public IReadOnlyList<string> References { get; init; } = [];

    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public IReadOnlyList<string> NetworkHosts { get; init; } = [];

    public IReadOnlyList<string>? Languages { get; init; }

    public (string Language, string Name, string EnglishName)? LanguagePack { get; init; }
}

/// <summary>A temporary plugins folder that test plugins are built into.</summary>
internal sealed class TestPluginFolder : IDisposable
{
    private static readonly Lazy<MetadataReference[]> Framework = new(() =>
    {
        var directory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        return
        [
            .. trusted.Where(path => Path.GetDirectoryName(path) == directory).Select(path => MetadataReference.CreateFromFile(path)),
            MetadataReference.CreateFromFile(typeof(System.Composition.ExportAttribute).Assembly.Location),
        ];
    });

    private readonly Dictionary<string, string> built = new(StringComparer.Ordinal);

    /// <summary>Gets the plugins folder.</summary>
    public string PluginsPath { get; } = Directory.CreateTempSubdirectory("runesmith-plugins-").FullName;

    /// <summary>Gets the folder libraries are built into, apart from any plugin.</summary>
    public string LibrariesPath { get; } = Directory.CreateTempSubdirectory("runesmith-libraries-").FullName;

    /// <summary>Builds a plugin with a packaged manifest into its own folder, with the contracts it is built against copied next to it as a
    /// build would.</summary>
    /// <param name="folder">The name of the plugin's folder, or null for its id.</param>
    public string Add(TestPlugin plugin, string? folder = null)
    {
        folder ??= plugin.Id;
        var lib = Directory.CreateDirectory(Path.Combine(PluginsPath, folder, "lib")).FullName;
        var others = plugin.BuiltAgainst.Select(other => built[other.Root + ".Contracts"]).ToList();
        var contracts = Compile(plugin.Root + ".Contracts", plugin.Contracts, others, lib);
        var implementation = Compile(plugin.Root, plugin.Implementation, [contracts, .. others, .. plugin.References], lib);
        foreach (var other in others)
            File.Copy(other, Path.Combine(lib, Path.GetFileName(other)));

        var manifest = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = plugin.Id,
            ["name"] = plugin.Root,
            ["version"] = plugin.Version,
            ["summary"] = $"The {plugin.Root} test plugin.",
            ["authors"] = new JsonArray("Tests"),
            ["license"] = "Apache-2.0",
            ["repository"] = "https://github.com/RunesmithHub/Runesmith",
            ["runesmithApi"] = plugin.RunesmithApi,
            ["dependencies"] = new JsonArray([.. plugin.Dependencies.Select(d => new JsonObject { ["id"] = d.Id, ["range"] = d.Range })]),
            ["capabilities"] = new JsonArray([.. plugin.Capabilities.Select(c => new JsonObject { ["id"] = c, ["reason"] = "For the tests." })]),
            ["networkHosts"] = new JsonArray([.. plugin.NetworkHosts.Select(h => (JsonNode)h)]),
            ["categories"] = new JsonArray("Other"),
            ["assemblies"] = new JsonObject
            {
                ["contracts"] = FileEntry(contracts),
                ["implementation"] = FileEntry(implementation),
            },
            ["build"] = new JsonObject { ["commit"] = "0000000000000000000000000000000000000000", ["run"] = "local", ["sdk"] = "10.0.100" },
        };
        if (plugin.Languages is { } languages)
            manifest["languages"] = new JsonArray([.. languages.Select(l => (JsonNode)l)]);
        if (plugin.LanguagePack is { } pack)
            manifest["languagePack"] = new JsonObject { ["language"] = pack.Language, ["name"] = pack.Name, ["englishName"] = pack.EnglishName };

        File.WriteAllText(Path.Combine(PluginsPath, folder, "plugin.json"), manifest.ToJsonString());
        return Path.Combine(PluginsPath, folder);
    }

    /// <summary>Builds a library outside the plugins folder, for plugins to be built against.</summary>
    /// <returns>The library's path.</returns>
    public string AddLibrary(string name, string source, params string[] references) => Compile(name, source, references, LibrariesPath);

    /// <summary>Builds a plugin with a manifest in the older format, without a <c>schemaVersion</c>.</summary>
    public string AddLegacy(string id, string assemblyName, string source, string apiVersion = "0.2")
    {
        var folder = Directory.CreateDirectory(Path.Combine(PluginsPath, id)).FullName;
        Compile(assemblyName, source, [], folder);
        var manifest = new JsonObject
        {
            ["id"] = id,
            ["name"] = assemblyName,
            ["version"] = "1.0",
            ["apiVersion"] = apiVersion,
            ["assembly"] = assemblyName + ".dll",
        };
        File.WriteAllText(Path.Combine(folder, "plugin.json"), manifest.ToJsonString());
        return folder;
    }

    /// <summary>Finds the plugins in the folder, as local plugins.</summary>
    public List<PluginInfo> Discover(params string[] disabled) =>
        [.. PluginDiscovery.Discover([(PluginsPath, PluginSource.Local)], disabled.ToHashSet(StringComparer.Ordinal))];

    public void Dispose()
    {
        try
        {
            Directory.Delete(PluginsPath, recursive: true);
            Directory.Delete(LibrariesPath, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
            // Loaded assemblies stay locked on Windows until the process ends.
        }
    }

    private string Compile(string name, string source, IEnumerable<string> references, string folder)
    {
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            [.. Framework.Value, .. references.Select(path => MetadataReference.CreateFromFile(path))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var path = Path.Combine(folder, name + ".dll");
        var result = compilation.Emit(path);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        built[name] = path;
        return path;
    }

    private static JsonObject FileEntry(string path) => new()
    {
        ["file"] = Path.GetFileName(path),
        ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
    };
}
