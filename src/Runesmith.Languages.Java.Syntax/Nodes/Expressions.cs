namespace Runesmith.Languages.Java.Syntax;

/// <summary>An expression.</summary>
public abstract class ExpressionSyntax : SyntaxNode
{
    private protected ExpressionSyntax(SourceSpan span, SyntaxNode?[]? slots)
        : base(span, slots)
    {
    }
}

/// <summary>A literal; its value comes from its source text, through <see cref="JavaLiterals"/>.</summary>
public sealed class LiteralExpressionSyntax : ExpressionSyntax
{
    internal LiteralExpressionSyntax(SourceSpan span, LiteralKind kind)
        : base(span, null)
    {
        Kind = kind;
    }

    public LiteralKind Kind { get; }
}

/// <summary>A simple name used as an expression: a variable, a field, or the start of a qualified name such as <c>java</c> in
/// <c>java.util.List</c>.</summary>
public sealed class NameExpressionSyntax : ExpressionSyntax
{
    internal NameExpressionSyntax(SourceSpan span, IdentifierSyntax name)
        : base(span, [name])
    {
    }

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(0);
}

/// <summary>A dotted access: <c>target.name</c>, a field, or a type or package in a qualified name. The name is missing in a half-typed
/// <c>target.</c>.</summary>
public sealed class FieldAccessExpressionSyntax : ExpressionSyntax
{
    internal FieldAccessExpressionSyntax(SourceSpan span, ExpressionSyntax target, IdentifierSyntax name)
        : base(span, [target, name])
    {
    }

    public ExpressionSyntax Target => Slot<ExpressionSyntax>(0);

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(1);
}

/// <summary>A method call: <c>name(args)</c>, <c>target.name(args)</c> or <c>target.&lt;T&gt;name(args)</c>.</summary>
public sealed class MethodInvocationExpressionSyntax : ExpressionSyntax
{
    internal MethodInvocationExpressionSyntax(SourceSpan span, ExpressionSyntax? target, TypeArgumentListSyntax? typeArguments, IdentifierSyntax name,
        ArgumentListSyntax arguments)
        : base(span, [target, typeArguments, name, arguments])
    {
    }

    public ExpressionSyntax? Target => OptionalSlot<ExpressionSyntax>(0);

    public TypeArgumentListSyntax? TypeArguments => OptionalSlot<TypeArgumentListSyntax>(1);

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(2);

    public ArgumentListSyntax Arguments => Slot<ArgumentListSyntax>(3);
}

/// <summary>The arguments of a call, between parentheses.</summary>
public sealed class ArgumentListSyntax : SyntaxNode
{
    internal ArgumentListSyntax(SourceSpan span, SyntaxList<ExpressionSyntax>? arguments, bool hasCloseParenthesis)
        : base(span, [arguments])
    {
        HasCloseParenthesis = hasCloseParenthesis;
    }

    public SyntaxList<ExpressionSyntax> Arguments => ListSlot<ExpressionSyntax>(0);

    public bool HasCloseParenthesis { get; }
}

/// <summary><c>this</c>, or <c>Outer.this</c>.</summary>
public sealed class ThisExpressionSyntax : ExpressionSyntax
{
    internal ThisExpressionSyntax(SourceSpan span, ExpressionSyntax? qualifier)
        : base(span, [qualifier])
    {
    }

    public ExpressionSyntax? Qualifier => OptionalSlot<ExpressionSyntax>(0);
}

/// <summary><c>super</c> before a member access or a method reference, or <c>Outer.super</c>.</summary>
public sealed class SuperExpressionSyntax : ExpressionSyntax
{
    internal SuperExpressionSyntax(SourceSpan span, ExpressionSyntax? qualifier)
        : base(span, [qualifier])
    {
    }

    public ExpressionSyntax? Qualifier => OptionalSlot<ExpressionSyntax>(0);
}

/// <summary>A class literal: <c>String.class</c>, <c>int[].class</c>.</summary>
public sealed class ClassLiteralExpressionSyntax : ExpressionSyntax
{
    internal ClassLiteralExpressionSyntax(SourceSpan span, TypeSyntax type)
        : base(span, [type])
    {
    }

    public TypeSyntax Type => Slot<TypeSyntax>(0);
}

/// <summary>An expression in parentheses.</summary>
public sealed class ParenthesizedExpressionSyntax : ExpressionSyntax
{
    internal ParenthesizedExpressionSyntax(SourceSpan span, ExpressionSyntax expression)
        : base(span, [expression])
    {
    }

    public ExpressionSyntax Expression => Slot<ExpressionSyntax>(0);
}

/// <summary><c>new T(args)</c>, with type arguments, an outer instance (<c>outer.new Inner()</c>) or an anonymous class body.</summary>
public sealed class ObjectCreationExpressionSyntax : ExpressionSyntax
{
    internal ObjectCreationExpressionSyntax(SourceSpan span, ExpressionSyntax? outer, TypeArgumentListSyntax? constructorTypeArguments,
        TypeSyntax type, ArgumentListSyntax arguments, ClassBodySyntax? body)
        : base(span, [outer, constructorTypeArguments, type, arguments, body])
    {
    }

    public ExpressionSyntax? Outer => OptionalSlot<ExpressionSyntax>(0);

    public TypeArgumentListSyntax? ConstructorTypeArguments => OptionalSlot<TypeArgumentListSyntax>(1);

    /// <summary>Gets the class created; its type arguments are empty for the diamond <c>&lt;&gt;</c>.</summary>
    public TypeSyntax Type => Slot<TypeSyntax>(2);

    public ArgumentListSyntax Arguments => Slot<ArgumentListSyntax>(3);

    /// <summary>Gets the body of an anonymous class.</summary>
    public ClassBodySyntax? Body => OptionalSlot<ClassBodySyntax>(4);
}

/// <summary><c>new int[n][]</c> or <c>new String[] { "a" }</c>.</summary>
public sealed class ArrayCreationExpressionSyntax : ExpressionSyntax
{
    internal ArrayCreationExpressionSyntax(SourceSpan span, TypeSyntax elementType, SyntaxList<DimensionSyntax> dimensions,
        ArrayInitializerExpressionSyntax? initializer)
        : base(span, [elementType, dimensions, initializer])
    {
    }

    public TypeSyntax ElementType => Slot<TypeSyntax>(0);

    /// <summary>Gets the brackets; those with a size come first.</summary>
    public SyntaxList<DimensionSyntax> Dimensions => ListSlot<DimensionSyntax>(1);

    public ArrayInitializerExpressionSyntax? Initializer => OptionalSlot<ArrayInitializerExpressionSyntax>(2);
}

/// <summary>An array initializer: <c>{ 1, 2, 3 }</c>.</summary>
public sealed class ArrayInitializerExpressionSyntax : ExpressionSyntax
{
    internal ArrayInitializerExpressionSyntax(SourceSpan span, SyntaxList<ExpressionSyntax>? elements)
        : base(span, [elements])
    {
    }

    public SyntaxList<ExpressionSyntax> Elements => ListSlot<ExpressionSyntax>(0);
}

/// <summary><c>array[index]</c>.</summary>
public sealed class ArrayAccessExpressionSyntax : ExpressionSyntax
{
    internal ArrayAccessExpressionSyntax(SourceSpan span, ExpressionSyntax array, ExpressionSyntax index)
        : base(span, [array, index])
    {
    }

    public ExpressionSyntax Array => Slot<ExpressionSyntax>(0);

    public ExpressionSyntax Index => Slot<ExpressionSyntax>(1);
}

/// <summary>A prefix or postfix unary operation, such as <c>-x</c> or <c>i++</c>.</summary>
public sealed class UnaryExpressionSyntax : ExpressionSyntax
{
    internal UnaryExpressionSyntax(SourceSpan span, UnaryOperator @operator, ExpressionSyntax operand)
        : base(span, [operand])
    {
        Operator = @operator;
    }

    public UnaryOperator Operator { get; }

    public ExpressionSyntax Operand => Slot<ExpressionSyntax>(0);
}

/// <summary>A binary operation, such as <c>a + b</c> or <c>a &amp;&amp; b</c>.</summary>
public sealed class BinaryExpressionSyntax : ExpressionSyntax
{
    internal BinaryExpressionSyntax(SourceSpan span, BinaryOperator @operator, ExpressionSyntax left, ExpressionSyntax right)
        : base(span, [left, right])
    {
        Operator = @operator;
    }

    public BinaryOperator Operator { get; }

    public ExpressionSyntax Left => Slot<ExpressionSyntax>(0);

    public ExpressionSyntax Right => Slot<ExpressionSyntax>(1);
}

/// <summary>An assignment: <c>a = b</c> or a compound one such as <c>a += b</c>.</summary>
public sealed class AssignmentExpressionSyntax : ExpressionSyntax
{
    internal AssignmentExpressionSyntax(SourceSpan span, AssignmentOperator @operator, ExpressionSyntax left, ExpressionSyntax right)
        : base(span, [left, right])
    {
        Operator = @operator;
    }

    public AssignmentOperator Operator { get; }

    public ExpressionSyntax Left => Slot<ExpressionSyntax>(0);

    public ExpressionSyntax Right => Slot<ExpressionSyntax>(1);
}

/// <summary><c>condition ? whenTrue : whenFalse</c>.</summary>
public sealed class ConditionalExpressionSyntax : ExpressionSyntax
{
    internal ConditionalExpressionSyntax(SourceSpan span, ExpressionSyntax condition, ExpressionSyntax whenTrue, ExpressionSyntax whenFalse)
        : base(span, [condition, whenTrue, whenFalse])
    {
    }

    public ExpressionSyntax Condition => Slot<ExpressionSyntax>(0);

    public ExpressionSyntax WhenTrue => Slot<ExpressionSyntax>(1);

    public ExpressionSyntax WhenFalse => Slot<ExpressionSyntax>(2);
}

/// <summary><c>expression instanceof Type</c>, or with a pattern: <c>expression instanceof String s</c>.</summary>
public sealed class InstanceofExpressionSyntax : ExpressionSyntax
{
    internal InstanceofExpressionSyntax(SourceSpan span, ExpressionSyntax expression, SyntaxNode typeOrPattern)
        : base(span, [expression, typeOrPattern])
    {
    }

    public ExpressionSyntax Expression => Slot<ExpressionSyntax>(0);

    /// <summary>Gets the type tested, or the pattern matched.</summary>
    public SyntaxNode TypeOrPattern => Slot<SyntaxNode>(1);

    public PatternSyntax? Pattern => TypeOrPattern as PatternSyntax;
}

/// <summary>A cast: <c>(Type) expression</c>.</summary>
public sealed class CastExpressionSyntax : ExpressionSyntax
{
    internal CastExpressionSyntax(SourceSpan span, TypeSyntax type, ExpressionSyntax expression)
        : base(span, [type, expression])
    {
    }

    public TypeSyntax Type => Slot<TypeSyntax>(0);

    public ExpressionSyntax Expression => Slot<ExpressionSyntax>(1);
}

/// <summary>A lambda: <c>x -&gt; x + 1</c>, <c>(int a, int b) -&gt; { ... }</c>.</summary>
public sealed class LambdaExpressionSyntax : ExpressionSyntax
{
    internal LambdaExpressionSyntax(SourceSpan span, SyntaxList<ParameterSyntax>? parameters, bool hasParentheses, SyntaxNode body)
        : base(span, [parameters, body])
    {
        HasParentheses = hasParentheses;
    }

    /// <summary>Gets the parameters; one without a type has its type inferred.</summary>
    public SyntaxList<ParameterSyntax> Parameters => ListSlot<ParameterSyntax>(0);

    public bool HasParentheses { get; }

    /// <summary>Gets the body: an expression or a block.</summary>
    public SyntaxNode Body => Slot<SyntaxNode>(1);
}

/// <summary>A method reference: <c>String::valueOf</c>, <c>this::run</c>, <c>ArrayList::new</c>, <c>int[]::new</c>.</summary>
public sealed class MethodReferenceExpressionSyntax : ExpressionSyntax
{
    internal MethodReferenceExpressionSyntax(SourceSpan span, SyntaxNode target, TypeArgumentListSyntax? typeArguments, IdentifierSyntax name)
        : base(span, [target, typeArguments, name])
    {
    }

    /// <summary>Gets what the method belongs to: an expression, or a type when it is generic or an array.</summary>
    public SyntaxNode Target => Slot<SyntaxNode>(0);

    public TypeArgumentListSyntax? TypeArguments => OptionalSlot<TypeArgumentListSyntax>(1);

    /// <summary>Gets the method's name, <c>new</c> for a constructor.</summary>
    public IdentifierSyntax Name => Slot<IdentifierSyntax>(2);

    public bool IsConstructorReference => Name.Text == "new";
}

/// <summary>A switch expression.</summary>
public sealed class SwitchExpressionSyntax : ExpressionSyntax
{
    internal SwitchExpressionSyntax(SourceSpan span, ExpressionSyntax selector, SyntaxList<SwitchCaseSyntax>? cases)
        : base(span, [selector, cases])
    {
    }

    public ExpressionSyntax Selector => Slot<ExpressionSyntax>(0);

    public SyntaxList<SwitchCaseSyntax> Cases => ListSlot<SwitchCaseSyntax>(1);
}

/// <summary>An expression that is missing where one belongs.</summary>
public sealed class MissingExpressionSyntax : ExpressionSyntax
{
    internal MissingExpressionSyntax(int position)
        : base(new SourceSpan(position, 0), null)
    {
    }

    public override bool IsMissing => true;
}
