namespace Runesmith.Languages.Java.ClassFiles;

/// <summary>A field of a class from a class file.</summary>
/// <param name="ConstantValue">The value of a <c>static final</c> constant: an int, long, float, double or string.</param>
public sealed record FieldSymbol(string Name, TypeReference Type, JavaModifiers Modifiers, object? ConstantValue, bool IsEnumConstant, bool IsDeprecated)
{
    public bool IsStatic => (Modifiers & JavaModifiers.Static) != 0;
}

/// <summary>A method or constructor of a class from a class file.</summary>
/// <param name="ParameterNames">The parameter names, from the class file when it has them, and otherwise <c>arg0</c>, <c>arg1</c> and so on.</param>
/// <param name="HasParameterNames">Whether <paramref name="ParameterNames"/> came from the class file.</param>
public sealed record MethodSymbol(
    string Name,
    JavaModifiers Modifiers,
    IReadOnlyList<TypeParameter> TypeParameters,
    IReadOnlyList<TypeReference> ParameterTypes,
    IReadOnlyList<string> ParameterNames,
    bool HasParameterNames,
    TypeReference ReturnType,
    IReadOnlyList<TypeReference> Throws,
    bool IsDeprecated)
{
    /// <summary>The name class files give constructors.</summary>
    public const string ConstructorName = "<init>";

    public bool IsConstructor => Name == ConstructorName;

    public bool IsStatic => (Modifiers & JavaModifiers.Static) != 0;

    public bool IsVarargs => (Modifiers & JavaModifiers.Varargs) != 0;

    public bool IsAbstract => (Modifiers & JavaModifiers.Abstract) != 0;

    public bool IsDefault => (Modifiers & JavaModifiers.Default) != 0;
}

/// <summary>A component of a record.</summary>
public sealed record RecordComponent(string Name, TypeReference Type);

/// <summary>The method or class a local or anonymous class is declared in.</summary>
/// <param name="MethodName">The method, or null when it is declared in an initializer.</param>
public sealed record EnclosingMethod(string ClassName, string? MethodName, string? MethodDescriptor);

/// <summary>A package a module exports.</summary>
/// <param name="IsQualified">Whether only named modules may read it, so it is not public API.</param>
public sealed record ModuleExport(string PackageName, bool IsQualified);

/// <summary>What a <c>module-info.class</c> declares that the class library needs.</summary>
public sealed record ModuleInfo(string Name, IReadOnlyList<ModuleExport> Exports, IReadOnlyList<string> Requires)
{
    /// <summary>Gets the modules required with <c>requires transitive</c>, whose packages a module import brings in too.</summary>
    public IReadOnlyList<string> TransitiveRequires { get; init; } = [];
}
