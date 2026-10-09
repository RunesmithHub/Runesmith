namespace Runesmith.Languages.Java.Syntax;

/// <summary>The kinds of Java tokens. Contextual keywords such as <c>var</c> and <c>record</c> are identifiers; the parser reads them by
/// position. A <c>&gt;&gt;</c> or <c>&gt;&gt;&gt;</c> shift is two or three adjacent <see cref="GreaterThan"/> tokens, so type arguments can
/// close one at a time.</summary>
public enum TokenKind : byte
{
    EndOfFile,
    BadToken,
    Identifier,

    /// <summary>The underscore, a keyword since Java 9 that names unnamed variables since Java 22.</summary>
    Underscore,

    IntegerLiteral,
    LongLiteral,
    FloatLiteral,
    DoubleLiteral,
    CharacterLiteral,
    StringLiteral,
    TextBlock,

    AbstractKeyword,
    AssertKeyword,
    BooleanKeyword,
    BreakKeyword,
    ByteKeyword,
    CaseKeyword,
    CatchKeyword,
    CharKeyword,
    ClassKeyword,
    ConstKeyword,
    ContinueKeyword,
    DefaultKeyword,
    DoKeyword,
    DoubleKeyword,
    ElseKeyword,
    EnumKeyword,
    ExtendsKeyword,
    FinalKeyword,
    FinallyKeyword,
    FloatKeyword,
    ForKeyword,
    GotoKeyword,
    IfKeyword,
    ImplementsKeyword,
    ImportKeyword,
    InstanceofKeyword,
    IntKeyword,
    InterfaceKeyword,
    LongKeyword,
    NativeKeyword,
    NewKeyword,
    PackageKeyword,
    PrivateKeyword,
    ProtectedKeyword,
    PublicKeyword,
    ReturnKeyword,
    ShortKeyword,
    StaticKeyword,
    StrictfpKeyword,
    SuperKeyword,
    SwitchKeyword,
    SynchronizedKeyword,
    ThisKeyword,
    ThrowKeyword,
    ThrowsKeyword,
    TransientKeyword,
    TryKeyword,
    VoidKeyword,
    VolatileKeyword,
    WhileKeyword,
    TrueKeyword,
    FalseKeyword,
    NullKeyword,

    OpenParen,
    CloseParen,
    OpenBrace,
    CloseBrace,
    OpenBracket,
    CloseBracket,
    Semicolon,
    Comma,
    Dot,
    Ellipsis,
    At,
    ColonColon,
    Equals,
    GreaterThan,
    LessThan,
    Exclamation,
    Tilde,
    Question,
    Colon,
    Arrow,
    EqualsEquals,
    GreaterThanEquals,
    LessThanEquals,
    ExclamationEquals,
    AmpersandAmpersand,
    BarBar,
    PlusPlus,
    MinusMinus,
    Plus,
    Minus,
    Asterisk,
    Slash,
    Ampersand,
    Bar,
    Caret,
    Percent,
    LessThanLessThan,
    PlusEquals,
    MinusEquals,
    AsteriskEquals,
    SlashEquals,
    AmpersandEquals,
    BarEquals,
    CaretEquals,
    PercentEquals,
    LessThanLessThanEquals,
    GreaterThanGreaterThanEquals,
    GreaterThanGreaterThanGreaterThanEquals,
}

/// <summary>Facts about token kinds.</summary>
public static class TokenKinds
{
    /// <summary>Whether a kind is a reserved keyword, including the literals <c>true</c>, <c>false</c> and <c>null</c>.</summary>
    public static bool IsKeyword(TokenKind kind) => kind is >= TokenKind.AbstractKeyword and <= TokenKind.NullKeyword;

    public static bool IsLiteral(TokenKind kind) =>
        kind is >= TokenKind.IntegerLiteral and <= TokenKind.TextBlock or TokenKind.TrueKeyword or TokenKind.FalseKeyword or TokenKind.NullKeyword;

    /// <summary>Whether a kind names a primitive type, or <c>void</c>.</summary>
    public static bool IsPrimitiveType(TokenKind kind) => kind is TokenKind.BooleanKeyword or TokenKind.ByteKeyword or TokenKind.ShortKeyword
        or TokenKind.CharKeyword or TokenKind.IntKeyword or TokenKind.LongKeyword or TokenKind.FloatKeyword or TokenKind.DoubleKeyword
        or TokenKind.VoidKeyword;
}
