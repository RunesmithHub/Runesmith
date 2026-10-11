using System.Text;
using System.Text.RegularExpressions;

namespace Runesmith.Sdk.Analyzers.Fluent;

/// <summary>What kind of value a message's variable takes, from how the message uses it.</summary>
internal enum FluentVariableType
{
    Text,
    Number,
    Date,
}

/// <summary>A variable a message uses: its name without <c>$</c>, the type its uses imply, and where it first appears.</summary>
internal sealed class FluentVariable
{
    public FluentVariable(string name, FluentVariableType type, FluentSpan span)
    {
        Name = name;
        Type = type;
        Span = span;
    }

    public string Name { get; }

    public FluentVariableType Type { get; }

    public FluentSpan Span { get; }
}

/// <summary>Reads what patterns use: their variables, the messages and terms they refer to, and their text for documentation.</summary>
internal static class FluentAnalysis
{
    private static readonly HashSet<string> PluralCategories = new(StringComparer.Ordinal) { "zero", "one", "two", "few", "many" };
    private static readonly Regex VariableLine = new(@"^\s*\$([a-zA-Z][a-zA-Z0-9_-]*)\s*(?:\(([^)]*)\))?\s*(?:[-:]\s*)?(.*)$", RegexOptions.CultureInvariant);

    /// <summary>Gets the variables of a message or term, its value and attributes together, in the order they first appear.</summary>
    public static IReadOnlyList<FluentVariable> Variables(FluentDefinition definition)
    {
        var found = new List<(string Name, FluentVariableType Type, FluentSpan Span)>();
        if (definition.Value is { } value)
            Collect(value, found);
        foreach (var attribute in definition.Attributes)
            Collect(attribute.Value, found);
        return Merge(found);
    }

    /// <summary>Gets the variables of one pattern, in the order they first appear.</summary>
    public static IReadOnlyList<FluentVariable> Variables(FluentPattern pattern)
    {
        var found = new List<(string Name, FluentVariableType Type, FluentSpan Span)>();
        Collect(pattern, found);
        return Merge(found);
    }

    /// <summary>Gets the messages and terms a pattern refers to; term ids start with <c>-</c>.</summary>
    public static IEnumerable<(string Id, string? Attribute, FluentSpan Span)> References(FluentPattern pattern)
    {
        foreach (var expression in Expressions(pattern))
        {
            switch (expression)
            {
                case FluentMessageReference message:
                    yield return (message.Id, message.Attribute, message.Span);
                    break;
                case FluentTermReference term:
                    yield return ("-" + term.Id, term.Attribute, term.Span);
                    break;
            }
        }
    }

    /// <summary>Splits a message's comment into its description and the variables it describes in a <c>Variables:</c> section, as
    /// <c>$count (Number) - how many files</c>.</summary>
    public static (string Summary, IReadOnlyDictionary<string, (string? Type, string Description)> Variables) ReadComment(string? comment)
    {
        var summary = new List<string>();
        var variables = new Dictionary<string, (string?, string)>(StringComparer.Ordinal);
        var inVariables = false;
        foreach (var line in (comment ?? "").Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.Equals(trimmed.TrimEnd(':'), "Variables", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith(":", StringComparison.Ordinal))
            {
                inVariables = true;
                continue;
            }

            var match = VariableLine.Match(line);
            if (match.Success && (inVariables || match.Groups[2].Success))
            {
                variables[match.Groups[1].Value] = (match.Groups[2].Success ? match.Groups[2].Value.Trim() : null, match.Groups[3].Value.Trim());
                continue;
            }

            if (inVariables && trimmed.Length > 0 && variables.Count > 0)
                continue;
            summary.Add(trimmed);
        }

        return (string.Join(" ", summary.Where(s => s.Length > 0)), variables);
    }

    /// <summary>Gets a variable type named in a comment, such as <c>Number</c>, or null when the name is not one.</summary>
    public static FluentVariableType? ParseType(string? name) => name?.Trim().ToUpperInvariant() switch
    {
        "NUMBER" or "INT" or "INTEGER" or "FLOAT" or "DOUBLE" => FluentVariableType.Number,
        "DATE" or "DATETIME" or "TIME" => FluentVariableType.Date,
        "STRING" or "TEXT" => FluentVariableType.Text,
        _ => null,
    };

    /// <summary>Writes a pattern back as Fluent source on one line, for documentation.</summary>
    public static string Render(FluentPattern pattern)
    {
        var builder = new StringBuilder();
        Render(pattern, builder);
        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    private static void Render(FluentPattern pattern, StringBuilder builder)
    {
        foreach (var element in pattern.Elements)
        {
            if (element is FluentText text)
            {
                builder.Append(text.Value);
                continue;
            }

            builder.Append("{ ");
            Render(((FluentPlaceable)element).Expression, builder);
            builder.Append(" }");
        }
    }

    private static void Render(FluentExpression expression, StringBuilder builder)
    {
        switch (expression)
        {
            case FluentStringLiteral literal:
                builder.Append('"').Append(literal.Value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                break;
            case FluentNumberLiteral number:
                builder.Append(number.Value);
                break;
            case FluentVariableReference variable:
                builder.Append('$').Append(variable.Name);
                break;
            case FluentMessageReference message:
                builder.Append(message.Id);
                if (message.Attribute is { } messageAttribute)
                    builder.Append('.').Append(messageAttribute);
                break;
            case FluentTermReference term:
                builder.Append('-').Append(term.Id);
                if (term.Attribute is { } termAttribute)
                    builder.Append('.').Append(termAttribute);
                if (term.Arguments is { } termArguments)
                    Render(termArguments, builder);
                break;
            case FluentFunctionReference function:
                builder.Append(function.Name);
                Render(function.Arguments, builder);
                break;
            case FluentNestedPlaceable nested:
                builder.Append("{ ");
                Render(nested.Expression, builder);
                builder.Append(" }");
                break;
            case FluentSelectExpression select:
                Render(select.Selector, builder);
                builder.Append(" ->");
                foreach (var variant in select.Variants)
                {
                    builder.Append(' ').Append(variant.IsDefault ? "*[" : "[").Append(variant.Key).Append("] ");
                    Render(variant.Value, builder);
                }

                break;
        }
    }

    private static void Render(FluentArguments arguments, StringBuilder builder)
    {
        builder.Append('(');
        var first = true;
        foreach (var argument in arguments.Positional)
        {
            if (!first)
                builder.Append(", ");
            first = false;
            Render(argument, builder);
        }

        foreach (var argument in arguments.Named)
        {
            if (!first)
                builder.Append(", ");
            first = false;
            builder.Append(argument.Name).Append(": ");
            Render(argument.Value, builder);
        }

        builder.Append(')');
    }

    private static IEnumerable<FluentExpression> Expressions(FluentPattern pattern)
    {
        foreach (var element in pattern.Elements)
        {
            if (element is FluentPlaceable placeable)
            {
                foreach (var expression in Expressions(placeable.Expression))
                    yield return expression;
            }
        }
    }

    private static IEnumerable<FluentExpression> Expressions(FluentExpression expression)
    {
        yield return expression;
        IEnumerable<FluentExpression> children = expression switch
        {
            FluentNestedPlaceable nested => Expressions(nested.Expression),
            FluentFunctionReference function => Arguments(function.Arguments),
            FluentTermReference { Arguments: { } arguments } => Arguments(arguments),
            FluentSelectExpression select => Expressions(select.Selector).Concat(select.Variants.SelectMany(v => Expressions(v.Value))),
            _ => [],
        };
        foreach (var child in children)
            yield return child;
    }

    private static IEnumerable<FluentExpression> Arguments(FluentArguments arguments) =>
        arguments.Positional.Concat(arguments.Named.Select(n => n.Value)).SelectMany(Expressions);

    private static void Collect(FluentPattern pattern, List<(string, FluentVariableType, FluentSpan)> found)
    {
        foreach (var element in pattern.Elements)
        {
            if (element is FluentPlaceable placeable)
                Collect(placeable.Expression, FluentVariableType.Text, found);
        }
    }

    private static void Collect(FluentExpression expression, FluentVariableType context, List<(string, FluentVariableType, FluentSpan)> found)
    {
        switch (expression)
        {
            case FluentVariableReference variable:
                found.Add((variable.Name, context, variable.Span));
                break;
            case FluentNestedPlaceable nested:
                Collect(nested.Expression, context, found);
                break;
            case FluentFunctionReference function:
            {
                var type = function.Name switch
                {
                    "NUMBER" => FluentVariableType.Number,
                    "DATETIME" => FluentVariableType.Date,
                    _ => FluentVariableType.Text,
                };
                for (var i = 0; i < function.Arguments.Positional.Count; i++)
                    Collect(function.Arguments.Positional[i], i == 0 ? type : FluentVariableType.Text, found);
                foreach (var named in function.Arguments.Named)
                    Collect(named.Value, FluentVariableType.Text, found);
                break;
            }

            case FluentTermReference { Arguments: { } arguments }:
                foreach (var argument in arguments.Positional.Concat(arguments.Named.Select(n => n.Value)))
                    Collect(argument, FluentVariableType.Text, found);
                break;
            case FluentSelectExpression select:
                var numeric = select.Variants.Any(v => v.IsNumber || PluralCategories.Contains(v.Key));
                Collect(select.Selector, numeric ? FluentVariableType.Number : FluentVariableType.Text, found);
                foreach (var variant in select.Variants)
                    Collect(variant.Value, found);
                break;
        }
    }

    private static List<FluentVariable> Merge(List<(string Name, FluentVariableType Type, FluentSpan Span)> found)
    {
        var order = new List<string>();
        var types = new Dictionary<string, (FluentVariableType Type, FluentSpan Span)>(StringComparer.Ordinal);
        foreach (var (name, type, span) in found)
        {
            if (!types.TryGetValue(name, out var known))
            {
                order.Add(name);
                types[name] = (type, span);
            }
            else if (type == FluentVariableType.Date || (type == FluentVariableType.Number && known.Type == FluentVariableType.Text))
            {
                types[name] = (type, known.Span);
            }
        }

        return [.. order.Select(name => new FluentVariable(name, types[name].Type, types[name].Span))];
    }
}
