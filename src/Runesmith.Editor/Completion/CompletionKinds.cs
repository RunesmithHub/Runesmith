using Avalonia.Media;
using HammerUI;
using Runesmith.Sdk.Languages;

namespace Runesmith.Editor.Completion;

/// <summary>The icon and color of each kind of completion item.</summary>
internal static class CompletionKinds
{
    public static Geometry Icon(CompletionItemKind kind) => kind switch
    {
        CompletionItemKind.Method or CompletionItemKind.Function or CompletionItemKind.Constructor => Icons.Box,
        CompletionItemKind.Field or CompletionItemKind.Variable or CompletionItemKind.Constant => Icons.Diamond,
        CompletionItemKind.Property => Icons.Sliders,
        CompletionItemKind.Class => Icons.Layers,
        CompletionItemKind.Struct => Icons.Square,
        CompletionItemKind.Interface => Icons.Puzzle,
        CompletionItemKind.Enum or CompletionItemKind.EnumMember => Icons.Hexagon,
        CompletionItemKind.Module => Icons.Package,
        CompletionItemKind.Keyword => Icons.Code,
        CompletionItemKind.Snippet => Icons.Sparkles,
        CompletionItemKind.Event => Icons.Activity,
        CompletionItemKind.Operator => Icons.Plus,
        CompletionItemKind.TypeParameter => Icons.Tag,
        CompletionItemKind.Reference => Icons.Link,
        CompletionItemKind.File => Icons.File,
        CompletionItemKind.Folder => Icons.Folder,
        CompletionItemKind.Color => Icons.Palette,
        _ => Icons.Type,
    };

    /// <summary>Gets the theme resource key of the kind's color.</summary>
    public static string ColorKey(CompletionItemKind kind) => kind switch
    {
        CompletionItemKind.Method or CompletionItemKind.Function or CompletionItemKind.Constructor => "AccentBrush",
        CompletionItemKind.Field or CompletionItemKind.Variable or CompletionItemKind.Property or CompletionItemKind.Constant => "InfoBrush",
        CompletionItemKind.Class or CompletionItemKind.Struct or CompletionItemKind.Interface or CompletionItemKind.Enum
            or CompletionItemKind.EnumMember or CompletionItemKind.TypeParameter => "WarningBrush",
        CompletionItemKind.Module or CompletionItemKind.Event => "SuccessBrush",
        _ => "TextSecondaryBrush",
    };
}
