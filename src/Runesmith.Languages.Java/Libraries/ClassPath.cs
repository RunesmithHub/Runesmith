using Runesmith.Languages.Java.ClassFiles;

namespace Runesmith.Languages.Java.Libraries;

/// <summary>Several class libraries searched in order, as the compiler searches a class path: the first library with a type wins.</summary>
/// <remarks>The JDK comes first, then the project's libraries; the project's own source types can be added as a library of their own.
/// A class path does not own its libraries.</remarks>
public sealed class ClassPath(IEnumerable<IClassLibrary> libraries) : IClassLibrary
{
    private readonly IReadOnlyList<IClassLibrary> libraries = [.. libraries];

    public string Name => string.Join(", ", libraries.Select(l => l.Name));

    /// <summary>Gets the libraries, in search order.</summary>
    public IReadOnlyList<IClassLibrary> Libraries => libraries;

    public IReadOnlyCollection<string> Packages => libraries.SelectMany(l => l.Packages).ToHashSet(StringComparer.Ordinal);

    public IEnumerable<string> TypeNames => libraries.SelectMany(l => l.TypeNames).Distinct(StringComparer.Ordinal);

    public IReadOnlyList<string> GetTypes(string packageName) =>
        libraries.Count == 1 ? libraries[0].GetTypes(packageName) : [.. libraries.SelectMany(l => l.GetTypes(packageName)).Distinct(StringComparer.Ordinal)];

    public bool Contains(string binaryName) => libraries.Any(l => l.Contains(binaryName));

    public ClassSymbol? FindClass(string binaryName)
    {
        foreach (var library in libraries)
        {
            if (library.Contains(binaryName))
                return library.FindClass(binaryName);
        }

        return null;
    }

    public IReadOnlyList<string> FindBySimpleName(string simpleName) =>
        libraries.Count == 1 ? libraries[0].FindBySimpleName(simpleName) : [.. libraries.SelectMany(l => l.FindBySimpleName(simpleName)).Distinct(StringComparer.Ordinal)];

    /// <summary>Finds the library a type comes from, the first that has it.</summary>
    public IClassLibrary? LibraryOf(string binaryName) => libraries.FirstOrDefault(l => l.Contains(binaryName));
}
