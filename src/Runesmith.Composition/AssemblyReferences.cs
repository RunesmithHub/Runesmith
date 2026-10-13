using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Runesmith.Composition;

/// <summary>Reads which assemblies an assembly file was built against, without loading it.</summary>
internal static class AssemblyReferences
{
    /// <summary>Gets the name and version of every assembly the file references, or none when it is not a .NET assembly, such as a native
    /// library a plugin carries.</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read.</exception>
    public static List<(string Name, Version Version)> Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        try
        {
            if (!reader.HasMetadata)
                return [];

            var metadata = reader.GetMetadataReader();
            if (!metadata.IsAssembly)
                return [];

            return
            [
                .. metadata.AssemblyReferences.Select(handle => metadata.GetAssemblyReference(handle))
                    .Select(reference => (metadata.GetString(reference.Name), reference.Version)),
            ];
        }
        catch (BadImageFormatException)
        {
            return [];
        }
    }
}
