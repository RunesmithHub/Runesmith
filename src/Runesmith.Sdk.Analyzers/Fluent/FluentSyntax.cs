namespace Runesmith.Sdk.Analyzers.Fluent;

/// <summary>A range of a Fluent file's text, from <see cref="Start"/> up to but not including <see cref="End"/>.</summary>
internal readonly struct FluentSpan
{
    public FluentSpan(int start, int end)
    {
        Start = start;
        End = end < start ? start : end;
    }

    public int Start { get; }

    public int End { get; }

    public int Length => End - Start;
}

/// <summary>A syntax error: what is wrong, and where.</summary>
internal sealed class FluentError
{
    public FluentError(string message, FluentSpan span)
    {
        Message = message;
        Span = span;
    }

    public string Message { get; }

    public FluentSpan Span { get; }
}

/// <summary>A parsed Fluent file: its entries in order, and its syntax errors.</summary>
internal sealed class FluentResource
{
    private readonly int[] lineStarts;

    public FluentResource(string text, IReadOnlyList<FluentEntry> entries, IReadOnlyList<FluentError> errors)
    {
        Text = text;
        Entries = entries;
        Errors = errors;
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                starts.Add(i + 1);
        }

        lineStarts = [.. starts];
    }

    public string Text { get; }

    public IReadOnlyList<FluentEntry> Entries { get; }

    public IReadOnlyList<FluentError> Errors { get; }

    public IEnumerable<FluentMessage> Messages => Entries.OfType<FluentMessage>();

    public IEnumerable<FluentTerm> Terms => Entries.OfType<FluentTerm>();

    /// <summary>Gets the line and the column of an offset, both counted from 0.</summary>
    public (int Line, int Column) Position(int offset)
    {
        offset = Math.Max(0, Math.Min(offset, Text.Length));
        var line = Array.BinarySearch(lineStarts, offset);
        if (line < 0)
            line = ~line - 1;
        return (line, offset - lineStarts[line]);
    }
}

/// <summary>A top-level part of a Fluent file.</summary>
internal abstract class FluentEntry
{
    protected FluentEntry(FluentSpan span) => Span = span;

    public FluentSpan Span { get; }
}

/// <summary>A standalone comment: <c>#</c>, <c>##</c> for a group or <c>###</c> for the file, with its lines joined.</summary>
internal sealed class FluentComment : FluentEntry
{
    public FluentComment(int level, string text, FluentSpan span)
        : base(span)
    {
        Level = level;
        Text = text;
    }

    public int Level { get; }

    public string Text { get; }
}

/// <summary>Text that could not be parsed.</summary>
internal sealed class FluentJunk : FluentEntry
{
    public FluentJunk(FluentSpan span)
        : base(span)
    {
    }
}

/// <summary>A message or a term: an id, a value, attributes, and the comment written directly above it.</summary>
internal abstract class FluentDefinition : FluentEntry
{
    protected FluentDefinition(string id, FluentSpan idSpan, FluentPattern? value, IReadOnlyList<FluentAttribute> attributes, string? comment, FluentSpan span)
        : base(span)
    {
        Id = id;
        IdSpan = idSpan;
        Value = value;
        Attributes = attributes;
        Comment = comment;
    }

    public string Id { get; }

    public FluentSpan IdSpan { get; }

    public FluentPattern? Value { get; }

    public IReadOnlyList<FluentAttribute> Attributes { get; }

    public string? Comment { get; }
}

/// <summary>A message, such as <c>run-exited = The program exited with code { $code }.</c></summary>
internal sealed class FluentMessage : FluentDefinition
{
    public FluentMessage(string id, FluentSpan idSpan, FluentPattern? value, IReadOnlyList<FluentAttribute> attributes, string? comment, FluentSpan span)
        : base(id, idSpan, value, attributes, comment, span)
    {
    }
}

/// <summary>A term, such as <c>-brand = Runesmith</c>, which only other messages of the same language use.</summary>
internal sealed class FluentTerm : FluentDefinition
{
    public FluentTerm(string id, FluentSpan idSpan, FluentPattern value, IReadOnlyList<FluentAttribute> attributes, string? comment, FluentSpan span)
        : base(id, idSpan, value, attributes, comment, span)
    {
    }
}

/// <summary>An attribute of a message or a term, such as <c>.tooltip = Save the file</c>.</summary>
internal sealed class FluentAttribute
{
    public FluentAttribute(string id, FluentSpan idSpan, FluentPattern value)
    {
        Id = id;
        IdSpan = idSpan;
        Value = value;
    }

    public string Id { get; }

    public FluentSpan IdSpan { get; }

    public FluentPattern Value { get; }
}

/// <summary>The value of a message, term, attribute or variant: text and placeables.</summary>
internal sealed class FluentPattern
{
    public FluentPattern(IReadOnlyList<FluentPatternElement> elements) => Elements = elements;

    public IReadOnlyList<FluentPatternElement> Elements { get; }
}

internal abstract class FluentPatternElement
{
}

/// <summary>Text of a pattern, with its line breaks as <c>\n</c> and the common indentation removed.</summary>
internal sealed class FluentText : FluentPatternElement
{
    public FluentText(string value) => Value = value;

    public string Value { get; }
}

/// <summary>A placeable, <c>{ ... }</c>.</summary>
internal sealed class FluentPlaceable : FluentPatternElement
{
    public FluentPlaceable(FluentExpression expression) => Expression = expression;

    public FluentExpression Expression { get; }
}

internal abstract class FluentExpression
{
    protected FluentExpression(FluentSpan span) => Span = span;

    public FluentSpan Span { get; }
}

/// <summary>A quoted text, <c>"..."</c>; <see cref="Value"/> has its escapes resolved.</summary>
internal sealed class FluentStringLiteral : FluentExpression
{
    public FluentStringLiteral(string value, FluentSpan span)
        : base(span) => Value = value;

    public string Value { get; }
}

/// <summary>A number, such as <c>3</c> or <c>-1.5</c>, as written.</summary>
internal sealed class FluentNumberLiteral : FluentExpression
{
    public FluentNumberLiteral(string value, FluentSpan span)
        : base(span) => Value = value;

    public string Value { get; }
}

/// <summary>A variable, <c>$name</c>, whose value the code passes.</summary>
internal sealed class FluentVariableReference : FluentExpression
{
    public FluentVariableReference(string name, FluentSpan span)
        : base(span) => Name = name;

    public string Name { get; }
}

/// <summary>Another message, or one of its attributes: <c>{ other-message }</c>, <c>{ other-message.title }</c>.</summary>
internal sealed class FluentMessageReference : FluentExpression
{
    public FluentMessageReference(string id, string? attribute, FluentSpan span)
        : base(span)
    {
        Id = id;
        Attribute = attribute;
    }

    public string Id { get; }

    public string? Attribute { get; }
}

/// <summary>A term, or one of its attributes, with optional arguments: <c>{ -brand(case: "genitive") }</c>.</summary>
internal sealed class FluentTermReference : FluentExpression
{
    public FluentTermReference(string id, string? attribute, FluentArguments? arguments, FluentSpan span)
        : base(span)
    {
        Id = id;
        Attribute = attribute;
        Arguments = arguments;
    }

    public string Id { get; }

    public string? Attribute { get; }

    public FluentArguments? Arguments { get; }
}

/// <summary>A function call, such as <c>NUMBER($size, maximumFractionDigits: 1)</c>.</summary>
internal sealed class FluentFunctionReference : FluentExpression
{
    public FluentFunctionReference(string name, FluentArguments arguments, FluentSpan span)
        : base(span)
    {
        Name = name;
        Arguments = arguments;
    }

    public string Name { get; }

    public FluentArguments Arguments { get; }
}

/// <summary>A placeable inside a placeable, <c>{ { $x } }</c>.</summary>
internal sealed class FluentNestedPlaceable : FluentExpression
{
    public FluentNestedPlaceable(FluentExpression expression, FluentSpan span)
        : base(span) => Expression = expression;

    public FluentExpression Expression { get; }
}

/// <summary>A choice between variants by a selector, such as plural forms by a number.</summary>
internal sealed class FluentSelectExpression : FluentExpression
{
    public FluentSelectExpression(FluentExpression selector, IReadOnlyList<FluentVariant> variants, FluentSpan span)
        : base(span)
    {
        Selector = selector;
        Variants = variants;
    }

    public FluentExpression Selector { get; }

    public IReadOnlyList<FluentVariant> Variants { get; }
}

/// <summary>One variant of a select expression: <c>[one] One file</c>, or the default, <c>*[other] { $count } files</c>.</summary>
internal sealed class FluentVariant
{
    public FluentVariant(string key, bool isNumber, bool isDefault, FluentPattern value)
    {
        Key = key;
        IsNumber = isNumber;
        IsDefault = isDefault;
        Value = value;
    }

    public string Key { get; }

    public bool IsNumber { get; }

    public bool IsDefault { get; }

    public FluentPattern Value { get; }
}

/// <summary>The arguments of a function or term call.</summary>
internal sealed class FluentArguments
{
    public FluentArguments(IReadOnlyList<FluentExpression> positional, IReadOnlyList<FluentNamedArgument> named)
    {
        Positional = positional;
        Named = named;
    }

    public IReadOnlyList<FluentExpression> Positional { get; }

    public IReadOnlyList<FluentNamedArgument> Named { get; }
}

/// <summary>A named argument, such as <c>maximumFractionDigits: 1</c>.</summary>
internal sealed class FluentNamedArgument
{
    public FluentNamedArgument(string name, FluentExpression value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }

    public FluentExpression Value { get; }
}
