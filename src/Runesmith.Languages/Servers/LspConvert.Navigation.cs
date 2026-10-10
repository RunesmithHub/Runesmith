using Runesmith.Lsp;
using Runesmith.Sdk.Languages;
using Runesmith.Text;
using Protocol = Runesmith.Lsp.Protocol;

namespace Runesmith.Languages.Servers;

internal static partial class LspConvert
{
    private const int DeprecatedTag = 1;

    private static readonly Dictionary<string, SemanticTokenModifiers> ModifierNames = new(StringComparer.Ordinal)
    {
        ["declaration"] = SemanticTokenModifiers.Declaration,
        ["definition"] = SemanticTokenModifiers.Definition,
        ["readonly"] = SemanticTokenModifiers.Readonly,
        ["static"] = SemanticTokenModifiers.Static,
        ["deprecated"] = SemanticTokenModifiers.Deprecated,
        ["abstract"] = SemanticTokenModifiers.Abstract,
        ["async"] = SemanticTokenModifiers.Async,
        ["modification"] = SemanticTokenModifiers.Modification,
        ["documentation"] = SemanticTokenModifiers.Documentation,
        ["defaultLibrary"] = SemanticTokenModifiers.DefaultLibrary,
    };

    public static SymbolKind ToSymbolKind(int kind) => kind is >= 1 and <= 26 ? (SymbolKind)(kind - 1) : SymbolKind.Variable;

    public static DocumentHighlightKind ToHighlightKind(int? kind) => kind switch
    {
        2 => DocumentHighlightKind.Read,
        3 => DocumentHighlightKind.Write,
        _ => DocumentHighlightKind.Text,
    };

    public static DocumentSymbol ToDocumentSymbol(TextSnapshot snapshot, Protocol.DocumentSymbol symbol)
    {
        var range = ToSpan(snapshot, symbol.Range);
        var selection = ToSpan(snapshot, symbol.SelectionRange);
        return new DocumentSymbol(symbol.Name, ToSymbolKind(symbol.Kind), range, range.Contains(selection) ? selection : TextSpan.FromBounds(range.Start, range.Start))
        {
            Detail = string.IsNullOrWhiteSpace(symbol.Detail) ? null : symbol.Detail,
            IsDeprecated = symbol.Deprecated == true || symbol.Tags?.Contains(DeprecatedTag) == true,
            Children = symbol.Children is { Count: > 0 } children ? [.. children.Select(child => ToDocumentSymbol(snapshot, child))] : [],
        };
    }

    public static WorkspaceSymbol? ToWorkspaceSymbol(Protocol.SymbolInformation symbol)
    {
        if (LspUri.ToPath(symbol.Location.Uri) is not { } path)
            return null;

        var range = symbol.Location.Range;
        var location = new DocumentLocation(path, range is null ? default : ToTextPosition(range.Start), range is null ? null : ToTextPosition(range.End));
        return new WorkspaceSymbol(symbol.Name, ToSymbolKind(symbol.Kind), location)
        {
            ContainerName = string.IsNullOrWhiteSpace(symbol.ContainerName) ? null : symbol.ContainerName,
            IsDeprecated = symbol.Deprecated == true || symbol.Tags?.Contains(DeprecatedTag) == true,
        };
    }

    public static FoldingRange? ToFoldingRange(Protocol.FoldingRange range) =>
        range.EndLine <= range.StartLine || range.StartLine < 0
            ? null
            : new FoldingRange(range.StartLine, range.EndLine)
            {
                Kind = range.Kind switch
                {
                    "comment" => FoldingRangeKind.Comment,
                    "imports" => FoldingRangeKind.Imports,
                    "region" => FoldingRangeKind.Region,
                    _ => FoldingRangeKind.Block,
                },
                CollapsedText = string.IsNullOrEmpty(range.CollapsedText) ? null : range.CollapsedText,
            };

    /// <summary>Decodes a server's semantic tokens with its legend; types it does not name and tokens past the text are left out.</summary>
    public static IReadOnlyList<SemanticToken> ToSemanticTokens(TextSnapshot snapshot, Protocol.SemanticTokensLegend legend, IReadOnlyList<int> data)
    {
        var types = legend.TokenTypes.Select(name => SemanticTokenTypes.All.FirstOrDefault(standard => standard == name) ?? name).ToArray();
        var modifiers = legend.TokenModifiers.Select(name => ModifierNames.GetValueOrDefault(name)).ToArray();
        var tokens = new List<SemanticToken>(data.Count / 5);
        var line = 0;
        var character = 0;
        var lineStart = 0;
        var lineLength = snapshot.GetLine(0).Length;
        for (var i = 0; i + 4 < data.Count; i += 5)
        {
            if (data[i] > 0)
            {
                line += data[i];
                character = data[i + 1];
                if (line >= snapshot.LineCount)
                    break;

                var textLine = snapshot.GetLine(line);
                lineStart = textLine.Start;
                lineLength = textLine.Length;
            }
            else
            {
                character += data[i + 1];
            }

            var type = data[i + 3];
            var length = Math.Min(data[i + 2], lineLength - character);
            if ((uint)type >= (uint)types.Length || length <= 0 || character < 0)
                continue;

            var flags = SemanticTokenModifiers.None;
            for (int bits = data[i + 4], bit = 0; bits != 0 && bit < modifiers.Length; bits >>= 1, bit++)
            {
                if ((bits & 1) != 0)
                    flags |= modifiers[bit];
            }

            tokens.Add(new SemanticToken(new TextSpan(lineStart + character, length), types[type], flags));
        }

        return tokens;
    }

    public static HierarchyItem? ToHierarchyItem(Protocol.HierarchyItem item)
    {
        if (LspUri.ToPath(item.Uri) is not { } path)
            return null;

        return new HierarchyItem(item.Name, ToSymbolKind(item.Kind), new DocumentLocation(path, ToTextPosition(item.Range.Start), ToTextPosition(item.Range.End)))
        {
            Detail = string.IsNullOrWhiteSpace(item.Detail) ? null : item.Detail,
            SelectionLocation = new DocumentLocation(path, ToTextPosition(item.SelectionRange.Start), ToTextPosition(item.SelectionRange.End)),
        };
    }

    public static IReadOnlyList<DocumentLocation> ToLocations(string path, IEnumerable<Protocol.Range> ranges) =>
        [.. ranges.Select(range => new DocumentLocation(path, ToTextPosition(range.Start), ToTextPosition(range.End)))];
}
