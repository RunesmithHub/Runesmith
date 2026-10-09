using System.Collections.Frozen;

namespace Runesmith.Languages.Java.Syntax;

/// <summary>The reserved keywords of Java, and the contextual keywords that are identifiers except in certain places.</summary>
public static class Keywords
{
    private static readonly FrozenDictionary<string, TokenKind> Reserved = new Dictionary<string, TokenKind>(StringComparer.Ordinal)
    {
        ["abstract"] = TokenKind.AbstractKeyword,
        ["assert"] = TokenKind.AssertKeyword,
        ["boolean"] = TokenKind.BooleanKeyword,
        ["break"] = TokenKind.BreakKeyword,
        ["byte"] = TokenKind.ByteKeyword,
        ["case"] = TokenKind.CaseKeyword,
        ["catch"] = TokenKind.CatchKeyword,
        ["char"] = TokenKind.CharKeyword,
        ["class"] = TokenKind.ClassKeyword,
        ["const"] = TokenKind.ConstKeyword,
        ["continue"] = TokenKind.ContinueKeyword,
        ["default"] = TokenKind.DefaultKeyword,
        ["do"] = TokenKind.DoKeyword,
        ["double"] = TokenKind.DoubleKeyword,
        ["else"] = TokenKind.ElseKeyword,
        ["enum"] = TokenKind.EnumKeyword,
        ["extends"] = TokenKind.ExtendsKeyword,
        ["final"] = TokenKind.FinalKeyword,
        ["finally"] = TokenKind.FinallyKeyword,
        ["float"] = TokenKind.FloatKeyword,
        ["for"] = TokenKind.ForKeyword,
        ["goto"] = TokenKind.GotoKeyword,
        ["if"] = TokenKind.IfKeyword,
        ["implements"] = TokenKind.ImplementsKeyword,
        ["import"] = TokenKind.ImportKeyword,
        ["instanceof"] = TokenKind.InstanceofKeyword,
        ["int"] = TokenKind.IntKeyword,
        ["interface"] = TokenKind.InterfaceKeyword,
        ["long"] = TokenKind.LongKeyword,
        ["native"] = TokenKind.NativeKeyword,
        ["new"] = TokenKind.NewKeyword,
        ["package"] = TokenKind.PackageKeyword,
        ["private"] = TokenKind.PrivateKeyword,
        ["protected"] = TokenKind.ProtectedKeyword,
        ["public"] = TokenKind.PublicKeyword,
        ["return"] = TokenKind.ReturnKeyword,
        ["short"] = TokenKind.ShortKeyword,
        ["static"] = TokenKind.StaticKeyword,
        ["strictfp"] = TokenKind.StrictfpKeyword,
        ["super"] = TokenKind.SuperKeyword,
        ["switch"] = TokenKind.SwitchKeyword,
        ["synchronized"] = TokenKind.SynchronizedKeyword,
        ["this"] = TokenKind.ThisKeyword,
        ["throw"] = TokenKind.ThrowKeyword,
        ["throws"] = TokenKind.ThrowsKeyword,
        ["transient"] = TokenKind.TransientKeyword,
        ["try"] = TokenKind.TryKeyword,
        ["void"] = TokenKind.VoidKeyword,
        ["volatile"] = TokenKind.VolatileKeyword,
        ["while"] = TokenKind.WhileKeyword,
        ["true"] = TokenKind.TrueKeyword,
        ["false"] = TokenKind.FalseKeyword,
        ["null"] = TokenKind.NullKeyword,
        ["_"] = TokenKind.Underscore,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static readonly FrozenDictionary<string, TokenKind>.AlternateLookup<ReadOnlySpan<char>> ReservedBySpan =
        Reserved.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>Gets the contextual keywords: identifiers with a meaning in certain places, such as <c>var</c>, <c>record</c> and
    /// <c>yield</c>. <c>non-sealed</c> is three tokens.</summary>
    public static IReadOnlySet<string> Contextual { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "exports", "module", "non-sealed", "open", "opens", "permits", "provides", "record", "requires", "sealed", "to", "transitive", "uses",
        "var", "when", "with", "yield",
    };

    /// <summary>Gets the reserved keywords, including <c>true</c>, <c>false</c>, <c>null</c> and <c>_</c>.</summary>
    public static IEnumerable<string> All => Reserved.Keys;

    /// <summary>Gets the kind of a reserved keyword, or <see cref="TokenKind.Identifier"/> for anything else.</summary>
    public static TokenKind KindOf(ReadOnlySpan<char> word) => ReservedBySpan.TryGetValue(word, out var kind) ? kind : TokenKind.Identifier;

    /// <summary>Gets the text of a keyword or operator kind, such as <c>class</c> or <c>-&gt;</c>.</summary>
    public static string TextOf(TokenKind kind)
    {
        foreach (var (text, keyword) in Reserved)
        {
            if (keyword == kind)
                return text;
        }

        return kind switch
        {
            TokenKind.OpenParen => "(",
            TokenKind.CloseParen => ")",
            TokenKind.OpenBrace => "{",
            TokenKind.CloseBrace => "}",
            TokenKind.OpenBracket => "[",
            TokenKind.CloseBracket => "]",
            TokenKind.Semicolon => ";",
            TokenKind.Comma => ",",
            TokenKind.Dot => ".",
            TokenKind.Ellipsis => "...",
            TokenKind.At => "@",
            TokenKind.ColonColon => "::",
            TokenKind.Equals => "=",
            TokenKind.GreaterThan => ">",
            TokenKind.LessThan => "<",
            TokenKind.Exclamation => "!",
            TokenKind.Tilde => "~",
            TokenKind.Question => "?",
            TokenKind.Colon => ":",
            TokenKind.Arrow => "->",
            TokenKind.EqualsEquals => "==",
            TokenKind.GreaterThanEquals => ">=",
            TokenKind.LessThanEquals => "<=",
            TokenKind.ExclamationEquals => "!=",
            TokenKind.AmpersandAmpersand => "&&",
            TokenKind.BarBar => "||",
            TokenKind.PlusPlus => "++",
            TokenKind.MinusMinus => "--",
            TokenKind.Plus => "+",
            TokenKind.Minus => "-",
            TokenKind.Asterisk => "*",
            TokenKind.Slash => "/",
            TokenKind.Ampersand => "&",
            TokenKind.Bar => "|",
            TokenKind.Caret => "^",
            TokenKind.Percent => "%",
            TokenKind.LessThanLessThan => "<<",
            TokenKind.PlusEquals => "+=",
            TokenKind.MinusEquals => "-=",
            TokenKind.AsteriskEquals => "*=",
            TokenKind.SlashEquals => "/=",
            TokenKind.AmpersandEquals => "&=",
            TokenKind.BarEquals => "|=",
            TokenKind.CaretEquals => "^=",
            TokenKind.PercentEquals => "%=",
            TokenKind.LessThanLessThanEquals => "<<=",
            TokenKind.GreaterThanGreaterThanEquals => ">>=",
            TokenKind.GreaterThanGreaterThanGreaterThanEquals => ">>>=",
            TokenKind.Identifier => "identifier",
            TokenKind.EndOfFile => "end of file",
            _ => kind.ToString(),
        };
    }
}
