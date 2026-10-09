using Runesmith.Languages.Java.ClassFiles;
using Runesmith.LanguageServices;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>The top-level types of some libraries by the first letter of their names, for completing types that are not imported yet.</summary>
/// <remarks>Such a type is offered once the first letter of its name is typed, so an empty word does not rank tens of thousands of names.</remarks>
internal sealed class TypeNameIndex
{
    private readonly Dictionary<char, Entry[]> byFirstLetter;

    private TypeNameIndex(Dictionary<char, Entry[]> byFirstLetter) => this.byFirstLetter = byFirstLetter;

    public static TypeNameIndex Empty { get; } = new([]);

    public static TypeNameIndex Create(IEnumerable<string> binaryNames)
    {
        var groups = new Dictionary<char, List<Entry>>();
        foreach (var name in binaryNames)
        {
            if (ClassNames.OuterName(name) is not null || ClassNames.IsAnonymousOrLocal(name) || name.EndsWith("package-info", StringComparison.Ordinal))
                continue;

            var simple = ClassNames.SimpleName(name);
            if (simple.Length == 0)
                continue;

            var key = char.ToLowerInvariant(simple[0]);
            if (!groups.TryGetValue(key, out var list))
                groups[key] = list = [];
            list.Add(new Entry(simple, name, ClassNames.PackageName(name), CompletionMatcher.MaskOf(simple)));
        }

        return new TypeNameIndex(groups.ToDictionary(g => g.Key, g => g.Value.ToArray()));
    }

    /// <summary>Gets the types whose names start with a letter, either case.</summary>
    public ReadOnlySpan<Entry> StartingWith(char letter) =>
        byFirstLetter.TryGetValue(char.ToLowerInvariant(letter), out var entries) ? entries : [];

    /// <summary>A type: its simple name, binary name, package, and the character mask of its simple name.</summary>
    public readonly record struct Entry(string SimpleName, string BinaryName, string Package, ulong Mask);
}
