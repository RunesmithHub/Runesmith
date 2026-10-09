using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Libraries;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>The sources of another project of the same build, as a class library that always reads that project as it is now.</summary>
internal sealed class ReferencedSources(JavaProjectModel project) : IClassLibrary
{
    private SourceLibrary Current => project.Snapshot.Sources;

    public string Name => project.Name;

    public IReadOnlyCollection<string> Packages => Current.Packages;

    public IEnumerable<string> TypeNames => Current.TypeNames;

    public IReadOnlyList<string> GetTypes(string packageName) => Current.GetTypes(packageName);

    public bool Contains(string binaryName) => Current.Contains(binaryName);

    public ClassSymbol? FindClass(string binaryName) => Current.FindClass(binaryName);

    public IReadOnlyList<string> FindBySimpleName(string simpleName) => Current.FindBySimpleName(simpleName);
}
