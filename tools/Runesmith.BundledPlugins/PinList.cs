using System.Text.Json;
using System.Text.Json.Serialization;
using RunesmithHub.Protocol;
using RunesmithHub.Protocol.Versioning;

namespace Runesmith.BundledPlugins;

/// <summary>The official plugins Runesmith ships, from <c>bundled-plugins.json</c>: the index they come from and each plugin's version and package hash.</summary>
internal sealed record PinList(Uri Index, IReadOnlyList<Pin> Plugins)
{
    /// <exception cref="InvalidDataException">The file can't be read or holds a value that is not allowed.</exception>
    public static PinList Read(string path)
    {
        PinList? list;
        try
        {
            list = JsonSerializer.Deserialize(File.ReadAllBytes(path), PinJson.Default.PinList);
        }
        catch (Exception exception) when (exception is JsonException or IOException)
        {
            throw new InvalidDataException($"{path} could not be read: {exception.Message}", exception);
        }

        if (list is null || list.Plugins.Count == 0)
            throw new InvalidDataException($"{path} lists no plugins.");
        foreach (var pin in list.Plugins)
        {
            if (!PluginId.IsValid(pin.Id) || !SemanticVersion.TryParse(pin.Version, out _) || !Hashes.IsSha256(pin.Sha256))
                throw new InvalidDataException($"{path}: {pin.Id} {pin.Version} needs a plugin ID, a version and a lowercase hex SHA-256.");
        }

        if (list.Plugins.GroupBy(pin => pin.Id, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1) is { } duplicate)
            throw new InvalidDataException($"{path} lists {duplicate.Key} more than once.");
        return list;
    }
}

/// <param name="Sha256">The SHA-256 of the plugin's package, as the hub's index pins it.</param>
internal sealed record Pin(string Id, string Version, string Sha256);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(PinList))]
internal sealed partial class PinJson : JsonSerializerContext;
