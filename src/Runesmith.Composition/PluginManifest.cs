using System.Text.Json;
using System.Text.Json.Serialization;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Composition;

/// <summary>A plugin's <c>plugin.json</c>: who the plugin is, which assemblies hold its code, what it needs and what it may do.</summary>
/// <remarks>Installed and bundled plugins carry the packaged manifest, with <c>schemaVersion</c> 1, the assemblies in <c>lib/</c>, the API
/// range, dependencies and capabilities. A manifest without <c>schemaVersion</c> is the older format plugins under development may still
/// use: it names one assembly and an API version, and declares no capabilities.</remarks>
/// <param name="Id">A unique id, <c>publisher.name</c>, such as <c>runesmith.csharp</c>.</param>
/// <param name="Version">The plugin's own version.</param>
public sealed record PluginManifest(string Id, string Name, string Version)
{
    /// <summary>The name of the manifest file in a plugin's folder.</summary>
    public const string FileName = "plugin.json";

    /// <summary>The folder of a packaged plugin's assemblies, relative to its manifest.</summary>
    public const string LibFolder = "lib";

    /// <summary>Gets the manifest format's version, or null for a manifest in the older format.</summary>
    public int? SchemaVersion { get; init; }

    /// <summary>Gets whether the manifest is in the older format, without a <c>schemaVersion</c>.</summary>
    public bool IsLegacy => SchemaVersion is null;

    public string? Description { get; init; }

    public string? Author { get; init; }

    /// <summary>Gets the range of plugin API versions the plugin works with, or null for a manifest in the older format.</summary>
    public VersionRange? RunesmithApi { get; init; }

    /// <summary>Gets the plugin API version an older-format manifest was built for, such as <c>0.1</c>.</summary>
    public string? ApiVersion { get; init; }

    /// <summary>Gets the plugin's implementation assembly, relative to the manifest, such as <c>lib/Acme.Todo.dll</c>.</summary>
    public string Assembly { get; init; } = "";

    /// <summary>Gets the plugin's contracts assembly, relative to the manifest, or null for a manifest in the older format.</summary>
    public string? ContractsAssembly { get; init; }

    public IReadOnlyList<PluginDependency> Dependencies { get; init; } = [];

    /// <summary>Gets the capabilities the plugin declared, or null for a manifest in the older format, which declares none.</summary>
    public IReadOnlyList<DeclaredCapability>? Capabilities { get; init; }

    /// <summary>Gets whether the plugin may use what needs a capability: it declared it, or its manifest is in the older format.</summary>
    public bool Allows(string capability) => Capabilities is not { } declared || declared.Any(c => c.Id == capability);

    /// <summary>Reads a manifest file in either format.</summary>
    /// <exception cref="InvalidDataException">The file is not valid JSON, misses a required field or has a value that is not allowed.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static PluginManifest Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("schemaVersion", out var schema))
            {
                if (schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out var version) && version > DeveloperManifest.CurrentSchemaVersion)
                    throw new InvalidDataException($"The manifest has schemaVersion {version}, and this Runesmith reads up to {DeveloperManifest.CurrentSchemaVersion}; the plugin needs a newer Runesmith.");

                return FromPackaged(IndexJson.Deserialize<PackagedManifest>(bytes), path);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} is not valid JSON: {exception.Message}", exception);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{path}: {exception.Message}", exception);
        }

        return ReadLegacy(bytes, path);
    }

    private static PluginManifest FromPackaged(PackagedManifest manifest, string path)
    {
        if (!PluginId.IsValid(manifest.Id))
            throw new InvalidDataException($"\"{manifest.Id}\" is not a plugin id; ids look like publisher.name.");

        foreach (var file in (ReadOnlySpan<string>)[manifest.Assemblies.Implementation.File, manifest.Assemblies.Contracts.File])
        {
            if (!RelativePaths.IsSafe(file) || !RelativePaths.HasExtension(file, ".dll"))
                throw new InvalidDataException($"\"{file}\" is not an assembly in {LibFolder}.");
        }

        return new PluginManifest(manifest.Id, manifest.Name, manifest.Version.ToString())
        {
            SchemaVersion = manifest.SchemaVersion,
            Description = manifest.Summary,
            Author = string.Join(", ", manifest.Authors),
            RunesmithApi = manifest.RunesmithApi,
            Assembly = $"{LibFolder}/{manifest.Assemblies.Implementation.File}",
            ContractsAssembly = $"{LibFolder}/{manifest.Assemblies.Contracts.File}",
            // The protocol's JSON leaves absent lists null despite their defaults.
            Dependencies = manifest.Dependencies ?? [],
            Capabilities = manifest.Capabilities ?? [],
        };
    }

    private static PluginManifest ReadLegacy(byte[] bytes, string path)
    {
        LegacyManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize(bytes, LegacyManifestJson.Default.LegacyManifest);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} is not valid JSON: {exception.Message}", exception);
        }

        if (manifest is null)
            throw new InvalidDataException($"{path} is empty.");

        string?[] required = [manifest.Id, manifest.Name, manifest.Version, manifest.ApiVersion, manifest.Assembly];
        string[] names = ["id", "name", "version", "apiVersion", "assembly"];
        for (var i = 0; i < required.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(required[i]))
                throw new InvalidDataException($"{path} has no \"{names[i]}\".");
        }

        return new PluginManifest(manifest.Id!, manifest.Name!, manifest.Version!)
        {
            Description = manifest.Description,
            Author = manifest.Author,
            ApiVersion = manifest.ApiVersion,
            Assembly = manifest.Assembly!,
        };
    }
}

internal sealed record LegacyManifest(string? Id, string? Name, string? Version, string? ApiVersion, string? Assembly, string? Description, string? Author);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(LegacyManifest))]
internal sealed partial class LegacyManifestJson : JsonSerializerContext;
