namespace Runesmith.Languages.Java.Syntax;

/// <summary>The modifiers of a declaration.</summary>
[Flags]
public enum JavaModifiers
{
    None = 0,
    Public = 1 << 0,
    Protected = 1 << 1,
    Private = 1 << 2,
    Static = 1 << 3,
    Abstract = 1 << 4,
    Final = 1 << 5,
    Native = 1 << 6,
    Synchronized = 1 << 7,
    Transient = 1 << 8,
    Volatile = 1 << 9,
    Strictfp = 1 << 10,
    Default = 1 << 11,
    Sealed = 1 << 12,
    NonSealed = 1 << 13,
}

/// <summary>The primitive types, and <c>void</c>.</summary>
public enum PrimitiveTypeKind
{
    Boolean,
    Byte,
    Short,
    Char,
    Int,
    Long,
    Float,
    Double,
    Void,
}

/// <summary>How a wildcard is bounded: <c>?</c>, <c>? extends T</c> or <c>? super T</c>.</summary>
public enum WildcardBoundKind
{
    None,
    Extends,
    Super,
}

/// <summary>The forms of import declarations.</summary>
public enum ImportKind
{
    /// <summary><c>import java.util.List;</c></summary>
    Single,

    /// <summary><c>import java.util.*;</c></summary>
    OnDemand,

    /// <summary><c>import static java.lang.Math.max;</c></summary>
    Static,

    /// <summary><c>import static java.lang.Math.*;</c></summary>
    StaticOnDemand,

    /// <summary><c>import module java.base;</c>, since Java 25.</summary>
    Module,
}

/// <summary>The directives of a module declaration.</summary>
public enum ModuleDirectiveKind
{
    Requires,
    Exports,
    Opens,
    Uses,
    Provides,
}

/// <summary>The modifiers of a <c>requires</c> directive.</summary>
[Flags]
public enum RequiresModifiers
{
    None = 0,
    Transitive = 1,
    Static = 2,
}

/// <summary>The kinds of literals.</summary>
public enum LiteralKind
{
    Integer,
    Long,
    Float,
    Double,
    Character,
    String,
    TextBlock,
    True,
    False,
    Null,
}

/// <summary>The unary operators, prefix and postfix.</summary>
public enum UnaryOperator
{
    Plus,
    Minus,
    BitwiseNot,
    LogicalNot,
    PreIncrement,
    PreDecrement,
    PostIncrement,
    PostDecrement,
}

/// <summary>The binary operators, from <c>*</c> to <c>||</c>.</summary>
public enum BinaryOperator
{
    Multiply,
    Divide,
    Remainder,
    Add,
    Subtract,
    LeftShift,
    RightShift,
    UnsignedRightShift,
    LessThan,
    GreaterThan,
    LessThanOrEqual,
    GreaterThanOrEqual,
    Equals,
    NotEquals,
    BitwiseAnd,
    ExclusiveOr,
    BitwiseOr,
    LogicalAnd,
    LogicalOr,
}

/// <summary>The assignment operators: <c>=</c> and the compound ones such as <c>+=</c>.</summary>
public enum AssignmentOperator
{
    Assign,
    Add,
    Subtract,
    Multiply,
    Divide,
    Remainder,
    BitwiseAnd,
    BitwiseOr,
    ExclusiveOr,
    LeftShift,
    RightShift,
    UnsignedRightShift,
}
