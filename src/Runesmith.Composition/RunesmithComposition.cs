using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.Composition;

namespace Runesmith.Composition;

/// <summary>Loads plugins and composes them with Runesmith's own parts.</summary>
/// <remarks>Composing means finding every export and import and checking that they fit, which takes a while. The result is cached on disk, keyed
/// by the exact assemblies that took part, so later starts only read the cache until an assembly changes.</remarks>
public static class RunesmithComposition
{
    private const string CachePrefix = "composition-";

    /// <summary>Loads the plugins and composes everything.</summary>
    public static async Task<CompositionResult> CreateAsync(CompositionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var stopwatch = Stopwatch.StartNew();
        var loader = new PluginAssemblyLoader();
        var resolver = new Resolver(loader);

        var plugins = PluginDiscovery.Discover(options.PluginFolders, options.DisabledPlugins, options.HubPlugins, options.WithheldPlugins, options.AllowLocalOverrides).ToList();
        var pluginLoader = new PluginLoader(plugins);
        var loaded = pluginLoader.Load();
        foreach (var contracts in pluginLoader.Contracts.Assemblies)
            loader.Add(contracts);
        foreach (var (_, assembly) in loaded)
            loader.Add(assembly);

        var assemblies = new List<Assembly>(options.HostAssemblies);
        assemblies.AddRange(loaded.Select(plugin => plugin.Assembly));
        var cachePath = options.CacheDirectory is { } directory ? Path.Combine(directory, CachePrefix + CacheKey(assemblies) + ".cache") : null;
        if (cachePath is not null && await TryLoadCacheAsync(cachePath, resolver, cancellationToken) is { } cached)
            return new CompositionResult(cached.CreateExportProvider(), plugins, [], fromCache: true, stopwatch.Elapsed);

        var discovery = new AttributedPartDiscovery(resolver, isNonPublicSupported: true);
        var hostParts = await discovery.CreatePartsAsync(options.HostAssemblies, progress: null, cancellationToken);
        var errors = hostParts.DiscoveryErrors.Select(error => error.Message).ToList();
        var catalog = ComposableCatalog.Create(resolver).AddParts(hostParts);
        foreach (var (index, assembly) in loaded)
        {
            if (pluginLoader.Blocker(plugins[index]) is { } blocker)
            {
                plugins[index] = plugins[index].Fail(blocker);
                errors.Add($"{plugins[index].Manifest.Name}: {blocker}");
                continue;
            }

            var parts = await discovery.CreatePartsAsync([assembly], progress: null, cancellationToken);
            if (parts.DiscoveryErrors.Count > 0)
            {
                plugins[index] = plugins[index].Fail(parts.DiscoveryErrors[0].Message);
                errors.AddRange(parts.DiscoveryErrors.Select(error => error.Message));
                continue;
            }

            catalog = catalog.AddParts(parts);
        }

        var configuration = CompositionConfiguration.Create(catalog);
        errors.AddRange(configuration.CompositionErrors.SelectMany(level => level).Select(diagnostic => diagnostic.Message));
        var runtime = RuntimeComposition.CreateRuntimeComposition(configuration);

        if (cachePath is not null && errors.Count == 0)
            await SaveCacheAsync(cachePath, runtime, cancellationToken);

        var factory = runtime.CreateExportProviderFactory();
        return new CompositionResult(factory.CreateExportProvider(), plugins, errors, fromCache: false, stopwatch.Elapsed);
    }

    private static string CacheKey(IEnumerable<Assembly> assemblies)
    {
        var text = new StringBuilder();
        text.AppendLine(typeof(ExportProvider).Assembly.GetName().Version?.ToString());
        foreach (var assembly in assemblies.OrderBy(a => a.FullName, StringComparer.Ordinal))
        {
            var file = new FileInfo(assembly.Location);
            text.Append(assembly.FullName).Append('|').Append(assembly.ManifestModule.ModuleVersionId).Append('|')
                .Append(file.Exists ? file.Length : 0).Append('|').AppendLine(file.Exists ? file.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())))[..16];
    }

    private static async Task<IExportProviderFactory?> TryLoadCacheAsync(string path, Resolver resolver, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await new CachedComposition().LoadExportProviderFactoryAsync(stream, resolver, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A cache from another build of Runesmith, or a damaged one, is rebuilt.
            TryDelete(path);
            return null;
        }
    }

    private static async Task SaveCacheAsync(string path, RuntimeComposition runtime, CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            foreach (var old in Directory.EnumerateFiles(directory, CachePrefix + "*.cache"))
                TryDelete(old);

            var temporary = path + ".tmp";
            await using (var stream = File.Create(temporary))
                await new CachedComposition().SaveAsync(runtime, stream, cancellationToken);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Without a cache the next start composes from scratch, which is slower but correct.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
