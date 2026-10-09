using System.Text;

namespace Runesmith.Languages.Java.Syntax;

/// <summary>Splits decoded Java text into tokens and comments, reporting malformed literals and comments.</summary>
/// <remarks>Positions are offsets in the text it was given; whitespace is skipped and comments are collected on the side.</remarks>
internal sealed class Lexer
{
    private readonly string text;
    private readonly int end;
    private readonly List<JavaComment> comments;
    private readonly List<SyntaxDiagnostic> diagnostics;
    private int position;

    public Lexer(string text, int start, int end, List<JavaComment> comments, List<SyntaxDiagnostic> diagnostics)
    {
        this.text = text;
        this.end = end;
        this.comments = comments;
        this.diagnostics = diagnostics;
        position = start;
    }

    /// <summary>Lexes a whole text into tokens, ending with an end-of-file token.</summary>
    public static List<JavaToken> LexAll(string text, List<JavaComment> comments, List<SyntaxDiagnostic> diagnostics)
    {
        var end = text.Length;

        // A control-Z at the very end is allowed and ignored.
        if (end > 0 && text[end - 1] == '\u001a')
            end--;

        var lexer = new Lexer(text, 0, end, comments, diagnostics);
        var tokens = new List<JavaToken>(Math.Max(16, text.Length / 5));
        while (true)
        {
            var token = lexer.Next();
            tokens.Add(token);
            if (token.Kind == TokenKind.EndOfFile)
                return tokens;
        }
    }

    public int Position => position;

    public JavaToken Next()
    {
        SkipWhitespaceAndComments();
        if (position >= end)
            return new JavaToken(TokenKind.EndOfFile, new SourceSpan(end, 0));

        var start = position;
        var c = text[position];
        if (CharacterClasses.IsAsciiIdentifierStart(c))
            return Identifier(start);

        if (char.IsAsciiDigit(c))
            return Number(start);

        switch (c)
        {
            case '"':
                return position + 2 < end && text[position + 1] == '"' && text[position + 2] == '"' ? TextBlock(start) : String(start);
            case '\'':
                return Character(start);
            case '.':
                if (position + 1 < end && char.IsAsciiDigit(text[position + 1]))
                    return Number(start);
                if (Peek(1) == '.' && Peek(2) == '.')
                    return Token(TokenKind.Ellipsis, 3);
                return Token(TokenKind.Dot, 1);
            case '(': return Token(TokenKind.OpenParen, 1);
            case ')': return Token(TokenKind.CloseParen, 1);
            case '{': return Token(TokenKind.OpenBrace, 1);
            case '}': return Token(TokenKind.CloseBrace, 1);
            case '[': return Token(TokenKind.OpenBracket, 1);
            case ']': return Token(TokenKind.CloseBracket, 1);
            case ';': return Token(TokenKind.Semicolon, 1);
            case ',': return Token(TokenKind.Comma, 1);
            case '@': return Token(TokenKind.At, 1);
            case '?': return Token(TokenKind.Question, 1);
            case '~': return Token(TokenKind.Tilde, 1);
            case ':': return Peek(1) == ':' ? Token(TokenKind.ColonColon, 2) : Token(TokenKind.Colon, 1);
            case '=': return Peek(1) == '=' ? Token(TokenKind.EqualsEquals, 2) : Token(TokenKind.Equals, 1);
            case '!': return Peek(1) == '=' ? Token(TokenKind.ExclamationEquals, 2) : Token(TokenKind.Exclamation, 1);
            case '*': return Peek(1) == '=' ? Token(TokenKind.AsteriskEquals, 2) : Token(TokenKind.Asterisk, 1);
            case '/': return Peek(1) == '=' ? Token(TokenKind.SlashEquals, 2) : Token(TokenKind.Slash, 1);
            case '%': return Peek(1) == '=' ? Token(TokenKind.PercentEquals, 2) : Token(TokenKind.Percent, 1);
            case '^': return Peek(1) == '=' ? Token(TokenKind.CaretEquals, 2) : Token(TokenKind.Caret, 1);
            case '+':
                return Peek(1) switch { '+' => Token(TokenKind.PlusPlus, 2), '=' => Token(TokenKind.PlusEquals, 2), _ => Token(TokenKind.Plus, 1) };
            case '-':
                return Peek(1) switch
                {
                    '-' => Token(TokenKind.MinusMinus, 2),
                    '=' => Token(TokenKind.MinusEquals, 2),
                    '>' => Token(TokenKind.Arrow, 2),
                    _ => Token(TokenKind.Minus, 1),
                };
            case '&':
                return Peek(1) switch
                {
                    '&' => Token(TokenKind.AmpersandAmpersand, 2),
                    '=' => Token(TokenKind.AmpersandEquals, 2),
                    _ => Token(TokenKind.Ampersand, 1),
                };
            case '|':
                return Peek(1) switch { '|' => Token(TokenKind.BarBar, 2), '=' => Token(TokenKind.BarEquals, 2), _ => Token(TokenKind.Bar, 1) };
            case '<':
                if (Peek(1) == '<')
                    return Peek(2) == '=' ? Token(TokenKind.LessThanLessThanEquals, 3) : Token(TokenKind.LessThanLessThan, 2);
                return Peek(1) == '=' ? Token(TokenKind.LessThanEquals, 2) : Token(TokenKind.LessThan, 1);
            case '>':
                if (Peek(1) == '=')
                    return Token(TokenKind.GreaterThanEquals, 2);
                if (Peek(1) == '>' && Peek(2) == '=')
                    return Token(TokenKind.GreaterThanGreaterThanEquals, 3);
                if (Peek(1) == '>' && Peek(2) == '>' && Peek(3) == '=')
                    return Token(TokenKind.GreaterThanGreaterThanGreaterThanEquals, 4);
                return Token(TokenKind.GreaterThan, 1);
        }

        if (Rune.TryGetRuneAt(text, position, out var rune) && CharacterClasses.IsIdentifierStart(rune))
            return Identifier(start);

        position += char.IsSurrogatePair(text, position) ? 2 : 1;
        Report(start, position, $"The character '{text[start..position]}' cannot appear here");
        return new JavaToken(TokenKind.BadToken, SourceSpan.FromBounds(start, position));
    }

    private char Peek(int ahead) => position + ahead < end ? text[position + ahead] : '\0';

    private JavaToken Token(TokenKind kind, int length)
    {
        var start = position;
        position += length;
        return new JavaToken(kind, new SourceSpan(start, length));
    }

    private void SkipWhitespaceAndComments()
    {
        while (position < end)
        {
            var c = text[position];
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                position++;
                continue;
            }

            if (c != '/' || position + 1 >= end)
                return;

            var next = text[position + 1];
            if (next == '/')
            {
                var start = position;
                var kind = position + 2 < end && text[position + 2] == '/' ? CommentKind.MarkdownLine : CommentKind.Line;
                position += 2;
                while (position < end && text[position] is not ('\n' or '\r'))
                    position++;
                comments.Add(new JavaComment(kind, SourceSpan.FromBounds(start, position)));
            }
            else if (next == '*')
            {
                var start = position;
                var kind = position + 2 < end && text[position + 2] == '*' && !(position + 3 < end && text[position + 3] == '/')
                    ? CommentKind.Documentation
                    : CommentKind.Block;
                var close = text.IndexOf("*/", position + 2, end - position - 2, StringComparison.Ordinal);
                position = close < 0 ? end : close + 2;
                if (close < 0)
                    Report(start, end, "The comment is not closed with */");
                comments.Add(new JavaComment(kind, SourceSpan.FromBounds(start, position)));
            }
            else
            {
                return;
            }
        }
    }

    private JavaToken Identifier(int start)
    {
        var ascii = true;
        while (position < end)
        {
            var c = text[position];
            if (CharacterClasses.IsAsciiIdentifierPart(c))
            {
                position++;
                continue;
            }

            if (c < 0x80 && c is not (<= '\u0008' or (>= '\u000e' and <= '\u001b') or '\u007f'))
                break;

            if (!Rune.TryGetRuneAt(text, position, out var rune) || !CharacterClasses.IsIdentifierPart(rune))
                break;

            ascii = false;
            position += rune.Utf16SequenceLength;
        }

        var kind = ascii ? Keywords.KindOf(text.AsSpan(start, position - start)) : TokenKind.Identifier;
        return new JavaToken(kind, SourceSpan.FromBounds(start, position));
    }

    private JavaToken Number(int start)
    {
        var c = text[position];
        if (c == '0' && Peek(1) is 'x' or 'X')
            return HexNumber(start);

        if (c == '0' && Peek(1) is 'b' or 'B')
        {
            position += 2;
            var digits = Digits(static d => d is '0' or '1', start);
            if (digits == 0)
                Report(start, position, "A binary literal needs at least one digit after 0b");
            if (Peek(0) is 'l' or 'L')
            {
                position++;
                return new JavaToken(TokenKind.LongLiteral, SourceSpan.FromBounds(start, position));
            }

            return new JavaToken(TokenKind.IntegerLiteral, SourceSpan.FromBounds(start, position));
        }

        var isFloat = false;
        if (c != '.')
            Digits(char.IsAsciiDigit, start);

        if (Peek(0) == '.')
        {
            isFloat = true;
            position++;
            if (char.IsAsciiDigit(Peek(0)))
                Digits(char.IsAsciiDigit, start);
        }

        if (Peek(0) is 'e' or 'E')
        {
            isFloat = true;
            Exponent(start);
        }

        switch (Peek(0))
        {
            case 'f' or 'F':
                position++;
                return new JavaToken(TokenKind.FloatLiteral, SourceSpan.FromBounds(start, position));
            case 'd' or 'D':
                position++;
                return new JavaToken(TokenKind.DoubleLiteral, SourceSpan.FromBounds(start, position));
            case 'l' or 'L' when !isFloat:
                position++;
                CheckOctal(start, position - 1);
                return new JavaToken(TokenKind.LongLiteral, SourceSpan.FromBounds(start, position));
        }

        if (isFloat)
            return new JavaToken(TokenKind.DoubleLiteral, SourceSpan.FromBounds(start, position));

        CheckOctal(start, position);
        return new JavaToken(TokenKind.IntegerLiteral, SourceSpan.FromBounds(start, position));
    }

    private JavaToken HexNumber(int start)
    {
        position += 2;
        var digits = Digits(char.IsAsciiHexDigit, start);
        var isFloat = false;
        if (Peek(0) == '.')
        {
            isFloat = true;
            position++;
            digits += Digits(char.IsAsciiHexDigit, start);
        }

        if (digits == 0)
            Report(start, position, "A hexadecimal literal needs at least one digit after 0x");

        if (Peek(0) is 'p' or 'P')
        {
            isFloat = true;
            Exponent(start);
        }
        else if (isFloat)
        {
            Report(start, position, "A hexadecimal floating-point literal needs an exponent, such as p0");
        }

        switch (Peek(0))
        {
            case 'f' or 'F' when isFloat:
                position++;
                return new JavaToken(TokenKind.FloatLiteral, SourceSpan.FromBounds(start, position));
            case 'd' or 'D' when isFloat:
                position++;
                return new JavaToken(TokenKind.DoubleLiteral, SourceSpan.FromBounds(start, position));
            case 'l' or 'L' when !isFloat:
                position++;
                return new JavaToken(TokenKind.LongLiteral, SourceSpan.FromBounds(start, position));
        }

        return new JavaToken(isFloat ? TokenKind.DoubleLiteral : TokenKind.IntegerLiteral, SourceSpan.FromBounds(start, position));
    }

    private void Exponent(int start)
    {
        position++;
        if (Peek(0) is '+' or '-')
            position++;
        if (Digits(char.IsAsciiDigit, start) == 0)
            Report(start, position, "The exponent needs at least one digit");
    }

    // Reads digits with underscores between them, and reports underscores at either end.
    private int Digits(Func<char, bool> isDigit, int literalStart)
    {
        var count = 0;
        var underscoreAt = -1;
        var first = position;
        while (position < end)
        {
            var c = text[position];
            if (isDigit(c))
            {
                count++;
                position++;
            }
            else if (c == '_')
            {
                if (position == first)
                    Report(literalStart, position + 1, "An underscore can only be between digits");
                underscoreAt = position;
                position++;
            }
            else
            {
                break;
            }
        }

        if (underscoreAt == position - 1 && underscoreAt >= 0)
            Report(literalStart, position, "An underscore can only be between digits");
        return count;
    }

    private void CheckOctal(int start, int digitsEnd)
    {
        if (text[start] != '0' || digitsEnd - start < 2)
            return;

        for (var i = start + 1; i < digitsEnd; i++)
        {
            if (text[i] is '8' or '9')
            {
                Report(start, digitsEnd, "An octal literal can only have the digits 0 to 7");
                return;
            }
        }
    }

    private JavaToken Character(int start)
    {
        position++;
        var count = 0;
        while (position < end && text[position] is not ('\'' or '\n' or '\r'))
        {
            if (text[position] == '\\')
                Escape(allowLineTerminator: false);
            else
                position += char.IsSurrogatePair(text, position) ? 2 : 1;
            count++;
        }

        if (position < end && text[position] == '\'')
        {
            position++;
            if (count != 1)
                Report(start, position, count == 0 ? "A character literal needs a character" : "A character literal can hold only one character");
        }
        else
        {
            Report(start, position, "The character literal is not closed with '");
        }

        return new JavaToken(TokenKind.CharacterLiteral, SourceSpan.FromBounds(start, position));
    }

    private JavaToken String(int start)
    {
        position++;
        while (position < end && text[position] is not ('"' or '\n' or '\r'))
        {
            if (text[position] == '\\')
                Escape(allowLineTerminator: false);
            else
                position++;
        }

        if (position < end && text[position] == '"')
            position++;
        else
            Report(start, position, "The string is not closed with \"");
        return new JavaToken(TokenKind.StringLiteral, SourceSpan.FromBounds(start, position));
    }

    private JavaToken TextBlock(int start)
    {
        position += 3;
        while (position < end && text[position] is ' ' or '\t' or '\f')
            position++;

        if (position < end && text[position] is '\n' or '\r')
        {
            position += text[position] == '\r' && Peek(1) == '\n' ? 2 : 1;
        }
        else
        {
            Report(start, position, "A text block's opening \"\"\" must be followed by the end of the line");
        }

        while (position < end)
        {
            var c = text[position];
            if (c == '"' && Peek(1) == '"' && Peek(2) == '"')
            {
                position += 3;
                return new JavaToken(TokenKind.TextBlock, SourceSpan.FromBounds(start, position));
            }

            if (c == '\\')
                Escape(allowLineTerminator: true);
            else
                position++;
        }

        Report(start, end, "The text block is not closed with \"\"\"");
        return new JavaToken(TokenKind.TextBlock, SourceSpan.FromBounds(start, end));
    }

    private void Escape(bool allowLineTerminator)
    {
        var start = position;
        position++;
        if (position >= end)
            return;

        var c = text[position];
        switch (c)
        {
            case 'b' or 't' or 'n' or 'f' or 'r' or 's' or '"' or '\'' or '\\':
                position++;
                return;
            case >= '0' and <= '7':
                var max = c <= '3' ? 3 : 2;
                var count = 0;
                while (count < max && position < end && text[position] is >= '0' and <= '7')
                {
                    position++;
                    count++;
                }

                return;
            case '\n' or '\r' when allowLineTerminator:
                position += c == '\r' && Peek(1) == '\n' ? 2 : 1;
                return;
            default:
                position++;
                Report(start, position, $"'\\{c}' is not a valid escape sequence");
                return;
        }
    }

    private void Report(int start, int stop, string message) =>
        diagnostics.Add(new SyntaxDiagnostic(SourceSpan.FromBounds(start, Math.Max(start, stop)), SyntaxDiagnostic.SyntaxError, message));
}
