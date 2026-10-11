using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Runesmith.Localization;

/// <summary>Reads the Fluent files an assembly embeds, the resources whose names start with <c>locales/</c> and end with <c>.ftl</c>.</summary>
public static class LocaleResources
{
    private const string Prefix = "locales/";

    /// <summary>Reads the Fluent resources of a loaded assembly.</summary>
    public static IReadOnlyList<LocaleResource> Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var resources = new List<LocaleResource>();
        foreach (var name in assembly.GetManifestResourceNames().Where(IsFluent).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is not null)
                resources.Add(new LocaleResource(name, Decode(stream)));
        }

        return resources;
    }

    /// <summary>Reads the Fluent resources of an assembly file, without loading it.</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="BadImageFormatException">The file is not a .NET assembly.</exception>
    public static IReadOnlyList<LocaleResource> Read(string assemblyPath)
    {
        using var file = File.OpenRead(assemblyPath);
        using var reader = new PEReader(file);
        var metadata = reader.GetMetadataReader();
        var directory = reader.PEHeaders.CorHeader?.ResourcesDirectory ?? default;
        var resources = new List<LocaleResource>();
        foreach (var handle in metadata.ManifestResources)
        {
            var resource = metadata.GetManifestResource(handle);
            var name = metadata.GetString(resource.Name);
            if (!IsFluent(name) || !resource.Implementation.IsNil || directory.Size == 0)
                continue;

            var section = reader.GetSectionData(directory.RelativeVirtualAddress).GetReader();
            section.Offset = checked((int)resource.Offset);
            var length = section.ReadInt32();
            using var stream = new MemoryStream(section.ReadBytes(length));
            resources.Add(new LocaleResource(name, Decode(stream)));
        }

        return [.. resources.OrderBy(r => r.Name, StringComparer.Ordinal)];
    }

    private static bool IsFluent(string name) => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(".ftl", StringComparison.OrdinalIgnoreCase);

    private static string Decode(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
