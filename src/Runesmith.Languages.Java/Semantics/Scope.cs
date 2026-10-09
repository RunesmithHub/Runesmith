using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>What declared a local name.</summary>
internal enum LocalKind
{
    Local,
    Parameter,
    LambdaParameter,
    Pattern,
    Resource,
    CatchParameter,
}

/// <summary>A local variable, parameter or pattern binding; a <c>var</c> local's type is inferred the first time it is asked for.</summary>
internal sealed class LocalSymbol
{
    private readonly Func<TypedExpression>? infer;
    private TypedExpression? type;

    public LocalSymbol(string name, LocalKind kind, SourceSpan nameSpan, TypeReference? type, bool isTypeCertain)
    {
        Name = name;
        Kind = kind;
        NameSpan = nameSpan;
        this.type = new TypedExpression(type, isTypeCertain && JavaTypes.IsConcrete(type), ExpressionKind.Value, this);
    }

    public LocalSymbol(string name, LocalKind kind, SourceSpan nameSpan, Func<TypedExpression> infer)
    {
        Name = name;
        Kind = kind;
        NameSpan = nameSpan;
        this.infer = infer;
    }

    public string Name { get; }

    public LocalKind Kind { get; }

    public SourceSpan NameSpan { get; }

    /// <summary>Gets the declared or inferred type, or null when it is not known.</summary>
    public TypeReference? Type => Typed.Type;

    /// <summary>Gets whether the type is the one the compiler gives the variable, as opposed to a best guess.</summary>
    public bool IsTypeCertain => Typed.IsCertain;

    private TypedExpression Typed => type ??= infer!() with { Kind = ExpressionKind.Value, Symbol = this };
}

/// <summary>What a scope is for.</summary>
internal enum ScopeKind
{
    Type,
    Method,
    Block,
    Lambda,
}

/// <summary>One level of names: a type's members, a method's parameters, or a block's locals; the file's imports are below them all.</summary>
internal sealed class Scope
{
    private List<LocalSymbol>? locals;
    private List<TypeParameter>? typeParameters;
    private Dictionary<string, string>? localTypes;
    private Lazy<bool>? incomplete;

    private Scope(FileScope file, Scope? parent, ScopeKind kind)
    {
        File = file;
        Parent = parent;
        Kind = kind;
    }

    public FileScope File { get; }

    public Scope? Parent { get; }

    public ScopeKind Kind { get; }

    /// <summary>Gets the type whose members are in scope, for a type scope; null when it could not be built.</summary>
    public ClassSymbol? Type { get; private init; }

    /// <summary>Gets the binary name of the type of a type scope.</summary>
    public string? TypeName { get; private init; }

    /// <summary>Gets whether code here has no instance of the enclosing type: a static method, initializer or field.</summary>
    public bool IsStatic { get; private init; }

    /// <summary>Gets whether names may be in scope that the analyzer cannot see, such as members of a supertype no library has.</summary>
    public bool IsIncomplete => incomplete?.Value ?? false;

    public IReadOnlyList<LocalSymbol> Locals => locals ?? (IReadOnlyList<LocalSymbol>)[];

    public IReadOnlyList<TypeParameter> TypeParameters => typeParameters ?? (IReadOnlyList<TypeParameter>)[];

    public IReadOnlyDictionary<string, string> LocalTypes => localTypes ?? (IReadOnlyDictionary<string, string>)EmptyTypes;

    private static Dictionary<string, string> EmptyTypes { get; } = [];

    public static Scope ForType(FileScope file, Scope? parent, ClassSymbol? type, string typeName, Func<bool> isIncomplete) =>
        new(file, parent, ScopeKind.Type) { Type = type, TypeName = typeName, incomplete = new Lazy<bool>(() => type is null || isIncomplete()) };

    public static Scope ForMethod(FileScope file, Scope? parent, bool isStatic) => new(file, parent, ScopeKind.Method) { IsStatic = isStatic };

    public static Scope ForFile(FileScope file) => new(file, null, ScopeKind.Block);

    public static Scope ForBlock(Scope parent) => new(parent.File, parent, ScopeKind.Block);

    public static Scope ForLambda(Scope parent) => new(parent.File, parent, ScopeKind.Lambda);

    public void Declare(LocalSymbol local) => (locals ??= []).Add(local);

    public void DeclareTypeParameter(TypeParameter parameter) => (typeParameters ??= []).Add(parameter);

    public void DeclareLocalType(string simpleName, string binaryName) => (localTypes ??= new(StringComparer.Ordinal))[simpleName] = binaryName;

    /// <summary>Gets the innermost local of this scope with a name that is declared before an offset.</summary>
    public LocalSymbol? FindLocal(string name, int before = int.MaxValue)
    {
        if (locals is null)
            return null;

        for (var i = locals.Count - 1; i >= 0; i--)
        {
            if (locals[i].Name == name && locals[i].NameSpan.Start < before)
                return locals[i];
        }

        return null;
    }

    /// <summary>Gets the innermost type scope, the type the code is in.</summary>
    public Scope? EnclosingType()
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope.Kind == ScopeKind.Type)
                return scope;
        }

        return null;
    }

    /// <summary>Gets whether the code here runs without an instance of a type scope further out.</summary>
    public bool IsStaticBelow(Scope typeScope)
    {
        for (var scope = this; scope is not null && scope != typeScope; scope = scope.Parent)
        {
            if (scope.IsStatic || (scope.Kind == ScopeKind.Type && scope.Type is { } type && type.IsStatic))
                return true;
        }

        return false;
    }

    /// <summary>Gets whether every scope from here down to the file sees all of its names.</summary>
    public bool IsComplete()
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope.IsIncomplete)
                return false;
        }

        return File.IsComplete;
    }

    /// <summary>Finds a type parameter declared in this scope or one further out.</summary>
    public TypeParameter? FindTypeParameter(string name)
    {
        for (var scope = this; scope is not null; scope = scope.Parent)
        {
            if (scope.typeParameters?.FindLast(p => p.Name == name) is { } parameter)
                return parameter;
            if (scope.Type?.TypeParameters.FirstOrDefault(p => p.Name == name) is { } classParameter)
                return classParameter;
        }

        return null;
    }
}
