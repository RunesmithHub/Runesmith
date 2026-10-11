using System.Composition;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Text;

namespace Runesmith.TypingBenchmark;

/// <summary>Semantic tokens and occurrence highlights for the benchmark's sample file, as an analyzer inside Runesmith would give them: each
/// version of the text is read and classified once. Other documents are left to the next provider.</summary>
[Export(typeof(ISemanticTokensProvider))]
[Export(typeof(IDocumentHighlightProvider))]
[Languages("csharp")]
[Shared]
public sealed class SampleLanguage : ISemanticTokensProvider, IDocumentHighlightProvider
{
    private static readonly HashSet<string> Keywords =
    [
        "as", "base", "bool", "break", "case", "class", "const", "default", "else", "false", "for", "foreach", "get", "if", "in", "int", "is",
        "namespace", "new", "null", "private", "public", "readonly", "region", "endregion", "return", "sealed", "set", "string", "this", "throw",
        "true", "using", "var", "void", "while", "nameof",
    ];

    private static readonly HashSet<string> Parameters = ["value", "limit", "values", "other"];

    private readonly Lock gate = new();
    private TextSnapshot? lastSnapshot;
    private string lastText = "";
    private List<SemanticToken>? lastTokens;

    public Task<IReadOnlyList<SemanticToken>?> GetSemanticTokensAsync(IDocument document, TextSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!TypingBenchmark.IsSample(document))
            return Task.FromResult<IReadOnlyList<SemanticToken>?>(null);

        lock (gate)
        {
            var text = TextOf(snapshot);
            lastTokens ??= Classify(text, cancellationToken);
            return Task.FromResult<IReadOnlyList<SemanticToken>?>(lastTokens);
        }
    }

    public Task<IReadOnlyList<DocumentHighlight>?> GetDocumentHighlightsAsync(IDocument document, TextSnapshot snapshot, int offset, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!TypingBenchmark.IsSample(document))
            return Task.FromResult<IReadOnlyList<DocumentHighlight>?>(null);

        var word = WordBoundaries.GetWordAt(snapshot, offset);
        if (word.IsEmpty)
            return Task.FromResult<IReadOnlyList<DocumentHighlight>?>([]);

        string text;
        lock (gate)
            text = TextOf(snapshot);
        var name = text.Substring(word.Start, word.Length);
        var highlights = new List<DocumentHighlight>();
        for (var at = text.IndexOf(name, StringComparison.Ordinal); at >= 0; at = text.IndexOf(name, at + name.Length, StringComparison.Ordinal))
        {
            if (at > 0 && IsNamePart(text[at - 1]) || at + name.Length < text.Length && IsNamePart(text[at + name.Length]))
                continue;

            var assigned = text.AsSpan(at + name.Length).TrimStart(' ').StartsWith("= ", StringComparison.Ordinal);
            highlights.Add(new DocumentHighlight(new TextSpan(at, name.Length), assigned ? DocumentHighlightKind.Write : DocumentHighlightKind.Read));
        }

        return Task.FromResult<IReadOnlyList<DocumentHighlight>?>(highlights);
    }

    // Every name outside comments and strings: a call is a method, a capitalized name a class, a parameter's name a parameter, the rest variables.
    private static List<SemanticToken> Classify(string text, CancellationToken cancellationToken)
    {
        var tokens = new List<SemanticToken>(text.Length / 8);
        var position = 0;
        while (position < text.Length)
        {
            if ((position & 0xFFFF) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var c = text[position];
            if (c == '/' && position + 1 < text.Length && text[position + 1] == '/' || c == '#')
            {
                position = LineEnd(text, position);
            }
            else if (c == '"')
            {
                position++;
                while (position < text.Length && text[position] is not ('"' or '\n'))
                    position += text[position] == '\\' ? 2 : 1;
                position++;
            }
            else if (char.IsAsciiLetter(c) || c == '_')
            {
                var start = position;
                while (position < text.Length && IsNamePart(text[position]))
                    position++;

                var name = text.AsSpan(start, position - start);
                if (Keywords.GetAlternateLookup<ReadOnlySpan<char>>().Contains(name))
                    continue;

                var type = text.AsSpan(position).TrimStart(' ').StartsWith("(", StringComparison.Ordinal) ? SemanticTokenTypes.Method
                    : char.IsAsciiLetterUpper(name[0]) ? SemanticTokenTypes.Class
                    : Parameters.GetAlternateLookup<ReadOnlySpan<char>>().Contains(name) ? SemanticTokenTypes.Parameter
                    : SemanticTokenTypes.Variable;
                tokens.Add(new SemanticToken(new TextSpan(start, position - start), type));
            }
            else
            {
                position++;
            }
        }

        return tokens;
    }

    private string TextOf(TextSnapshot snapshot)
    {
        if (!ReferenceEquals(snapshot, lastSnapshot))
        {
            lastSnapshot = snapshot;
            lastText = snapshot.GetText();
            lastTokens = null;
        }

        return lastText;
    }

    private static int LineEnd(string text, int position)
    {
        var end = text.IndexOf('\n', position);
        return end < 0 ? text.Length : end;
    }

    private static bool IsNamePart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
