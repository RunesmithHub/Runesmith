using System.Text;

namespace Runesmith.Languages.Java.ClassFiles;

/// <summary>Java's primitive types, and <c>void</c> for method results.</summary>
public enum PrimitiveKind : byte
{
    Boolean,
    Byte,
    Char,
    Short,
    Int,
    Long,
    Float,
    Double,
    Void,
}

/// <summary>A type as a class file names it: a primitive, a class with type arguments, an array or a type variable.</summary>
public abstract record TypeReference
{
    /// <summary>Gets the type as Java source writes it, with package names, such as <c>java.util.List&lt;java.lang.String&gt;</c>.</summary>
    public abstract string ToJavaString(bool qualified = true);

    public sealed override string ToString() => ToJavaString();
}

/// <summary>A primitive type or <c>void</c>.</summary>
public sealed record PrimitiveTypeReference(PrimitiveKind Kind) : TypeReference
{
    public static PrimitiveTypeReference Boolean { get; } = new(PrimitiveKind.Boolean);

    public static PrimitiveTypeReference Byte { get; } = new(PrimitiveKind.Byte);

    public static PrimitiveTypeReference Char { get; } = new(PrimitiveKind.Char);

    public static PrimitiveTypeReference Short { get; } = new(PrimitiveKind.Short);

    public static PrimitiveTypeReference Int { get; } = new(PrimitiveKind.Int);

    public static PrimitiveTypeReference Long { get; } = new(PrimitiveKind.Long);

    public static PrimitiveTypeReference Float { get; } = new(PrimitiveKind.Float);

    public static PrimitiveTypeReference Double { get; } = new(PrimitiveKind.Double);

    public static PrimitiveTypeReference Void { get; } = new(PrimitiveKind.Void);

    public override string ToJavaString(bool qualified = true) => Kind switch
    {
        PrimitiveKind.Boolean => "boolean",
        PrimitiveKind.Byte => "byte",
        PrimitiveKind.Char => "char",
        PrimitiveKind.Short => "short",
        PrimitiveKind.Int => "int",
        PrimitiveKind.Long => "long",
        PrimitiveKind.Float => "float",
        PrimitiveKind.Double => "double",
        _ => "void",
    };
}

/// <summary>A class or interface type, with its type arguments and, for an inner class of a generic class, the outer type.</summary>
/// <param name="BinaryName">The class's binary name with dots between packages, such as <c>java.util.Map$Entry</c>.</param>
/// <param name="Outer">The parameterized outer type, for an inner class written as <c>Outer&lt;T&gt;.Inner</c>.</param>
public sealed record ClassTypeReference(string BinaryName, IReadOnlyList<TypeArgument> TypeArguments, ClassTypeReference? Outer = null) : TypeReference
{
    /// <summary>The type <c>java.lang.Object</c>.</summary>
    public static ClassTypeReference Object { get; } = new("java.lang.Object", []);

    public bool Equals(ClassTypeReference? other) =>
        other is not null && BinaryName == other.BinaryName && TypeArguments.SequenceEqual(other.TypeArguments) && Equals(Outer, other.Outer);

    public override int GetHashCode() => HashCode.Combine(BinaryName, TypeArguments.Count);

    public override string ToJavaString(bool qualified = true)
    {
        var text = new StringBuilder();
        if (Outer is { TypeArguments.Count: > 0 })
        {
            text.Append(Outer.ToJavaString(qualified)).Append('.').Append(ClassNames.SimpleName(BinaryName));
        }
        else
        {
            text.Append(qualified ? ClassNames.SourceName(BinaryName) : ClassNames.NestedName(BinaryName));
        }

        if (TypeArguments.Count > 0)
            text.Append('<').AppendJoin(", ", TypeArguments.Select(a => a.ToJavaString(qualified))).Append('>');
        return text.ToString();
    }
}

/// <summary>An array type.</summary>
public sealed record ArrayTypeReference(TypeReference ElementType) : TypeReference
{
    public override string ToJavaString(bool qualified = true) => ElementType.ToJavaString(qualified) + "[]";
}

/// <summary>A reference to a type variable, such as <c>T</c>.</summary>
public sealed record TypeVariableReference(string Name) : TypeReference
{
    public override string ToJavaString(bool qualified = true) => Name;
}

/// <summary>How a type argument constrains its type.</summary>
public enum WildcardKind : byte
{
    /// <summary>An exact type argument, such as <c>String</c>.</summary>
    None,

    /// <summary><c>? extends T</c>.</summary>
    Extends,

    /// <summary><c>? super T</c>.</summary>
    Super,

    /// <summary><c>?</c>.</summary>
    Unbounded,
}

/// <summary>A type argument: a type, or a wildcard with an optional bound.</summary>
public sealed record TypeArgument(WildcardKind Wildcard, TypeReference? Type)
{
    public static TypeArgument Unbounded { get; } = new(WildcardKind.Unbounded, null);

    public string ToJavaString(bool qualified = true) => Wildcard switch
    {
        WildcardKind.Unbounded => "?",
        WildcardKind.Extends => "? extends " + Type!.ToJavaString(qualified),
        WildcardKind.Super => "? super " + Type!.ToJavaString(qualified),
        _ => Type!.ToJavaString(qualified),
    };
}

/// <summary>A type parameter of a class or method, with its bounds; an empty list means <c>Object</c>.</summary>
public sealed record TypeParameter(string Name, IReadOnlyList<TypeReference> Bounds)
{
    public bool Equals(TypeParameter? other) => other is not null && Name == other.Name && Bounds.SequenceEqual(other.Bounds);

    public override int GetHashCode() => HashCode.Combine(Name, Bounds.Count);

    public string ToJavaString(bool qualified = true) =>
        Bounds.Count == 0 || Bounds is [ClassTypeReference { BinaryName: "java.lang.Object" }]
            ? Name
            : Name + " extends " + string.Join(" & ", Bounds.Select(b => b.ToJavaString(qualified)));
}
