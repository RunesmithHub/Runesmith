using Avalonia.Media;
using HammerUI;
using Runesmith.Sdk.Languages;

namespace Runesmith.Editor;

/// <summary>The icon and color of each kind of symbol, the same as completion uses for the same kinds.</summary>
public static class SymbolKinds
{
    public static Geometry Icon(SymbolKind kind) => kind switch
    {
        SymbolKind.Method or SymbolKind.Function or SymbolKind.Constructor => Icons.Box,
        SymbolKind.Field or SymbolKind.Variable or SymbolKind.Constant => Icons.Diamond,
        SymbolKind.Property => Icons.Sliders,
        SymbolKind.Class or SymbolKind.Object => Icons.Layers,
        SymbolKind.Struct => Icons.Square,
        SymbolKind.Interface => Icons.Puzzle,
        SymbolKind.Enum or SymbolKind.EnumMember => Icons.Hexagon,
        SymbolKind.Module or SymbolKind.Namespace or SymbolKind.Package => Icons.Package,
        SymbolKind.Event => Icons.Activity,
        SymbolKind.Operator => Icons.Plus,
        SymbolKind.TypeParameter => Icons.Tag,
        SymbolKind.File => Icons.File,
        SymbolKind.Key => Icons.Link,
        SymbolKind.Array => Icons.Braces,
        _ => Icons.Type,
    };

    /// <summary>Gets the theme resource key of the kind's color.</summary>
    public static string ColorKey(SymbolKind kind) => kind switch
    {
        SymbolKind.Method or SymbolKind.Function or SymbolKind.Constructor => "AccentBrush",
        SymbolKind.Field or SymbolKind.Variable or SymbolKind.Property or SymbolKind.Constant or SymbolKind.Key => "InfoBrush",
        SymbolKind.Class or SymbolKind.Struct or SymbolKind.Interface or SymbolKind.Enum or SymbolKind.EnumMember or SymbolKind.TypeParameter
            or SymbolKind.Object => "WarningBrush",
        SymbolKind.Module or SymbolKind.Namespace or SymbolKind.Package or SymbolKind.Event => "SuccessBrush",
        _ => "TextSecondaryBrush",
    };

    /// <summary>Gets the kind's name for people, such as "enum member".</summary>
    public static string Name(SymbolKind kind) => kind switch
    {
        SymbolKind.EnumMember => "enum member",
        SymbolKind.TypeParameter => "type parameter",
        _ => kind.ToString().ToLowerInvariant(),
    };

    /// <summary>Gets the chain of symbols that contain an offset, outermost first.</summary>
    public static IReadOnlyList<DocumentSymbol> PathAt(IReadOnlyList<DocumentSymbol> symbols, int offset)
    {
        ArgumentNullException.ThrowIfNull(symbols);
        var path = new List<DocumentSymbol>();
        var level = symbols;
        while (level.FirstOrDefault(s => s.Range.Start <= offset && offset <= s.Range.End) is { } inner && path.Count < 64)
        {
            path.Add(inner);
            level = inner.Children;
        }

        return path;
    }
}
