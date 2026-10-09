using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Languages.Java.Semantics;

internal sealed partial class SemanticModel
{
    /// <summary>Resolves a type as written; names no library has become <see cref="UnresolvedTypeReference"/>s, and <c>var</c> is null.</summary>
    public TypeReference? ResolveType(TypeSyntax syntax, Scope? scope)
    {
        switch (syntax)
        {
            case PrimitiveTypeSyntax primitive:
                return Primitive(primitive.Kind);
            case ArrayTypeSyntax array:
            {
                var element = ResolveType(array.ElementType, scope);
                if (element is null)
                    return null;
                for (var i = 0; i < array.Rank; i++)
                    element = new ArrayTypeReference(element);
                return element;
            }

            case ClassTypeSyntax classType:
                return ResolveClassType(classType, scope);
            case IntersectionTypeSyntax intersection when intersection.Types.Count > 0:
                return ResolveType(intersection.Types[0], scope);
            case UnionTypeSyntax union when union.Types.Count > 0:
                return ResolveType(union.Types[0], scope);
            default:
                return null;
        }
    }

    /// <summary>Adds the extra brackets of a declarator, as in <c>int a[]</c>, to its type.</summary>
    public static TypeReference? WithDimensions(TypeReference? type, int dimensions)
    {
        for (var i = 0; type is not null && i < dimensions; i++)
            type = new ArrayTypeReference(type);
        return type;
    }

    /// <summary>Finds what a simple type name means in a scope: a type variable, a local, member or imported class, or null.</summary>
    public TypeReference? LookupTypeName(string name, Scope? scope)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.TypeParameters.Any(p => p.Name == name))
                return new TypeVariableReference(name);
            if (current.LocalTypes.TryGetValue(name, out var local))
                return new ClassTypeReference(local, []);
            if (current.Kind != ScopeKind.Type || current.Type is not { } type)
                continue;

            if (type.SimpleName == name && IsLocalType(type.BinaryName))
                return new ClassTypeReference(type.BinaryName, []);
            if (FindMemberType(type, name) is { } member)
                return new ClassTypeReference(member, []);
            if (type.TypeParameters.Any(p => p.Name == name))
                return new TypeVariableReference(name);
        }

        return FileScope.LookupType(name) is { } imported ? new ClassTypeReference(imported, []) : null;
    }

    /// <summary>Gets whether a simple type name might name a type the analyzer cannot see from a scope, so not finding it proves nothing.</summary>
    public bool MayHideType(string name, Scope? scope) => FileScope.IsUnresolvedImport(name) || !(scope?.IsComplete() ?? FileScope.IsComplete);

    private TypeReference ResolveClassType(ClassTypeSyntax syntax, Scope? scope)
    {
        string? binary;
        if (syntax.Qualifier is null)
        {
            var found = LookupTypeName(syntax.Name.Text, scope);
            if (found is TypeVariableReference variable)
                return variable;
            binary = (found as ClassTypeReference)?.BinaryName;
        }
        else if (ResolveQualifierAsType(syntax.Qualifier, scope) is { } outer)
        {
            binary = FindMemberType(outer, syntax.Name.Text);
        }
        else
        {
            var qualified = syntax.Qualifier.QualifiedName + "." + syntax.Name.Text;
            binary = Project.Contains(qualified) ? qualified : null;
        }

        if (binary is null)
            return new UnresolvedTypeReference(syntax.QualifiedName);

        var arguments = syntax.TypeArguments is { IsDiamond: false } list
            ? list.Arguments.Select(a => ResolveArgument(a, scope)).ToArray()
            : [];
        return new ClassTypeReference(binary, arguments);
    }

    /// <summary>Resolves the qualifier of a qualified type name as a type, or returns null when it names a package.</summary>
    public string? ResolveQualifierAsType(ClassTypeSyntax qualifier, Scope? scope)
    {
        if (qualifier.Qualifier is null)
            return (LookupTypeName(qualifier.Name.Text, scope) as ClassTypeReference)?.BinaryName;

        if (ResolveQualifierAsType(qualifier.Qualifier, scope) is { } outer)
            return FindMemberType(outer, qualifier.Name.Text);

        var name = qualifier.Qualifier.QualifiedName + "." + qualifier.Name.Text;
        return Project.Contains(name) ? name : null;
    }

    private TypeArgument ResolveArgument(TypeSyntax argument, Scope? scope)
    {
        if (argument is WildcardTypeSyntax wildcard)
        {
            var bound = wildcard.Bound is null ? null : ResolveType(wildcard.Bound, scope);
            return wildcard.BoundKind switch
            {
                WildcardBoundKind.Extends when bound is not null => new TypeArgument(WildcardKind.Extends, bound),
                WildcardBoundKind.Super when bound is not null => new TypeArgument(WildcardKind.Super, bound),
                _ => TypeArgument.Unbounded,
            };
        }

        var type = ResolveType(argument, scope);
        return type is null ? TypeArgument.Unbounded : new TypeArgument(WildcardKind.None, type);
    }

    /// <summary>Resolves type parameters, declaring their names first so their bounds can name them.</summary>
    public IReadOnlyList<TypeParameter> DeclareTypeParameters(TypeParameterListSyntax? list, Scope scope)
    {
        if (list is null || list.Parameters.Count == 0)
            return [];

        var names = list.Parameters.Where(p => !p.Name.IsMissing).Select(p => p.Name.Text).ToList();
        foreach (var name in names)
            scope.DeclareTypeParameter(new TypeParameter(name, []));

        var resolved = list.Parameters.Where(p => !p.Name.IsMissing)
            .Select(p => new TypeParameter(p.Name.Text, [.. p.Bounds.Select(b => ResolveType(b, scope)).OfType<TypeReference>()]))
            .ToList();
        foreach (var parameter in resolved)
            scope.DeclareTypeParameter(parameter);
        return resolved;
    }

    public static PrimitiveTypeReference Primitive(PrimitiveTypeKind kind) => kind switch
    {
        PrimitiveTypeKind.Boolean => PrimitiveTypeReference.Boolean,
        PrimitiveTypeKind.Byte => PrimitiveTypeReference.Byte,
        PrimitiveTypeKind.Short => PrimitiveTypeReference.Short,
        PrimitiveTypeKind.Char => PrimitiveTypeReference.Char,
        PrimitiveTypeKind.Int => PrimitiveTypeReference.Int,
        PrimitiveTypeKind.Long => PrimitiveTypeReference.Long,
        PrimitiveTypeKind.Float => PrimitiveTypeReference.Float,
        PrimitiveTypeKind.Double => PrimitiveTypeReference.Double,
        _ => PrimitiveTypeReference.Void,
    };
}
