using System.Text.Json;
using System.Text.Json.Serialization;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Index;
using RunesmithHub.Protocol.Json;
using RunesmithHub.Protocol.Manifests;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.Composition;

/// <summary>A plugin's <c>plugin.json</c>: who the plugin is, which assemblies hold its code, what it needs and what it may do.</summary>
/// <remarks>Plugins installed from the hub carry the packaged manifest, with <c>schemaVersion</c> 1, the assemblies in <c>lib/</c>, the API
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

    private const int MaxLanguages = 100;
    private const int MaxLanguageName = 40;

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

    /// <summary>Gets the hosts the plugin talks to with the network capability, such as <c>api.example.com</c>, or <c>*</c> for hosts the user
    /// chooses; empty for a manifest in the older format.</summary>
    public IReadOnlyList<string> NetworkHosts { get; init; } = [];

    /// <summary>Gets the BCP 47 tags of the languages of the plugin's own translations, English included, or null when the plugin is not
    /// localized.</summary>
    public IReadOnlyList<string>? Languages { get; init; }

    /// <summary>Gets the language the plugin adds as a language pack, or null when it is not one.</summary>
    public PluginLanguagePack? LanguagePack { get; init; }

    /// <summary>Gets whether the plugin is a language pack, which carries translations and no code.</summary>
    public bool IsLanguagePack => LanguagePack is not null;

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

        var pack = manifest.LanguagePack is { } declared ? new PluginLanguagePack(declared.Language, declared.Name, declared.EnglishName) : null;
        CheckLocalization(manifest.Languages, pack, manifest.Capabilities is { Count: > 0 }, manifest.Dependencies is { Count: > 0 });
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
            NetworkHosts = manifest.NetworkHosts ?? [],
            Languages = manifest.Languages,
            LanguagePack = pack,
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

        try
        {
            CheckLocalization(manifest.Languages, manifest.LanguagePack, hasCapabilities: false, hasDependencies: false);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{path}: {exception.Message}", exception);
        }

        return new PluginManifest(manifest.Id!, manifest.Name!, manifest.Version!)
        {
            Description = manifest.Description,
            Author = manifest.Author,
            ApiVersion = manifest.ApiVersion,
            Assembly = manifest.Assembly!,
            Languages = manifest.Languages,
            LanguagePack = manifest.LanguagePack,
        };
    }

    private static void CheckLocalization(IReadOnlyList<string>? languages, PluginLanguagePack? pack, bool hasCapabilities, bool hasDependencies)
    {
        if (pack is not null)
        {
            CheckTag("languagePack.language", pack.Language);
            CheckName("languagePack.name", pack.Name);
            CheckName("languagePack.englishName", pack.EnglishName);
            if (languages is not null)
                throw new InvalidDataException("A language pack lists no languages; languagePack.language is the one language it carries.");
            if (hasCapabilities)
                throw new InvalidDataException("A language pack declares no capabilities; it only carries translations.");
            if (hasDependencies)
                throw new InvalidDataException("A language pack depends on no plugin; it only carries translations.");
            return;
        }

        if (languages is null)
            return;
        if (languages.Count > MaxLanguages)
            throw new InvalidDataException($"languages can have at most {MaxLanguages} items.");

        var seen = new HashSet<string>(LanguageTags.Comparer);
        foreach (var language in languages)
        {
            CheckTag("languages", language);
            if (!seen.Add(language))
                throw new InvalidDataException($"languages lists {language} again.");
        }

        if (!seen.Contains(LanguageTags.English))
            throw new InvalidDataException("languages must include en, the English source the other languages translate.");
    }

    private static void CheckTag(string field, string? tag)
    {
        if (LanguageTags.Canonicalize(tag) is not { } canonical)
            throw new InvalidDataException($"{field} has \"{tag}\", which is not a BCP 47 language tag such as en, nl, nl-BE or pt-BR.");
        if (!string.Equals(tag, canonical, StringComparison.Ordinal))
            throw new InvalidDataException($"{field} has \"{tag}\", which must be written {canonical}.");
    }

    private static void CheckName(string field, string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxLanguageName || name.AsSpan().ContainsAny('\n', '\r'))
            throw new InvalidDataException($"{field} must be a single line of 1 to {MaxLanguageName} characters.");
    }
}

/// <summary>The language a language pack adds.</summary>
/// <param name="Language">The language's BCP 47 tag in its canonical case, such as <c>nl</c> or <c>pt-BR</c>.</param>
/// <param name="Name">The language's name in itself, such as Nederlands.</param>
/// <param name="EnglishName">The language's name in English, such as Dutch.</param>
public sealed record PluginLanguagePack(string Language, string Name, string EnglishName);

internal sealed record LegacyManifest(string? Id, string? Name, string? Version, string? ApiVersion, string? Assembly, string? Description, string? Author)
{
    public IReadOnlyList<string>? Languages { get; init; }

    public PluginLanguagePack? LanguagePack { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(LegacyManifest))]
internal sealed partial class LegacyManifestJson : JsonSerializerContext;
