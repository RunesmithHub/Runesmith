using System.Globalization;
using System.Text;

namespace Runesmith.Sdk.Analyzers.Fluent;

/// <summary>Reads Fluent (<c>.ftl</c>) files as Runesmith's Fluent runtime reads them, and says where and why a part is not valid.</summary>
/// <remarks>Follows the Fluent 1.0 syntax and accepts what the runtime accepts: continuation lines are indented with spaces, function names
/// are upper case, and a term's attributes are only selectors. A part that does not parse is skipped up to the next line that starts with a
/// letter, <c>-</c> or <c>#</c>, as junk.</remarks>
internal sealed class FluentParser
{
    private readonly string text;
    private readonly List<FluentEntry> entries = [];
    private readonly List<FluentError> errors = [];
    private int position;

    private FluentParser(string text) => this.text = text;

    private enum Role
    {
        InitialLineStart,
        LineStart,
        Continuation,
    }

    private enum Ending
    {
        LineFeed,
        CarriageReturnLineFeed,
        PlaceableStart,
        EndOfFile,
    }

    private bool IsEof => position >= text.Length;

    private char Current => position < text.Length ? text[position] : '\0';

    /// <summary>Parses the text of a Fluent file.</summary>
    public static FluentResource Parse(string text)
    {
        var parser = new FluentParser(text ?? "");
        parser.ParseResource();
        return new FluentResource(parser.text, parser.entries, parser.errors);
    }

    /// <summary>Gets whether a name can be a message or attribute id: a letter, then letters, digits, <c>-</c> and <c>_</c>.</summary>
    public static bool IsIdentifier(string name)
    {
        if (name.Length == 0 || !IsLetter(name[0]))
            return false;

        for (var i = 1; i < name.Length; i++)
        {
            if (!IsIdentifierPart(name[i]))
                return false;
        }

        return true;
    }

    private static bool IsLetter(char character) => character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    private static bool IsDigit(char character) => character is >= '0' and <= '9';

    private static bool IsIdentifierPart(char character) => IsLetter(character) || IsDigit(character) || character is '-' or '_';

    private static bool IsCallee(string name) => name.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-');

    private static bool IsHexDigit(char character) => IsDigit(character) || character is (>= 'a' and <= 'f') or (>= 'A' and <= 'F');

    private char Peek(int offset)
    {
        var index = position + offset;
        return index >= 0 && index < text.Length ? text[index] : '\0';
    }

    private void ParseResource()
    {
        if (text.Length > 0 && text[0] == '﻿')
            position = 1;

        SkipBlankBlock();
        FluentComment? pending = null;
        var pendingIsAdjacent = false;
        while (!IsEof)
        {
            var start = position;
            if (Current == '#')
            {
                if (pending is not null)
                    entries.Add(pending);
                pending = null;
                try
                {
                    pending = ParseComment();
                    pendingIsAdjacent = SkipBlankBlock() == 0;
                }
                catch (ParseFailure failure)
                {
                    Recover(start, failure);
                }

                continue;
            }

            string? comment = null;
            if (pending is not null)
            {
                if (pending.Level == 1 && pendingIsAdjacent)
                    comment = pending.Text;
                else
                    entries.Add(pending);
                pending = null;
            }

            try
            {
                entries.Add(Current switch
                {
                    '-' => ParseTerm(start, comment),
                    _ when IsLetter(Current) => ParseMessage(start, comment),
                    '\t' => throw Failure("Fluent indents with spaces, not tabs.", position),
                    ' ' => throw Failure("Expected a message, a term or a comment at the start of the line; only the lines that continue a value are indented.", position),
                    _ => throw Failure("A message id starts with a letter, such as hello-world.", position),
                });
            }
            catch (ParseFailure failure)
            {
                Recover(start, failure);
            }

            SkipBlankBlock();
        }

        if (pending is not null)
            entries.Add(pending);
    }

    private void Recover(int start, ParseFailure failure)
    {
        errors.Add(new FluentError(failure.Message, failure.Span));
        position = Math.Max(position, Math.Min(failure.Span.Start, text.Length));
        while (position < text.Length)
        {
            var atLineStart = position == 0 || text[position - 1] == '\n';
            if (atLineStart && position > start && (IsLetter(text[position]) || text[position] is '#' or '-'))
                break;
            position++;
        }

        entries.Add(new FluentJunk(new FluentSpan(start, position)));
    }

    private ParseFailure Failure(string message, int at) => new(message, new FluentSpan(at, Math.Min(at + 1, text.Length)));

    private FluentComment ParseComment()
    {
        var start = position;
        var level = 0;
        while (Peek(level) == '#')
            level++;
        if (level > 3)
            throw Failure("A comment starts with #, ## or ### and a space.", position + 3);

        var lines = new List<string>();
        while (!IsEof)
        {
            var lineStart = position;
            var hashes = 0;
            while (Peek(hashes) == '#')
                hashes++;
            if (hashes != level)
                break;

            position += hashes;
            if (IsLineEndOrEof())
            {
                lines.Add("");
            }
            else if (Current == ' ')
            {
                position++;
                var contentStart = position;
                while (!IsLineEndOrEof())
                    position++;
                lines.Add(text.Substring(contentStart, position - contentStart));
            }
            else
            {
                if (lines.Count == 0)
                    throw Failure("A comment starts with #, ## or ### and a space.", position);
                position = lineStart;
                break;
            }

            SkipLineEnd();
        }

        return new FluentComment(level, string.Join("\n", lines), new FluentSpan(start, position));
    }

    private FluentMessage ParseMessage(int start, string? comment)
    {
        var idStart = position;
        var id = ReadIdentifier();
        var idSpan = new FluentSpan(idStart, position);
        SkipBlankInline();
        if (Current != '=')
            throw Failure($"Expected = after the message id {id}.", position);

        position++;
        var value = ParsePattern();
        SkipBlankBlock();
        var attributes = ParseAttributes();
        if (value is null && attributes.Count == 0)
            throw new ParseFailure($"The message {id} has no value: write its text after =, or give it an attribute such as .title = ...", idSpan);

        return new FluentMessage(id, idSpan, value, attributes, comment, new FluentSpan(start, position));
    }

    private FluentTerm ParseTerm(int start, string? comment)
    {
        position++;
        if (!IsLetter(Current))
            throw Failure("A term's id starts with - and a letter, such as -brand-name.", position);

        var idStart = position;
        var id = ReadIdentifier();
        var idSpan = new FluentSpan(idStart - 1, position);
        SkipBlankInline();
        if (Current != '=')
            throw Failure($"Expected = after the term id -{id}.", position);

        position++;
        var value = ParsePattern() ?? throw new ParseFailure($"The term -{id} has no value; a term needs one.", idSpan);
        SkipBlankBlock();
        var attributes = ParseAttributes();
        return new FluentTerm(id, idSpan, value, attributes, comment, new FluentSpan(start, position));
    }

    private List<FluentAttribute> ParseAttributes()
    {
        var attributes = new List<FluentAttribute>();
        while (true)
        {
            var lineStart = position;
            SkipBlankInline();
            if (Current != '.')
            {
                position = lineStart;
                break;
            }

            position++;
            if (!IsLetter(Current))
            {
                position = lineStart;
                break;
            }

            var idStart = position;
            var id = ReadIdentifier();
            var idSpan = new FluentSpan(idStart, position);
            SkipBlankInline();
            if (Current != '=')
            {
                position = lineStart;
                break;
            }

            position++;
            if (ParsePattern() is not { } value)
            {
                position = lineStart;
                break;
            }

            attributes.Add(new FluentAttribute(id, idSpan, value));
        }

        return attributes;
    }

    private FluentPattern? ParsePattern()
    {
        var pieces = new List<Piece>();
        int? lastNonBlank = null;
        int? commonIndent = null;
        SkipBlankInline();
        Role role;
        if (SkipLineEnd())
        {
            SkipBlankBlock();
            role = Role.LineStart;
        }
        else
        {
            role = Role.InitialLineStart;
        }

        while (!IsEof)
        {
            if (Current == '{')
            {
                if (role == Role.LineStart)
                    commonIndent = 0;

                lastNonBlank = pieces.Count;
                pieces.Add(new Piece(ParsePlaceable(position)));
                role = Role.Continuation;
                continue;
            }

            var sliceStart = position;
            var indent = 0;
            if (role == Role.LineStart)
            {
                indent = SkipBlankInline();
                if (IsEof)
                    break;

                var first = Current;
                if (indent == 0)
                {
                    if (first is not ('\r' or '\n'))
                        break;
                }
                else if (first is '.' or '}' or '[' or '*')
                {
                    position = sliceStart;
                    break;
                }
            }

            var (start, end, nonBlank, ending) = ReadTextSlice();
            if (start != end)
            {
                if (role == Role.LineStart && nonBlank)
                    commonIndent = commonIndent is { } common ? Math.Min(common, indent) : indent;

                if (role != Role.LineStart || nonBlank || ending == Ending.LineFeed)
                {
                    if (nonBlank)
                        lastNonBlank = pieces.Count;
                    pieces.Add(new Piece(role == Role.LineStart ? sliceStart : start, end, indent, role, ending == Ending.CarriageReturnLineFeed));
                }
            }
            else if (ending == Ending.CarriageReturnLineFeed)
            {
                pieces.Add(new Piece(0, 0, indent, role, true));
            }

            role = ending switch
            {
                Ending.LineFeed or Ending.CarriageReturnLineFeed => Role.LineStart,
                Ending.PlaceableStart or Ending.EndOfFile => Role.Continuation,
                _ => role,
            };
        }

        if (lastNonBlank is not { } last)
            return null;

        var elements = new List<FluentPatternElement>();
        var pending = new StringBuilder();
        for (var i = 0; i <= last; i++)
        {
            var piece = pieces[i];
            if (piece.Placeable is { } placeable)
            {
                Flush(pending, elements);
                elements.Add(new FluentPlaceable(placeable));
                continue;
            }

            var start = piece.Start;
            if (piece.Role == Role.LineStart && piece.End > piece.Start)
                start += commonIndent is { } common ? Math.Min(piece.Indent, common) : piece.Indent;
            var value = piece.MissingLineFeed && piece.Start == piece.End
                ? "\n"
                : text.Substring(start, Math.Max(0, piece.End - start)) + (piece.MissingLineFeed ? "\n" : "");
            if (i == last)
                value = value.TrimEnd();
            pending.Append(value);
        }

        Flush(pending, elements);
        return new FluentPattern(elements);

        static void Flush(StringBuilder pending, List<FluentPatternElement> elements)
        {
            if (pending.Length == 0)
                return;
            elements.Add(new FluentText(pending.ToString()));
            pending.Clear();
        }
    }

    private (int Start, int End, bool NonBlank, Ending Ending) ReadTextSlice()
    {
        var start = position;
        var nonBlank = false;
        while (position < text.Length)
        {
            var character = text[position];
            switch (character)
            {
                case ' ':
                    position++;
                    break;
                case '\n':
                    position++;
                    return (start, position, nonBlank, Ending.LineFeed);
                case '\r' when Peek(1) == '\n':
                    var end = position;
                    position += 2;
                    return (start, end, nonBlank, Ending.CarriageReturnLineFeed);
                case '{':
                    return (start, position, nonBlank, Ending.PlaceableStart);
                case '}':
                    throw Failure("A } closes nothing here; to show a brace, write {\"}\"}.", position);
                default:
                    nonBlank = true;
                    position++;
                    break;
            }
        }

        return (start, position, nonBlank, Ending.EndOfFile);
    }

    // The position is the opening brace's, and the parse goes on after it.
    private FluentExpression ParsePlaceable(int open)
    {
        position = open + 1;
        SkipBlank();
        if (IsEof)
            throw Failure("The placeable is not closed; end it with }.", open);

        var expression = ParseExpression();
        SkipBlankInline();
        if (IsEof)
            throw Failure("The placeable is not closed; end it with }.", open);
        if (Current != '}')
            throw Failure("Expected } to close the placeable.", position);

        position++;
        return expression;
    }

    private FluentExpression ParseExpression()
    {
        var start = position;
        var selector = ParseInlineExpression();
        SkipBlank();
        if (Current != '-' || Peek(1) != '>')
        {
            if (selector is FluentTermReference { Attribute: not null } term)
                throw new ParseFailure($"The attribute -{term.Id}.{term.Attribute} can only choose a variant, as in {{ -{term.Id}.{term.Attribute} -> ... }}; a term's attributes are not shown.", term.Span);
            return selector;
        }

        switch (selector)
        {
            case FluentMessageReference { Attribute: null } message:
                throw new ParseFailure($"The message {message.Id} can't choose a variant; use a variable, such as {{ $count -> ... }}.", message.Span);
            case FluentMessageReference message:
                throw new ParseFailure($"The attribute {message.Id}.{message.Attribute} can't choose a variant; use a variable or a term's attribute.", message.Span);
            case FluentTermReference { Attribute: null } term:
                throw new ParseFailure($"The term -{term.Id} can't choose a variant; use one of its attributes, such as {{ -{term.Id}.gender -> ... }}.", term.Span);
            case FluentNestedPlaceable nested:
                throw new ParseFailure("A placeable can't choose a variant; use a variable, a function or a term's attribute.", nested.Span);
        }

        position += 2;
        SkipBlankInline();
        if (!SkipLineEnd())
            throw Failure("Start each variant on a line of its own, after ->.", position);

        SkipBlank();
        var variants = ParseVariants();
        return new FluentSelectExpression(selector, variants, new FluentSpan(start, position));
    }

    private List<FluentVariant> ParseVariants()
    {
        var variants = new List<FluentVariant>();
        var hasDefault = false;
        while (true)
        {
            var variantStart = position;
            var isDefault = Current == '*';
            if (isDefault)
            {
                if (hasDefault)
                    throw Failure("A select expression has one default variant; mark only one with *.", position);
                position++;
            }

            if (Current != '[')
            {
                if (isDefault)
                    throw Failure("Expected [ after *, as in *[other].", position);
                break;
            }

            position++;
            SkipBlank();
            string key;
            bool isNumber;
            if (IsDigit(Current) || Current == '-')
            {
                key = ReadNumber();
                isNumber = true;
            }
            else if (IsLetter(Current))
            {
                key = ReadIdentifier();
                isNumber = false;
            }
            else
            {
                throw Failure("Expected a variant key, such as [one], [other] or [0].", position);
            }

            SkipBlank();
            if (Current != ']')
                throw Failure($"Expected ] to close the variant key [{key}.", position);

            position++;
            var value = ParsePattern() ?? throw new ParseFailure($"The variant [{key}] has no value.", new FluentSpan(variantStart, position));
            variants.Add(new FluentVariant(key, isNumber, isDefault, value));
            hasDefault |= isDefault;
            SkipBlank();
        }

        if (!hasDefault)
            throw Failure("A select expression needs a default variant, marked with *, such as *[other].", position);
        return variants;
    }

    private FluentExpression ParseInlineExpression()
    {
        var start = position;
        var character = Current;
        if (character == '"')
            return ParseString();
        if (IsDigit(character))
            return new FluentNumberLiteral(ReadNumber(), new FluentSpan(start, position));

        if (character == '-')
        {
            if (!IsLetter(Peek(1)))
                return new FluentNumberLiteral(ReadNumber(), new FluentSpan(start, position));

            position++;
            var id = ReadIdentifier();
            var attribute = ReadAttributeAccessor();
            var arguments = ParseArguments();
            return new FluentTermReference(id, attribute, arguments, new FluentSpan(start, position));
        }

        if (character == '$')
        {
            position++;
            if (!IsLetter(Current))
                throw Failure("Expected a variable name after $, such as $count.", position);
            var name = ReadIdentifier();
            return new FluentVariableReference(name, new FluentSpan(start, position));
        }

        if (IsLetter(character))
        {
            var id = ReadIdentifier();
            var idEnd = position;
            if (ParseArguments() is { } arguments)
            {
                if (!IsCallee(id))
                    throw new ParseFailure($"{id} is not a function; function names are upper case, such as NUMBER or DATETIME.", new FluentSpan(start, idEnd));
                return new FluentFunctionReference(id, arguments, new FluentSpan(start, position));
            }

            var attribute = ReadAttributeAccessor();
            return new FluentMessageReference(id, attribute, new FluentSpan(start, position));
        }

        if (character == '{')
        {
            var inner = ParsePlaceable(position);
            return new FluentNestedPlaceable(inner, new FluentSpan(start, position));
        }

        throw Failure(IsEof
            ? "The placeable is not closed; end it with }."
            : "Expected a variable, a message, a term, a function, a number or quoted text in the placeable.", position);
    }

    private FluentStringLiteral ParseString()
    {
        var start = position;
        position++;
        var value = new StringBuilder();
        while (true)
        {
            if (IsLineEndOrEof())
                throw Failure("Close the quoted text with \" on the same line.", position);

            var character = Current;
            if (character == '"')
            {
                position++;
                return new FluentStringLiteral(value.ToString(), new FluentSpan(start, position));
            }

            if (character != '\\')
            {
                value.Append(character);
                position++;
                continue;
            }

            var escaped = Peek(1);
            if (escaped is '\\' or '"' or '{')
            {
                value.Append(escaped);
                position += 2;
            }
            else if (escaped is 'u' or 'U')
            {
                var length = escaped == 'u' ? 4 : 6;
                var digits = position + 2 + length <= text.Length ? text.Substring(position + 2, length) : "";
                if (digits.Length != length || !digits.All(IsHexDigit))
                    throw Failure($"Expected {length} hexadecimal digits after \\{escaped}.", position);

                var code = int.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                value.Append(code is >= 0xD800 and <= 0xDFFF or > 0x10FFFF ? "�" : char.ConvertFromUtf32(code));
                position += 2 + length;
            }
            else
            {
                throw Failure($"Unknown escape \\{escaped}; quoted text knows \\\\, \\\", \\u0000 and \\U000000.", position);
            }
        }
    }

    private FluentArguments? ParseArguments()
    {
        var before = position;
        SkipBlank();
        if (Current != '(')
        {
            position = before;
            return null;
        }

        position++;
        SkipBlank();
        var positional = new List<FluentExpression>();
        var named = new List<FluentNamedArgument>();
        while (!IsEof && Current != ')')
        {
            var argumentStart = position;
            var expression = ParseInlineExpression();
            SkipBlank();
            if (expression is FluentMessageReference { Attribute: null } name && Current == ':')
            {
                if (named.Any(n => n.Name == name.Id))
                    throw new ParseFailure($"The argument {name.Id} is given twice.", name.Span);

                position++;
                SkipBlank();
                var value = ParseInlineExpression();
                if (value is not (FluentStringLiteral or FluentNumberLiteral))
                    throw new ParseFailure($"The value of the argument {name.Id} is a number or quoted text.", value.Span);
                named.Add(new FluentNamedArgument(name.Id, value));
                SkipBlank();
            }
            else
            {
                if (named.Count > 0)
                    throw new ParseFailure("Positional arguments come before named ones.", new FluentSpan(argumentStart, position));
                positional.Add(expression);
            }

            if (Current == ',')
            {
                position++;
                SkipBlank();
            }
            else if (Current != ')')
            {
                throw Failure("Expected , or ) after the argument.", position);
            }
        }

        if (Current != ')')
            throw Failure("The arguments are not closed; end them with ).", position);

        position++;
        return new FluentArguments(positional, named);
    }

    private string? ReadAttributeAccessor()
    {
        if (Current != '.')
            return null;

        position++;
        if (!IsLetter(Current))
            throw Failure("Expected an attribute name after the dot, such as .title.", position);
        return ReadIdentifier();
    }

    private string ReadIdentifier()
    {
        var start = position;
        position++;
        while (position < text.Length && IsIdentifierPart(text[position]))
            position++;
        return text.Substring(start, position - start);
    }

    private string ReadNumber()
    {
        var start = position;
        if (Current == '-')
            position++;
        SkipDigits();
        if (Current == '.')
        {
            position++;
            SkipDigits();
        }

        return text.Substring(start, position - start);
    }

    private void SkipDigits()
    {
        var start = position;
        while (IsDigit(Current))
            position++;
        if (position == start)
            throw Failure("Expected a digit.", position);
    }

    private bool IsLineEndOrEof() => IsEof || Current == '\n' || (Current == '\r' && Peek(1) == '\n');

    private bool SkipLineEnd()
    {
        if (Current == '\n')
        {
            position++;
            return true;
        }

        if (Current == '\r' && Peek(1) == '\n')
        {
            position += 2;
            return true;
        }

        return false;
    }

    private int SkipBlankInline()
    {
        var start = position;
        while (Current == ' ')
            position++;
        return position - start;
    }

    private int SkipBlankBlock()
    {
        var count = 0;
        while (true)
        {
            var start = position;
            SkipBlankInline();
            if (!SkipLineEnd())
            {
                position = start;
                return count;
            }

            count++;
        }
    }

    private void SkipBlank()
    {
        while (Current == ' ' || SkipLineEnd())
        {
            if (Current == ' ')
                position++;
        }
    }

    private sealed class Piece
    {
        public Piece(FluentExpression placeable) => Placeable = placeable;

        public Piece(int start, int end, int indent, Role role, bool missingLineFeed)
        {
            Start = start;
            End = end;
            Indent = indent;
            Role = role;
            MissingLineFeed = missingLineFeed;
        }

        public FluentExpression? Placeable { get; }

        public int Start { get; }

        public int End { get; }

        public int Indent { get; }

        public Role Role { get; }

        public bool MissingLineFeed { get; }
    }

    private sealed class ParseFailure(string message, FluentSpan span) : Exception(message)
    {
        public FluentSpan Span { get; } = span;
    }
}
