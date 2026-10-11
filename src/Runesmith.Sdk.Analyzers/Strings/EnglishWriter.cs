using System.Globalization;
using System.Text;
using Runesmith.Sdk.Analyzers.Fluent;

namespace Runesmith.Sdk.Analyzers.Strings;

/// <summary>Writes the <c>Strings</c> class's English localizer: each English message compiled to C#, which the class uses while Runesmith's
/// localization is not there, such as in a plugin's unit tests, so its text is the English source rather than a readable form of the id.</summary>
/// <remarks>It formats as Runesmith does in English: <c>NUMBER</c> and <c>DATETIME</c> with their options, the English plural forms, number
/// keys such as <c>[0]</c>, message and term references, and term arguments.</remarks>
internal static class EnglishWriter
{
    public const string ClassName = "EnglishText";

    private const string Sdk = "global::Runesmith.Sdk.Localization";

    public static void Write(StringBuilder code, string indent, IReadOnlyList<FluentMessage> messages, IReadOnlyList<FluentTerm> terms)
    {
        var termMethods = new Dictionary<string, string>(StringComparer.Ordinal);
        var termPatterns = new List<(string Method, FluentPattern Pattern)>();
        foreach (var term in terms)
        {
            if (termMethods.ContainsKey(term.Id))
                continue;

            Add(term.Id, term.Value);
            foreach (var attribute in term.Attributes)
                Add(term.Id + "." + attribute.Id, attribute.Value);
        }

        var writer = new Compiler(termMethods);
        var body = indent + "    ";
        var inner = body + "    ";
        code.Append(indent).AppendLine("/// <summary>The English messages, for when Runesmith's localization is not there, such as in unit tests.</summary>");
        code.Append(indent).AppendLine($"private sealed class {ClassName} : {Sdk}.ILocalizer");
        code.Append(indent).AppendLine("{");
        code.Append(body).AppendLine($"public static readonly {ClassName} Instance = new();");
        code.AppendLine();
        code.Append(body).AppendLine("private static readonly global::System.Globalization.CultureInfo Culture = global::System.Globalization.CultureInfo.GetCultureInfo(\"en-US\");");
        code.AppendLine();
        code.Append(body).AppendLine($"public string Get(string id, {Sdk}.LocalizationArgs? args = null) => Find(id, args) ?? Readable(id);");
        code.AppendLine();
        code.Append(body).AppendLine($"public bool TryGet(string id, {Sdk}.LocalizationArgs? args, [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)");
        code.Append(body).AppendLine("{");
        code.Append(inner).AppendLine("text = Find(id, args);");
        code.Append(inner).AppendLine("return text is not null;");
        code.Append(body).AppendLine("}");
        code.AppendLine();
        code.Append(body).AppendLine($"private static string? Find(string id, {Sdk}.LocalizationArgs? args) => id switch");
        code.Append(body).AppendLine("{");
        foreach (var message in messages)
        {
            if (message.Value is { } value)
                code.Append(inner).AppendLine($"{Literal(message.Id)} => {writer.Pattern(value, "args")},");
            foreach (var attribute in message.Attributes)
                code.Append(inner).AppendLine($"{Literal(message.Id + "." + attribute.Id)} => {writer.Pattern(attribute.Value, "args")},");
        }

        code.Append(inner).AppendLine("_ => null,");
        code.Append(body).AppendLine("};");
        foreach (var (method, pattern) in termPatterns)
        {
            code.AppendLine();
            code.Append(body).AppendLine($"private static string {method}({Sdk}.LocalizationArgs? args) => {writer.Pattern(pattern, "args")};");
        }

        code.AppendLine();
        foreach (var line in Helpers.Split('\n'))
            code.Append(line.Length == 0 ? "" : body).AppendLine(line.TrimEnd('\r'));
        code.Append(indent).AppendLine("}");

        void Add(string id, FluentPattern? pattern)
        {
            if (pattern is null)
                return;

            var method = "Term" + termMethods.Count.ToString(CultureInfo.InvariantCulture);
            termMethods[id] = method;
            termPatterns.Add((method, pattern));
        }
    }

    internal static string Literal(string text)
    {
        var literal = new StringBuilder(text.Length + 2).Append('"');
        foreach (var character in text)
        {
            literal.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when char.IsControl(character) || char.IsSurrogate(character) => "\\u" + ((int)character).ToString("x4", CultureInfo.InvariantCulture),
                _ => character.ToString(),
            });
        }

        return literal.Append('"').ToString();
    }

    private sealed class Compiler(IReadOnlyDictionary<string, string> terms)
    {
        public string Pattern(FluentPattern pattern, string args)
        {
            var parts = pattern.Elements.Select(element => element switch
            {
                FluentText text => Literal(text.Value),
                FluentPlaceable placeable => Text(placeable.Expression, args),
                _ => "\"\"",
            }).ToList();
            return parts.Count switch
            {
                0 => "\"\"",
                1 => parts[0],
                _ => "string.Concat(new string[] { " + string.Join(", ", parts) + " })",
            };
        }

        private string Text(FluentExpression expression, string args) => expression switch
        {
            FluentStringLiteral text => Literal(text.Value),
            FluentNumberLiteral number => $"Number({Number(number)}, null, null, null, null, true)",
            FluentVariableReference variable => $"Format(Arg({args}, {Literal(variable.Name)}))",
            FluentMessageReference reference => $"(Find({Literal(Id(reference.Id, reference.Attribute))}, {args}) ?? {Literal(Readable(reference.Id))})",
            FluentTermReference term => Term(term),
            FluentFunctionReference { Name: "NUMBER" } call => $"Number({Positional(call, args)}, {Option(call, "style")}, {Count(call, "minimumFractionDigits")}, " +
                $"{Count(call, "maximumFractionDigits")}, {Count(call, "minimumIntegerDigits")}, {(Option(call, "useGrouping") is "\"false\"" or "\"0\"" ? "false" : "true")})",
            FluentFunctionReference { Name: "DATETIME" } call => $"Date({Positional(call, args)}, {Option(call, "dateStyle")}, {Option(call, "timeStyle")})",
            FluentFunctionReference call => $"Format({Positional(call, args)})",
            FluentNestedPlaceable nested => Text(nested.Expression, args),
            FluentSelectExpression select => Select(select, args),
            _ => "\"\"",
        };

        private string Value(FluentExpression expression, string args) => expression switch
        {
            FluentVariableReference variable => $"Arg({args}, {Literal(variable.Name)})",
            FluentNumberLiteral number => $"(object){Number(number)}",
            FluentStringLiteral text => Literal(text.Value),
            FluentFunctionReference { Name: "NUMBER" } call => Positional(call, args),
            FluentNestedPlaceable nested => Value(nested.Expression, args),
            _ => Text(expression, args),
        };

        private string Select(FluentSelectExpression select, string args)
        {
            var fallback = Math.Max(0, select.Variants.ToList().FindIndex(variant => variant.IsDefault));
            var keys = string.Join(", ", select.Variants.Select(variant => Literal(variant.Key)));
            var arms = select.Variants.Select((variant, index) => $"{index.ToString(CultureInfo.InvariantCulture)} => {Pattern(variant.Value, args)}");
            var last = select.Variants.Count == 0 ? "\"\"" : Pattern(select.Variants[fallback].Value, args);
            return $"(Choose({Value(select.Selector, args)}, {fallback.ToString(CultureInfo.InvariantCulture)}, new string[] {{ {keys} }}) switch {{ " +
                string.Join(", ", arms.Concat(["_ => " + last])) + " })";
        }

        private string Term(FluentTermReference term)
        {
            var id = Id(term.Id, term.Attribute);
            if (!terms.TryGetValue(id, out var method))
                return Literal(Readable(term.Id));

            var named = term.Arguments?.Named ?? [];
            var arguments = named.Count == 0
                ? "null"
                : $"new {Sdk}.LocalizationArgs()" + string.Concat(named.Select(argument => argument.Value switch
                {
                    FluentNumberLiteral number => $".Add({Literal(argument.Name)}, {Number(number)})",
                    FluentStringLiteral text => $".Add({Literal(argument.Name)}, {Literal(text.Value)})",
                    _ => "",
                }));
            return $"{method}({arguments})";
        }

        private string Positional(FluentFunctionReference call, string args) =>
            call.Arguments.Positional.Count == 0 ? "null" : Value(call.Arguments.Positional[0], args);

        private static string Option(FluentFunctionReference call, string name) =>
            call.Arguments.Named.FirstOrDefault(argument => argument.Name == name)?.Value switch
            {
                FluentStringLiteral text => Literal(text.Value),
                FluentNumberLiteral number => Literal(number.Value),
                _ => "null",
            };

        private static string Count(FluentFunctionReference call, string name) =>
            call.Arguments.Named.FirstOrDefault(argument => argument.Name == name)?.Value is FluentNumberLiteral number
            && int.TryParse(number.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count is >= 0 and <= 20
                ? count.ToString(CultureInfo.InvariantCulture)
                : "null";

        private static string Number(FluentNumberLiteral number) =>
            double.TryParse(number.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? "(" + value.ToString("R", CultureInfo.InvariantCulture) + "d)"
                : "0d";

        private static string Id(string id, string? attribute) => attribute is null ? id : id + "." + attribute;
    }

    // Mirrors Localized.Readable, for a reference to a message that is not in the English source.
    private static string Readable(string id)
    {
        var words = new StringBuilder(id.Length);
        foreach (var character in id)
        {
            var separator = character is '-' or '_' or '.';
            if (separator && (words.Length == 0 || words[words.Length - 1] == ' '))
                continue;

            words.Append(separator ? ' ' : words.Length == 0 ? char.ToUpperInvariant(character) : character);
        }

        var text = words.ToString().TrimEnd();
        return text.Length > 0 ? text : "?";
    }

    private const string Helpers = """
        private static string Readable(string id)
        {
            var words = new global::System.Text.StringBuilder(id.Length);
            foreach (var character in id)
            {
                var separator = character is '-' or '_' or '.';
                if (separator && (words.Length == 0 || words[words.Length - 1] == ' '))
                    continue;

                words.Append(separator ? ' ' : words.Length == 0 ? char.ToUpperInvariant(character) : character);
            }

            var text = words.ToString().TrimEnd();
            return text.Length > 0 ? text : "?";
        }

        private static object? Arg(global::Runesmith.Sdk.Localization.LocalizationArgs? args, string name) =>
            args is not null && args.TryGetValue(name, out var value) ? value : null;

        private static string Format(object? value) => value switch
        {
            null => "",
            string text => text,
            global::System.DateTimeOffset date => Date(date, null, null),
            _ => Number(value, null, null, null, null, true),
        };

        private static double? AsNumber(object? value) => value switch
        {
            long whole => whole,
            int whole => whole,
            double number => number,
            decimal exact => (double)exact,
            string text when double.TryParse(text, global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };

        private static string Number(object? value, string? style, int? minimum, int? maximum, int? integerDigits, bool grouping)
        {
            if (AsNumber(value) is not { } number)
                return value as string ?? "";

            var low = minimum ?? 0;
            var high = global::System.Math.Max(low, maximum ?? (style == "percent" ? 0 : style == "currency" ? 2 : 3));
            if (style == "percent")
            {
                var format = (global::System.Globalization.NumberFormatInfo)Culture.NumberFormat.Clone();
                format.PercentDecimalDigits = high;
                if (!grouping)
                    format.PercentGroupSizes = new[] { 0 };
                return number.ToString("P", format);
            }

            var integer = new string('0', global::System.Math.Max(1, integerDigits ?? 1));
            var whole = grouping ? "#," + integer.PadLeft(global::System.Math.Max(integer.Length, 3), '#') : integer;
            var pattern = high == 0 ? whole : whole + "." + new string('0', low) + new string('#', high - low);
            return value switch
            {
                long exactWhole => exactWhole.ToString(pattern, Culture),
                decimal exact => exact.ToString(pattern, Culture),
                _ => number.ToString(pattern, Culture),
            };
        }

        private static string Date(object? value, string? dateStyle, string? timeStyle)
        {
            if (value is not global::System.DateTimeOffset given)
                return value as string ?? "";

            var local = given.ToLocalTime().DateTime;
            var date = dateStyle switch
            {
                "full" => local.ToString("D", Culture),
                "long" => local.ToString("MMMM d, yyyy", Culture),
                "medium" => local.ToString("MMM d, yyyy", Culture),
                "short" => local.ToString("d", Culture),
                _ => null,
            };
            var time = timeStyle switch
            {
                "short" => local.ToString("t", Culture),
                "medium" or "long" or "full" => local.ToString("T", Culture),
                _ => null,
            };
            return date is not null && time is not null ? date + " " + time : date ?? time ?? local.ToString("d", Culture);
        }

        private static int Choose(object? value, int fallback, string[] keys)
        {
            if (AsNumber(value) is { } number && value is not string)
            {
                for (var index = 0; index < keys.Length; index++)
                {
                    if (double.TryParse(keys[index], global::System.Globalization.NumberStyles.Float, global::System.Globalization.CultureInfo.InvariantCulture, out var key) && key == number)
                        return index;
                }

                var category = number == 1 ? "one" : "other";
                return global::System.Array.IndexOf(keys, category) is var found and >= 0 ? found : fallback;
            }

            var text = value as string ?? Format(value);
            return global::System.Array.IndexOf(keys, text) is var match and >= 0 ? match : fallback;
        }
        """;
}
