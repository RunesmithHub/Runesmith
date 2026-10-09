using Runesmith.Languages.Java.ClassFiles;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>A type named in source that no library has, kept by its name so signatures still show it.</summary>
internal sealed record UnresolvedTypeReference(string Name) : TypeReference
{
    public override string ToJavaString(bool qualified = true) => Name;
}

/// <summary>The type of the <c>null</c> literal, which converts to every reference type.</summary>
internal sealed record NullTypeReference : TypeReference
{
    public static NullTypeReference Instance { get; } = new();

    public override string ToJavaString(bool qualified = true) => "null";
}

/// <summary>Facts about types: well-known types, boxing, numeric promotion, substitution of type variables and erasure.</summary>
internal static class JavaTypes
{
    public static ClassTypeReference Object { get; } = ClassTypeReference.Object;

    public static ClassTypeReference String { get; } = Class("java.lang.String");

    public static ClassTypeReference Iterable { get; } = Class("java.lang.Iterable");

    public static ClassTypeReference Class(string binaryName, params TypeReference[] arguments) =>
        new(binaryName, [.. arguments.Select(a => new TypeArgument(WildcardKind.None, a))]);

    /// <summary>Gets the binary name of a class type, or null for other types.</summary>
    public static string? ClassName(TypeReference? type) => (type as ClassTypeReference)?.BinaryName;

    public static bool IsNumeric(PrimitiveKind kind) => kind is not (PrimitiveKind.Boolean or PrimitiveKind.Void);

    public static bool IsBoolean(TypeReference? type) =>
        type is PrimitiveTypeReference { Kind: PrimitiveKind.Boolean } or ClassTypeReference { BinaryName: "java.lang.Boolean" };

    /// <summary>Gets the class a primitive boxes to.</summary>
    public static ClassTypeReference Box(PrimitiveKind kind) => Class(kind switch
    {
        PrimitiveKind.Boolean => "java.lang.Boolean",
        PrimitiveKind.Byte => "java.lang.Byte",
        PrimitiveKind.Char => "java.lang.Character",
        PrimitiveKind.Short => "java.lang.Short",
        PrimitiveKind.Int => "java.lang.Integer",
        PrimitiveKind.Long => "java.lang.Long",
        PrimitiveKind.Float => "java.lang.Float",
        PrimitiveKind.Double => "java.lang.Double",
        _ => "java.lang.Void",
    });

    /// <summary>Gets the primitive a class unboxes to, or null.</summary>
    public static PrimitiveKind? Unbox(TypeReference? type) => ClassName(type) switch
    {
        "java.lang.Boolean" => PrimitiveKind.Boolean,
        "java.lang.Byte" => PrimitiveKind.Byte,
        "java.lang.Character" => PrimitiveKind.Char,
        "java.lang.Short" => PrimitiveKind.Short,
        "java.lang.Integer" => PrimitiveKind.Int,
        "java.lang.Long" => PrimitiveKind.Long,
        "java.lang.Float" => PrimitiveKind.Float,
        "java.lang.Double" => PrimitiveKind.Double,
        _ => null,
    };

    /// <summary>Gets the primitive kind of a type after unboxing, or null.</summary>
    public static PrimitiveKind? PrimitiveOf(TypeReference? type) => type is PrimitiveTypeReference primitive ? primitive.Kind : Unbox(type);

    /// <summary>Gets a reference type for a type, boxing primitives.</summary>
    public static TypeReference AsReference(TypeReference type) => type is PrimitiveTypeReference primitive ? Box(primitive.Kind) : type;

    /// <summary>Binary numeric promotion: the type of <c>a + b</c> for numbers.</summary>
    public static PrimitiveTypeReference? Promote(TypeReference? left, TypeReference? right)
    {
        if (PrimitiveOf(left) is not { } a || PrimitiveOf(right) is not { } b || !IsNumeric(a) || !IsNumeric(b))
            return null;
        if (a == PrimitiveKind.Double || b == PrimitiveKind.Double)
            return PrimitiveTypeReference.Double;
        if (a == PrimitiveKind.Float || b == PrimitiveKind.Float)
            return PrimitiveTypeReference.Float;
        return a == PrimitiveKind.Long || b == PrimitiveKind.Long ? PrimitiveTypeReference.Long : PrimitiveTypeReference.Int;
    }

    /// <summary>Unary numeric promotion: the type of <c>-a</c>.</summary>
    public static PrimitiveTypeReference? Promote(TypeReference? operand) =>
        PrimitiveOf(operand) is { } kind && IsNumeric(kind)
            ? kind is PrimitiveKind.Byte or PrimitiveKind.Short or PrimitiveKind.Char ? PrimitiveTypeReference.Int : new PrimitiveTypeReference(kind)
            : null;

    /// <summary>Replaces type variables by the types a map gives them; variables the map does not have, or maps to null, stay.</summary>
    public static TypeReference Substitute(TypeReference type, IReadOnlyDictionary<string, TypeReference?>? map)
    {
        if (map is null || map.Count == 0)
            return type;

        return type switch
        {
            TypeVariableReference variable => map.TryGetValue(variable.Name, out var value) && value is not null ? value : variable,
            ArrayTypeReference array => new ArrayTypeReference(Substitute(array.ElementType, map)),
            ClassTypeReference { TypeArguments.Count: > 0 } generic => generic with
            {
                TypeArguments = [.. generic.TypeArguments.Select(a => a.Type is null ? a : a with { Type = Substitute(a.Type, map) })],
            },
            _ => type,
        };
    }


    /// <summary>Gets whether a type mentions a type variable or a type no library has.</summary>
    public static bool HasOpenParts(TypeReference? type) => type switch
    {
        null => true,
        TypeVariableReference or UnresolvedTypeReference => true,
        ArrayTypeReference array => HasOpenParts(array.ElementType),
        ClassTypeReference generic => generic.TypeArguments.Any(a => a.Type is not null && HasOpenParts(a.Type)),
        _ => false,
    };

    /// <summary>Gets whether the outermost part of a type is known, which is all the members of a value depend on.</summary>
    public static bool IsConcrete(TypeReference? type) => type switch
    {
        ClassTypeReference or PrimitiveTypeReference or NullTypeReference => true,
        ArrayTypeReference array => IsConcrete(array.ElementType),
        _ => false,
    };

    /// <summary>Removes type arguments, for comparing types by their classes.</summary>
    public static TypeReference Erase(TypeReference type) => type switch
    {
        ClassTypeReference { TypeArguments.Count: > 0 } generic => new ClassTypeReference(generic.BinaryName, []),
        ArrayTypeReference array => new ArrayTypeReference(Erase(array.ElementType)),
        _ => type,
    };

    /// <summary>Gets the type as source writes it, with simple names.</summary>
    public static string Display(TypeReference? type) => type?.ToJavaString(qualified: false) ?? "?";
}
