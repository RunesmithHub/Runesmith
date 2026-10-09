using Runesmith.Languages.Java.ClassFiles;

namespace Runesmith.Languages.Java.Libraries;

/// <summary>A set of classes that can be looked up by name: the JDK's API for a release, a JAR, or a class path of several.</summary>
/// <remarks>Names are binary names with dots between packages and <c>$</c> before nested classes, such as <c>java.util.Map$Entry</c>.
/// Anonymous and local classes are left out. Lookups are safe from any thread.</remarks>
public interface IClassLibrary
{
    /// <summary>Gets a name for logs, such as <c>JDK 21</c> or a JAR's file name.</summary>
    string Name { get; }

    /// <summary>Gets the packages that hold at least one type.</summary>
    IReadOnlyCollection<string> Packages { get; }

    /// <summary>Gets the binary names of every type, top-level and nested.</summary>
    IEnumerable<string> TypeNames { get; }

    /// <summary>Gets the binary names of the types in a package, top-level and nested, or none.</summary>
    IReadOnlyList<string> GetTypes(string packageName);

    bool Contains(string binaryName);

    /// <summary>Reads a type, or returns null when the library does not have it or its class file cannot be read.</summary>
    ClassSymbol? FindClass(string binaryName);

    /// <summary>Gets the binary names of the types with a simple name, such as every <c>List</c>, for completing types not imported yet.</summary>
    IReadOnlyList<string> FindBySimpleName(string simpleName);
}
