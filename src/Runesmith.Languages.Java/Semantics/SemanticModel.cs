using System.Collections.Concurrent;
using System.Globalization;
using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>A class or interface with the types its type variables stand for in the type it was reached from.</summary>
/// <param name="Map">Each type variable of <paramref name="Symbol"/> and its type there; null for a raw type or an unbounded wildcard.</param>
/// <param name="IsExact">Whether no <c>? super</c> wildcard was read as its bound on the way.</param>
internal sealed record HierarchyEntry(ClassSymbol Symbol, ClassTypeReference Type, IReadOnlyDictionary<string, TypeReference?>? Map, bool IsExact);

/// <summary>A field or method as a member of a type: where it is declared, and the types its owner's type variables stand for.</summary>
internal sealed record MemberRef<T>(T Member, ClassSymbol Owner, IReadOnlyDictionary<string, TypeReference?>? Map, bool IsExact)
    where T : class
{
    /// <summary>Gets a member's type with its owner's type variables replaced.</summary>
    public TypeReference Substitute(TypeReference type) => JavaTypes.Substitute(type, Map);
}

/// <summary>What the analyzer knows about one file of a project snapshot: its imports, its types and the local and anonymous classes
/// it declares; resolves type names and finds the members of types.</summary>
/// <remarks>Safe to use from several threads; everything it learns is kept for the snapshot's lifetime.</remarks>
internal sealed partial class SemanticModel
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Func<ClassSymbol?>> localTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ClassSymbol?> builtLocalTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<SyntaxNode, string> localTypeNames = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, SyntaxNode> localDeclarations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<ClassTypeReference, HierarchyEntry[]> hierarchies = new();
    private readonly ConcurrentDictionary<(ClassTypeReference Type, string? Name), MemberRef<MethodSymbol>[]> methods = new();
    private FileScope? fileScope;
    private int localCount;

    public SemanticModel(ProjectSnapshot project, SourceFile file)
    {
        Project = project;
        File = file;
    }

    public ProjectSnapshot Project { get; }

    public SourceFile File { get; }

    public JavaSyntaxTree Tree => File.Tree;

    public FileScope FileScope
    {
        get
        {
            if (Volatile.Read(ref fileScope) is { } scope)
                return scope;
            Interlocked.CompareExchange(ref fileScope, new FileScope(this), null);
            return fileScope!;
        }
    }

    /// <summary>Finds a type: one of this file's local or anonymous classes, or one on the project's class path.</summary>
    public ClassSymbol? FindClass(string binaryName)
    {
        Func<ClassSymbol?>? build;
        lock (gate)
        {
            if (builtLocalTypes.TryGetValue(binaryName, out var built))
                return built;
            if (!localTypes.TryGetValue(binaryName, out build))
                return Project.FindClass(binaryName);
        }

        var symbol = build();
        lock (gate)
            builtLocalTypes[binaryName] = symbol;
        return symbol;
    }

    public bool IsLocalType(string binaryName)
    {
        lock (gate)
            return localTypes.ContainsKey(binaryName);
    }

    /// <summary>Gets whether a type and its supertypes are all known, so a member it lacks is certainly missing.</summary>
    public bool IsFullyKnown(string binaryName)
    {
        if (!IsLocalType(binaryName))
            return Project.IsFullyKnown(binaryName);

        if (FindClass(binaryName) is not { } symbol)
            return false;
        return (symbol.SuperClass is null || IsFullyKnown(symbol.SuperClass.BinaryName)) && symbol.Interfaces.All(i => IsFullyKnown(i.BinaryName));
    }

    /// <summary>Gets the declaration of a local or anonymous class of this file.</summary>
    public SyntaxNode? LocalDeclaration(string binaryName)
    {
        lock (gate)
            return localDeclarations.GetValueOrDefault(binaryName);
    }

    /// <summary>Registers a local or anonymous class, keyed by its declaration, and returns its binary name; its symbol is built when first
    /// asked for.</summary>
    public string DeclareLocalType(SyntaxNode declaration, string simpleName, Func<string, ClassSymbol?> build)
    {
        lock (gate)
        {
            if (localTypeNames.TryGetValue(declaration, out var existing))
                return existing;

            var outer = File.Types.Count > 0 ? File.Types[0].BinaryName : "local";
            var name = outer + "$" + (++localCount).ToString(CultureInfo.InvariantCulture) + simpleName;
            localTypeNames[declaration] = name;
            localDeclarations[name] = declaration;
            localTypes[name] = () => build(name);
            return name;
        }
    }

    /// <summary>Gets a type and its supertypes, nearest first, each with the types its type variables stand for; interfaces end with
    /// <c>java.lang.Object</c>.</summary>
    /// <param name="symbolOfType">The type's symbol when it is at hand, such as while it is being built; such hierarchies are not kept.</param>
    public IReadOnlyList<HierarchyEntry> Hierarchy(ClassTypeReference type, ClassSymbol? symbolOfType = null)
    {
        if (symbolOfType is null && hierarchies.TryGetValue(type, out var known))
            return known;

        var (entries, complete) = ComputeHierarchy(type, symbolOfType);
        if (complete && symbolOfType is null)
            hierarchies.TryAdd(type, entries);
        return entries;
    }

    private (HierarchyEntry[] Entries, bool Complete) ComputeHierarchy(ClassTypeReference type, ClassSymbol? symbolOfType)
    {
        var entries = new List<HierarchyEntry>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<(ClassTypeReference Type, bool Exact)>();
        var complete = true;
        pending.Enqueue((type, true));
        while (pending.TryDequeue(out var next))
        {
            var found = visited.Count == 0 && symbolOfType is not null ? symbolOfType : FindClass(next.Type.BinaryName);
            if (!visited.Add(next.Type.BinaryName))
                continue;
            if (found is not { } symbol)
            {
                complete = false;
                continue;
            }

            var (map, exact) = MapOf(symbol, next.Type);
            exact &= next.Exact;
            entries.Add(new HierarchyEntry(symbol, next.Type, map, exact));
            if (symbol.SuperClass is { } superClass && JavaTypes.Substitute(superClass, map) is ClassTypeReference substituted)
                pending.Enqueue((substituted, exact));
            foreach (var implemented in symbol.Interfaces)
            {
                if (JavaTypes.Substitute(implemented, map) is ClassTypeReference substitutedInterface)
                    pending.Enqueue((substitutedInterface, exact));
            }
        }

        if (entries.Count > 0 && !visited.Contains(JavaTypes.Object.BinaryName) && FindClass(JavaTypes.Object.BinaryName) is { } objectSymbol)
            entries.Add(new HierarchyEntry(objectSymbol, JavaTypes.Object, null, true));
        return ([.. entries], complete);
    }

    /// <summary>Gets the fields of a type and its supertypes, nearest first, optionally only those with a name.</summary>
    public IEnumerable<MemberRef<FieldSymbol>> Fields(ClassTypeReference type, string? name = null)
    {
        foreach (var entry in Hierarchy(type))
        {
            foreach (var field in entry.Symbol.Fields)
            {
                if (name is null || field.Name == name)
                    yield return new MemberRef<FieldSymbol>(field, entry.Symbol, entry.Map, entry.IsExact);
            }
        }
    }

    /// <summary>Gets the methods of a type and its supertypes without the ones they override, optionally only those with a name; no
    /// constructors.</summary>
    public IReadOnlyList<MemberRef<MethodSymbol>> Methods(ClassTypeReference type, string? name = null)
    {
        if (methods.TryGetValue((type, name), out var known))
            return known;

        var hierarchy = Hierarchy(type);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var found = new List<MemberRef<MethodSymbol>>();
        foreach (var entry in hierarchy)
        {
            foreach (var method in entry.Symbol.Methods)
            {
                if (method.IsConstructor || (name is not null && method.Name != name))
                    continue;

                var reference = new MemberRef<MethodSymbol>(method, entry.Symbol, entry.Map, entry.IsExact);
                if (seen.Add(OverrideKey(reference)))
                    found.Add(reference);
            }
        }

        MemberRef<MethodSymbol>[] result = [.. found];
        if (hierarchies.ContainsKey(type))
            methods.TryAdd((type, name), result);
        return result;
    }

    /// <summary>Gets the constructors of a class.</summary>
    public IEnumerable<MemberRef<MethodSymbol>> Constructors(ClassTypeReference type)
    {
        if (FindClass(type.BinaryName) is not { } symbol)
            yield break;

        var (map, exact) = MapOf(symbol, type);
        foreach (var method in symbol.Methods)
        {
            if (method.IsConstructor)
                yield return new MemberRef<MethodSymbol>(method, symbol, map, exact);
        }
    }

    /// <summary>Finds a member type of a type, declared in it or inherited, as its binary name.</summary>
    public string? FindMemberType(string ownerBinaryName, string simpleName) => FindMemberType(new ClassTypeReference(ownerBinaryName, []), null, simpleName);

    /// <summary>Finds a member type of a type whose symbol is at hand, such as one still being built.</summary>
    public string? FindMemberType(ClassSymbol owner, string simpleName) => FindMemberType(new ClassTypeReference(owner.BinaryName, []), owner, simpleName);

    private string? FindMemberType(ClassTypeReference owner, ClassSymbol? symbol, string simpleName)
    {
        var hierarchy = Hierarchy(owner, symbol);
        for (var i = 0; i < hierarchy.Count; i++)
        {
            foreach (var nested in hierarchy[i].Symbol.NestedClasses)
            {
                if (ClassNames.SimpleName(nested) == simpleName && (i == 0 || IsInherited(nested)))
                    return nested;
            }
        }

        return null;
    }

    /// <summary>Gets the member types of a type, declared and inherited.</summary>
    public IEnumerable<string> MemberTypes(string ownerBinaryName)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hierarchy = Hierarchy(new ClassTypeReference(ownerBinaryName, []));
        for (var i = 0; i < hierarchy.Count; i++)
        {
            foreach (var nested in hierarchy[i].Symbol.NestedClasses)
            {
                if ((i == 0 || IsInherited(nested)) && seen.Add(ClassNames.SimpleName(nested)))
                    yield return nested;
            }
        }
    }

    // Private member types are not inherited; one whose symbol is not at hand yet counts as inherited.
    private bool IsInherited(string nested) => FindClass(nested) is not { } symbol || (symbol.Modifiers & ClassFiles.JavaModifiers.Private) == 0;

    /// <summary>Gets whether a type has a static field, method or member type with a name, declared or inherited.</summary>
    public bool HasStaticMember(string typeBinaryName, string name)
    {
        var type = new ClassTypeReference(typeBinaryName, []);
        return Fields(type, name).Any(f => f.Member.IsStatic) || Methods(type, name).Any(m => m.Member.IsStatic) || FindMemberType(typeBinaryName, name) is not null;
    }

    /// <summary>Gets the type a class declaration's members see as <c>this</c>: the class with its own type variables as arguments.</summary>
    public static ClassTypeReference SelfType(ClassSymbol symbol) => new(symbol.BinaryName,
        [.. symbol.TypeParameters.Select(p => new TypeArgument(WildcardKind.None, new TypeVariableReference(p.Name)))]);

    // A raw type or an unbounded wildcard reads as the erasure of the type variable's bound, as the compiler does, which is not exact.
    private static (IReadOnlyDictionary<string, TypeReference?>? Map, bool Exact) MapOf(ClassSymbol symbol, ClassTypeReference type)
    {
        if (symbol.TypeParameters.Count == 0)
            return (null, true);

        var map = new Dictionary<string, TypeReference?>(symbol.TypeParameters.Count, StringComparer.Ordinal);
        var exact = true;
        var parameterized = type.TypeArguments.Count == symbol.TypeParameters.Count;
        for (var i = 0; i < symbol.TypeParameters.Count; i++)
        {
            var parameter = symbol.TypeParameters[i];
            var argument = parameterized ? type.TypeArguments[i] : TypeArgument.Unbounded;
            if (argument is { Wildcard: WildcardKind.Unbounded } or { Type: null })
            {
                map[parameter.Name] = parameter.Bounds is [ClassTypeReference bound, ..] ? bound with { TypeArguments = [] } : JavaTypes.Object;
                exact = false;
                continue;
            }

            map[parameter.Name] = argument.Type;
            exact &= argument.Wildcard != WildcardKind.Super;
        }

        return (map, exact);
    }

    private static string OverrideKey(MemberRef<MethodSymbol> method)
    {
        var key = new System.Text.StringBuilder(method.Member.Name).Append('(');
        foreach (var parameter in method.Member.ParameterTypes)
            key.Append(ErasedName(method.Substitute(parameter), method.Member)).Append(',');
        return key.ToString();
    }

    // Overrides are matched by erasure; a type variable is erased to its first bound, or Object.
    private static string ErasedName(TypeReference type, MethodSymbol method) => type switch
    {
        ClassTypeReference generic => generic.BinaryName,
        ArrayTypeReference array => ErasedName(array.ElementType, method) + "[]",
        TypeVariableReference variable => method.TypeParameters.FirstOrDefault(p => p.Name == variable.Name)?.Bounds is [ClassTypeReference bound, ..]
            ? bound.BinaryName
            : "java.lang.Object",
        _ => type.ToJavaString(),
    };
}
