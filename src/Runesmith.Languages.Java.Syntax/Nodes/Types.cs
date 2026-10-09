namespace Runesmith.Languages.Java.Syntax;

/// <summary>A type as written in the source.</summary>
public abstract class TypeSyntax : SyntaxNode
{
    private protected TypeSyntax(SourceSpan span, SyntaxNode?[]? slots)
        : base(span, slots)
    {
    }
}

/// <summary>A primitive type, or <c>void</c>, with its annotations.</summary>
public sealed class PrimitiveTypeSyntax : TypeSyntax
{
    internal PrimitiveTypeSyntax(SourceSpan span, PrimitiveTypeKind kind, SyntaxList<AnnotationSyntax>? annotations)
        : base(span, [annotations])
    {
        Kind = kind;
    }

    public PrimitiveTypeKind Kind { get; }

    public SyntaxList<AnnotationSyntax> Annotations => ListSlot<AnnotationSyntax>(0);
}

/// <summary>A class or interface type, such as <c>Map.Entry&lt;K, V&gt;</c>; each dotted part is a class type with its qualifier.</summary>
public sealed class ClassTypeSyntax : TypeSyntax
{
    internal ClassTypeSyntax(SourceSpan span, ClassTypeSyntax? qualifier, SyntaxList<AnnotationSyntax>? annotations, IdentifierSyntax name,
        TypeArgumentListSyntax? typeArguments)
        : base(span, [qualifier, annotations, name, typeArguments])
    {
    }

    /// <summary>Gets the part before the last dot, such as <c>Map</c> in <c>Map.Entry</c>; it may be a package name.</summary>
    public ClassTypeSyntax? Qualifier => OptionalSlot<ClassTypeSyntax>(0);

    public SyntaxList<AnnotationSyntax> Annotations => ListSlot<AnnotationSyntax>(1);

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(2);

    /// <summary>Gets the type arguments; an empty list is the diamond <c>&lt;&gt;</c>.</summary>
    public TypeArgumentListSyntax? TypeArguments => OptionalSlot<TypeArgumentListSyntax>(3);

    /// <summary>Gets the name with its qualifiers, without type arguments, such as <c>java.util.Map.Entry</c>.</summary>
    public string QualifiedName => Qualifier is { } qualifier ? qualifier.QualifiedName + "." + Name.Text : Name.Text;
}

/// <summary>Type arguments: <c>&lt;String, ? extends Number&gt;</c>, or the diamond <c>&lt;&gt;</c>.</summary>
public sealed class TypeArgumentListSyntax : SyntaxNode
{
    internal TypeArgumentListSyntax(SourceSpan span, SyntaxList<TypeSyntax>? arguments)
        : base(span, [arguments])
    {
    }

    public SyntaxList<TypeSyntax> Arguments => ListSlot<TypeSyntax>(0);

    public bool IsDiamond => Arguments.Count == 0;
}

/// <summary>An array type, such as <c>int[][]</c>.</summary>
public sealed class ArrayTypeSyntax : TypeSyntax
{
    internal ArrayTypeSyntax(SourceSpan span, TypeSyntax elementType, SyntaxList<DimensionSyntax> dimensions)
        : base(span, [elementType, dimensions])
    {
    }

    public TypeSyntax ElementType => Slot<TypeSyntax>(0);

    public SyntaxList<DimensionSyntax> Dimensions => ListSlot<DimensionSyntax>(1);

    public int Rank => Dimensions.Count;
}

/// <summary>One pair of brackets with its annotations: <c>@A []</c>, or <c>[n]</c> in an array creation.</summary>
public sealed class DimensionSyntax : SyntaxNode
{
    internal DimensionSyntax(SourceSpan span, SyntaxList<AnnotationSyntax>? annotations, ExpressionSyntax? size)
        : base(span, [annotations, size])
    {
    }

    public SyntaxList<AnnotationSyntax> Annotations => ListSlot<AnnotationSyntax>(0);

    /// <summary>Gets the length of an array creation's dimension, or null for <c>[]</c>.</summary>
    public ExpressionSyntax? Size => OptionalSlot<ExpressionSyntax>(1);
}

/// <summary>A wildcard type argument: <c>?</c>, <c>? extends T</c> or <c>? super T</c>.</summary>
public sealed class WildcardTypeSyntax : TypeSyntax
{
    internal WildcardTypeSyntax(SourceSpan span, SyntaxList<AnnotationSyntax>? annotations, WildcardBoundKind boundKind, TypeSyntax? bound)
        : base(span, [annotations, bound])
    {
        BoundKind = boundKind;
    }

    public SyntaxList<AnnotationSyntax> Annotations => ListSlot<AnnotationSyntax>(0);

    public WildcardBoundKind BoundKind { get; }

    public TypeSyntax? Bound => OptionalSlot<TypeSyntax>(1);
}

/// <summary><c>var</c> in place of a local variable's, a lambda parameter's or a pattern's type, inferred by the compiler.</summary>
public sealed class VarTypeSyntax : TypeSyntax
{
    internal VarTypeSyntax(SourceSpan span)
        : base(span, null)
    {
    }
}

/// <summary>An intersection of types in a cast: <c>(Runnable &amp; Serializable)</c>.</summary>
public sealed class IntersectionTypeSyntax : TypeSyntax
{
    internal IntersectionTypeSyntax(SourceSpan span, SyntaxList<TypeSyntax> types)
        : base(span, [types])
    {
    }

    public SyntaxList<TypeSyntax> Types => ListSlot<TypeSyntax>(0);
}

/// <summary>The exception types of a multi-catch: <c>IOException | SQLException</c>.</summary>
public sealed class UnionTypeSyntax : TypeSyntax
{
    internal UnionTypeSyntax(SourceSpan span, SyntaxList<TypeSyntax> types)
        : base(span, [types])
    {
    }

    public SyntaxList<TypeSyntax> Types => ListSlot<TypeSyntax>(0);
}

/// <summary>A type that is missing where one belongs.</summary>
public sealed class MissingTypeSyntax : TypeSyntax
{
    internal MissingTypeSyntax(int position)
        : base(new SourceSpan(position, 0), null)
    {
    }

    public override bool IsMissing => true;
}

/// <summary>Type parameters: <c>&lt;T extends Comparable&lt;T&gt;, U&gt;</c>.</summary>
public sealed class TypeParameterListSyntax : SyntaxNode
{
    internal TypeParameterListSyntax(SourceSpan span, SyntaxList<TypeParameterSyntax>? parameters)
        : base(span, [parameters])
    {
    }

    public SyntaxList<TypeParameterSyntax> Parameters => ListSlot<TypeParameterSyntax>(0);
}

/// <summary>A type parameter with its bounds.</summary>
public sealed class TypeParameterSyntax : SyntaxNode
{
    internal TypeParameterSyntax(SourceSpan span, SyntaxList<AnnotationSyntax>? annotations, IdentifierSyntax name, SyntaxList<TypeSyntax>? bounds)
        : base(span, [annotations, name, bounds])
    {
    }

    public SyntaxList<AnnotationSyntax> Annotations => ListSlot<AnnotationSyntax>(0);

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(1);

    public SyntaxList<TypeSyntax> Bounds => ListSlot<TypeSyntax>(2);
}
