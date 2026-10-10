using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Runesmith.Sdk.Documents.Snippets;

/// <summary>A part of a parsed snippet.</summary>
internal abstract record SnippetNode;

/// <summary>Text inserted as it is.</summary>
internal sealed record SnippetText(string Text) : SnippetNode;

/// <summary>A tab stop: <c>$1</c>, a placeholder <c>${1:name}</c>, a choice <c>${1|a,b|}</c> or a transformed mirror
/// <c>${1/(.*)/${1:/upcase}/}</c>.</summary>
internal sealed record SnippetTabStop(int Index, IReadOnlyList<SnippetNode> Placeholder) : SnippetNode
{
    public IReadOnlyList<string>? Choices { get; init; }

    public SnippetTransform? Transform { get; init; }
}

/// <summary>A variable such as <c>$TM_FILENAME</c>, with the text used when it is empty and a transform.</summary>
internal sealed record SnippetVariable(string Name, IReadOnlyList<SnippetNode>? Default = null) : SnippetNode
{
    public SnippetTransform? Transform { get; init; }
}

/// <summary>A regular expression replacement applied to a variable's value or a tab stop's text.</summary>
internal sealed record SnippetTransform(Regex Pattern, IReadOnlyList<SnippetFormat> Format, bool IsGlobal)
{
    public string Apply(string value)
    {
        var replaced = 0;
        return Pattern.Replace(value, match =>
        {
            replaced++;
            return !IsGlobal && replaced > 1 ? match.Value : string.Concat(Format.Select(part => part.Render(match)));
        });
    }
}

/// <summary>A part of a transform's replacement.</summary>
internal abstract record SnippetFormat
{
    public abstract string Render(Match match);
}

internal sealed record SnippetFormatText(string Text) : SnippetFormat
{
    public override string Render(Match match) => Text;
}

/// <summary>A group of the match, changed in case, or chosen text depending on whether the group matched.</summary>
internal sealed record SnippetFormatGroup(int Group, string? Case = null, string? IfText = null, string? ElseText = null) : SnippetFormat
{
    public override string Render(Match match)
    {
        var group = Group < match.Groups.Count ? match.Groups[Group] : null;
        var value = group is { Success: true } ? group.Value : "";
        if (IfText is not null || ElseText is not null)
            return value.Length > 0 ? IfText ?? value : ElseText ?? "";

        return Case switch
        {
            "upcase" => value.ToUpperInvariant(),
            "downcase" => value.ToLowerInvariant(),
            "capitalize" => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..],
            "camelcase" => Words(value, upperFirst: false),
            "pascalcase" => Words(value, upperFirst: true),
            _ => value,
        };
    }

    private static string Words(string value, bool upperFirst)
    {
        var words = Regex.Split(value, @"[^\p{L}\p{N}]+", RegexOptions.None, TimeSpan.FromSeconds(1)).Where(w => w.Length > 0).ToList();
        var builder = new StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i];
            var upper = i > 0 || upperFirst;
            builder.Append(upper ? char.ToUpperInvariant(word[0]) : char.ToLowerInvariant(word[0])).Append(word.AsSpan(1));
        }

        return builder.ToString();
    }
}

/// <summary>Parses snippet syntax; what is not valid syntax is kept as text, so every text parses.</summary>
internal sealed class SnippetParser
{
    private readonly string text;
    private int position;

    private SnippetParser(string text) => this.text = text;

    public static IReadOnlyList<SnippetNode> Parse(string snippet)
    {
        ArgumentNullException.ThrowIfNull(snippet);
        return new SnippetParser(snippet.ReplaceLineEndings("\n")).ParseList(nested: false);
    }

    /// <summary>Whether the snippet uses a variable anywhere, such as <c>CLIPBOARD</c>, which takes a moment to read.</summary>
    public static bool Uses(IReadOnlyList<SnippetNode> nodes, string variable) => nodes.Any(node => node switch
    {
        SnippetVariable v => v.Name == variable || (v.Default is { } fallback && Uses(fallback, variable)),
        SnippetTabStop stop => Uses(stop.Placeholder, variable),
        _ => false,
    });

    private List<SnippetNode> ParseList(bool nested)
    {
        var nodes = new List<SnippetNode>();
        var pending = new StringBuilder();
        while (position < text.Length)
        {
            var c = text[position];
            if (nested && c == '}')
                break;

            if (c == '\\' && position + 1 < text.Length && text[position + 1] is '$' or '}' or '\\')
            {
                pending.Append(text[position + 1]);
                position += 2;
                continue;
            }

            if (c == '$' && TryParseDollar() is { } node)
            {
                Flush(pending, nodes);
                nodes.Add(node);
                continue;
            }

            pending.Append(c);
            position++;
        }

        Flush(pending, nodes);
        return nodes;
    }

    private static void Flush(StringBuilder pending, List<SnippetNode> nodes)
    {
        if (pending.Length == 0)
            return;

        if (nodes.Count > 0 && nodes[^1] is SnippetText previous)
            nodes[^1] = new SnippetText(previous.Text + pending);
        else
            nodes.Add(new SnippetText(pending.ToString()));
        pending.Clear();
    }

    private SnippetNode? TryParseDollar()
    {
        var start = position;
        position++;
        if (IsDigit(Peek()))
            return new SnippetTabStop(ReadNumber(), []);
        if (IsNameStart(Peek()))
            return new SnippetVariable(ReadName());

        if (Peek() == '{')
        {
            position++;
            SnippetNode? node = IsDigit(Peek()) ? ParseTabStopBody() : IsNameStart(Peek()) ? ParseVariableBody() : null;
            if (node is not null)
                return node;
        }

        position = start;
        return null;
    }

    private SnippetTabStop? ParseTabStopBody()
    {
        var index = ReadNumber();
        switch (Peek())
        {
            case '}':
                position++;
                return new SnippetTabStop(index, []);
            case ':':
                position++;
                var placeholder = ParseList(nested: true);
                if (Peek() != '}')
                    return null;
                position++;
                return new SnippetTabStop(index, placeholder);
            case '|':
                position++;
                if (ReadChoices() is not { Count: > 0 } choices)
                    return null;
                return new SnippetTabStop(index, [new SnippetText(choices[0])]) { Choices = choices };
            case '/':
                if (ReadTransform() is not { } transform || Peek() != '}')
                    return null;
                position++;
                return new SnippetTabStop(index, []) { Transform = transform };
            default:
                return null;
        }
    }

    private SnippetVariable? ParseVariableBody()
    {
        var name = ReadName();
        switch (Peek())
        {
            case '}':
                position++;
                return new SnippetVariable(name);
            case ':':
                position++;
                var fallback = ParseList(nested: true);
                if (Peek() != '}')
                    return null;
                position++;
                return new SnippetVariable(name, fallback);
            case '/':
                if (ReadTransform() is not { } transform || Peek() != '}')
                    return null;
                position++;
                return new SnippetVariable(name) { Transform = transform };
            default:
                return null;
        }
    }

    private List<string>? ReadChoices()
    {
        var choices = new List<string>();
        var current = new StringBuilder();
        while (position < text.Length)
        {
            var c = text[position];
            if (c == '\\' && position + 1 < text.Length && text[position + 1] is ',' or '|' or '\\' or '$' or '}')
            {
                current.Append(text[position + 1]);
                position += 2;
            }
            else if (c == ',')
            {
                choices.Add(current.ToString());
                current.Clear();
                position++;
            }
            else if (c == '|' && position + 1 < text.Length && text[position + 1] == '}')
            {
                choices.Add(current.ToString());
                position += 2;
                return choices;
            }
            else
            {
                current.Append(c);
                position++;
            }
        }

        return null;
    }

    private SnippetTransform? ReadTransform()
    {
        position++;
        if (ReadPattern() is not { } pattern)
            return null;

        var format = new List<SnippetFormat>();
        var literal = new StringBuilder();
        while (position < text.Length && text[position] != '/')
        {
            var c = text[position];
            if (c == '\\' && position + 1 < text.Length)
            {
                literal.Append(text[position + 1]);
                position += 2;
            }
            else if (c == '$' && ReadFormatGroup() is { } group)
            {
                if (literal.Length > 0)
                    format.Add(new SnippetFormatText(literal.ToString()));
                literal.Clear();
                format.Add(group);
            }
            else
            {
                literal.Append(c);
                position++;
            }
        }

        if (position >= text.Length)
            return null;

        position++;
        if (literal.Length > 0)
            format.Add(new SnippetFormatText(literal.ToString()));

        var options = new StringBuilder();
        while (position < text.Length && char.IsAsciiLetter(text[position]))
            options.Append(text[position++]);

        var flags = RegexOptions.None;
        if (options.ToString().Contains('i', StringComparison.Ordinal))
            flags |= RegexOptions.IgnoreCase;
        if (options.ToString().Contains('m', StringComparison.Ordinal))
            flags |= RegexOptions.Multiline;
        try
        {
            return new SnippetTransform(new Regex(pattern, flags, TimeSpan.FromSeconds(1)), format, options.ToString().Contains('g', StringComparison.Ordinal));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string? ReadPattern()
    {
        var builder = new StringBuilder();
        while (position < text.Length)
        {
            var c = text[position];
            if (c == '\\' && position + 1 < text.Length)
            {
                if (text[position + 1] == '/')
                    builder.Append('/');
                else
                    builder.Append(c).Append(text[position + 1]);
                position += 2;
                continue;
            }

            position++;
            if (c == '/')
                return builder.ToString();
            builder.Append(c);
        }

        return null;
    }

    private SnippetFormatGroup? ReadFormatGroup()
    {
        var start = position;
        position++;
        if (IsDigit(Peek()))
            return new SnippetFormatGroup(ReadNumber());

        if (Peek() == '{' && position + 1 < text.Length && IsDigit(text[position + 1]))
        {
            position++;
            var group = ReadNumber();
            if (Peek() == '}')
            {
                position++;
                return new SnippetFormatGroup(group);
            }

            if (Peek() == ':')
            {
                position++;
                var kind = Peek();
                if (kind == '/')
                {
                    position++;
                    var name = ReadName();
                    if (Peek() == '}')
                    {
                        position++;
                        return new SnippetFormatGroup(group, Case: name);
                    }
                }
                else if (kind == '+')
                {
                    position++;
                    if (ReadFormatText('}') is { } ifText)
                        return new SnippetFormatGroup(group, IfText: ifText);
                }
                else if (kind == '?')
                {
                    position++;
                    if (ReadFormatText(':') is { } ifText && ReadFormatText('}') is { } elseText)
                        return new SnippetFormatGroup(group, IfText: ifText, ElseText: elseText);
                }
                else
                {
                    if (kind == '-')
                        position++;
                    if (ReadFormatText('}') is { } elseText)
                        return new SnippetFormatGroup(group, ElseText: elseText);
                }
            }
        }

        position = start;
        return null;
    }

    private string? ReadFormatText(char end)
    {
        var builder = new StringBuilder();
        while (position < text.Length)
        {
            var c = text[position];
            if (c == '\\' && position + 1 < text.Length)
            {
                builder.Append(text[position + 1]);
                position += 2;
                continue;
            }

            position++;
            if (c == end)
                return builder.ToString();
            builder.Append(c);
        }

        return null;
    }

    private char Peek() => position < text.Length ? text[position] : '\0';

    private int ReadNumber()
    {
        var start = position;
        while (IsDigit(Peek()))
            position++;
        return int.TryParse(text.AsSpan(start, position - start), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;
    }

    private string ReadName()
    {
        var start = position;
        while (position < text.Length && (char.IsAsciiLetterOrDigit(text[position]) || text[position] == '_'))
            position++;
        return text[start..position];
    }

    private static bool IsDigit(char c) => char.IsAsciiDigit(c);

    private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c == '_';
}
