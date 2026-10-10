using System.Text;
using Runesmith.Text;

namespace Runesmith.Sdk.Documents.Snippets;

/// <summary>One place of a tab stop in the expanded text.</summary>
/// <param name="Index">The tab stop's number; 0 is the final position.</param>
/// <param name="Span">Where it is, relative to the start of the expanded text.</param>
/// <param name="Choices">The choices offered at it, if any.</param>
/// <param name="Transform">How a mirror's text is made from the tab stop's text, if it is a transformed mirror.</param>
internal sealed record SnippetStop(int Index, TextSpan Span, IReadOnlyList<string>? Choices = null, SnippetTransform? Transform = null);

/// <summary>A snippet turned into text, with its tab stops in order of where they are.</summary>
internal sealed record SnippetExpansion(string Text, IReadOnlyList<SnippetStop> Stops)
{
    /// <summary>Gets whether there is a tab stop to move between, other than the final position.</summary>
    public bool HasTabStops => Stops.Any(s => s.Index != 0);

    /// <summary>Gets the offset of the final position: <c>$0</c>, or the end of the text without one.</summary>
    public int FinalOffset => Stops.FirstOrDefault(s => s.Index == 0)?.Span.Start ?? Text.Length;

    /// <summary>Turns parsed snippet syntax into text.</summary>
    /// <param name="resolve">Gets a variable's value, or null for a variable that does not exist, which becomes a placeholder with its
    /// name.</param>
    /// <param name="lineIndent">The indentation of the line the snippet goes in, added after each line break.</param>
    /// <param name="indentUnit">What a tab in the snippet becomes, such as four spaces.</param>
    public static SnippetExpansion Create(IReadOnlyList<SnippetNode> nodes, Func<string, string?> resolve, string lineIndent = "", string indentUnit = "\t")
    {
        var builder = new Expander(resolve, lineIndent, indentUnit);
        var known = builder.Prepare(nodes);
        var text = new StringBuilder();
        var stops = new List<SnippetStop>();
        builder.Emit(known, text, stops);
        stops.Sort((a, b) => a.Span.Start != b.Span.Start ? a.Span.Start.CompareTo(b.Span.Start) : b.Span.Length.CompareTo(a.Span.Length));
        return new SnippetExpansion(text.ToString(), stops);
    }

    /// <summary>Expands a snippet into plain text, for editors without tab stops.</summary>
    public static SnippetExpansion Create(string snippet, Func<string, string?> resolve) => Create(SnippetParser.Parse(snippet), resolve);

    private sealed class Expander(Func<string, string?> resolve, string lineIndent, string indentUnit)
    {
        private readonly Dictionary<int, SnippetTabStop> definitions = [];
        private readonly Dictionary<string, string?> values = new(StringComparer.Ordinal);
        private int nextIndex;

        /// <summary>Turns variables that do not exist into placeholders and finds the tab stop that gives each number its text.</summary>
        public List<SnippetNode> Prepare(IReadOnlyList<SnippetNode> nodes)
        {
            nextIndex = MaxIndex(nodes) + 1;
            var rewritten = Rewrite(nodes);
            Collect(rewritten);
            return rewritten;
        }

        public void Emit(IReadOnlyList<SnippetNode> nodes, StringBuilder text, List<SnippetStop>? stops)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case SnippetText plain:
                        AppendIndented(text, plain.Text.Replace("\t", indentUnit, StringComparison.Ordinal));
                        break;
                    case SnippetVariable variable:
                        var value = Value(variable.Name) ?? "";
                        if (value.Length == 0 && variable.Default is { } fallback)
                            Emit(fallback, text, stops);
                        else
                            AppendIndented(text, variable.Transform?.Apply(value) ?? value);
                        break;
                    case SnippetTabStop stop:
                        EmitTabStop(stop, text, stops);
                        break;
                }
            }
        }

        private void EmitTabStop(SnippetTabStop stop, StringBuilder text, List<SnippetStop>? stops)
        {
            var start = text.Length;
            var definition = definitions.GetValueOrDefault(stop.Index);
            if (stop.Transform is { } transform)
            {
                text.Append(transform.Apply(definition is null ? "" : Render(definition.Placeholder)));
            }
            else if (stop.Placeholder.Count > 0 && ReferenceEquals(definition, stop))
            {
                Emit(stop.Placeholder, text, stops);
            }
            else if (definition is not null)
            {
                Emit(definition.Placeholder, text, null);
            }

            stops?.Add(new SnippetStop(stop.Index, TextSpan.FromBounds(start, text.Length), ReferenceEquals(definition, stop) ? stop.Choices : null, stop.Transform));
        }

        private string Render(IReadOnlyList<SnippetNode> nodes)
        {
            var text = new StringBuilder();
            Emit(nodes, text, null);
            return text.ToString();
        }

        private void AppendIndented(StringBuilder text, string value)
        {
            if (lineIndent.Length == 0 || !value.Contains('\n', StringComparison.Ordinal))
                text.Append(value);
            else
                text.Append(value.Replace("\n", "\n" + lineIndent, StringComparison.Ordinal));
        }

        private string? Value(string name)
        {
            if (!values.TryGetValue(name, out var value))
            {
                value = resolve(name);
                values[name] = value;
            }

            return value;
        }

        private List<SnippetNode> Rewrite(IReadOnlyList<SnippetNode> nodes)
        {
            var rewritten = new List<SnippetNode>(nodes.Count);
            foreach (var node in nodes)
            {
                rewritten.Add(node switch
                {
                    SnippetVariable { Default: null } variable when Value(variable.Name) is null =>
                        new SnippetTabStop(nextIndex++, [new SnippetText(variable.Name)]),
                    SnippetVariable { Default: { } fallback } variable => new SnippetVariable(variable.Name, Rewrite(fallback)) { Transform = variable.Transform },
                    SnippetTabStop stop => stop with { Placeholder = Rewrite(stop.Placeholder) },
                    _ => node,
                });
            }

            return rewritten;
        }

        private void Collect(IReadOnlyList<SnippetNode> nodes)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case SnippetTabStop stop:
                        if (stop.Transform is null && (stop.Placeholder.Count > 0 || stop.Choices is not null)
                            && (!definitions.TryGetValue(stop.Index, out var known) || known.Placeholder.Count == 0))
                            definitions[stop.Index] = stop;
                        else if (stop.Transform is null)
                            definitions.TryAdd(stop.Index, stop);
                        Collect(stop.Placeholder);
                        break;
                    case SnippetVariable { Default: { } fallback }:
                        Collect(fallback);
                        break;
                }
            }
        }

        private static int MaxIndex(IReadOnlyList<SnippetNode> nodes)
        {
            var max = 0;
            foreach (var node in nodes)
            {
                if (node is SnippetTabStop stop)
                    max = Math.Max(max, Math.Max(stop.Index == int.MaxValue ? 0 : stop.Index, MaxIndex(stop.Placeholder)));
                else if (node is SnippetVariable { Default: { } fallback })
                    max = Math.Max(max, MaxIndex(fallback));
            }

            return max;
        }
    }
}
