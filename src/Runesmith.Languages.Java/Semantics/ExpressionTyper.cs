using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>What an expression or a name is.</summary>
internal enum ExpressionKind
{
    Unknown,
    Value,
    Type,
    Package,
}

/// <summary>The type of an expression, and whether it is the type the compiler gives it.</summary>
/// <param name="IsCertain">Whether the type, at least its class, is the one the compiler gives the expression; only certain types are used to
/// call members missing.</param>
/// <param name="Symbol">What the expression names: a <see cref="LocalSymbol"/>, a field or method <see cref="MemberRef{T}"/>, a type's binary
/// name for a type, or a package's name for a package.</param>
internal readonly record struct TypedExpression(TypeReference? Type, bool IsCertain, ExpressionKind Kind = ExpressionKind.Value, object? Symbol = null)
{
    public static TypedExpression Unknown => default;

    public static TypedExpression Certain(TypeReference type) => new(type, true);
}

/// <summary>A call's chosen method and the methods it was chosen from.</summary>
/// <param name="IsUnique">Whether the choice is the compiler's: one method fits best, or all that fit best return the same class.</param>
internal sealed record CallResolution(IReadOnlyList<MemberRef<MethodSymbol>> Candidates, MemberRef<MethodSymbol>? Chosen, bool IsUnique,
    bool IsReceiverCertain);

/// <summary>Finds the types of expressions: literals, names, field and method access with type arguments substituted, overloads chosen by
/// their arguments, <c>new</c>, casts, operators, and lambda parameters from the target type.</summary>
/// <remarks>Results are kept per expression, so one instance serves one request on one file.</remarks>
internal sealed class ExpressionTyper(SemanticModel model)
{
    private readonly Dictionary<SyntaxNode, TypedExpression> memo = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SyntaxNode, CallResolution> calls = new(ReferenceEqualityComparer.Instance);

    public SemanticModel Model => model;

    public TypedExpression TypeOf(ExpressionSyntax expression, Scope scope)
    {
        if (memo.TryGetValue(expression, out var known))
            return known;

        var result = Compute(expression, scope);
        memo[expression] = result;
        return result;
    }

    /// <summary>Finds what a simple name means where it is used: a local, a field, a type or a package.</summary>
    public TypedExpression ResolveName(string name, Scope scope, int position)
    {
        var certain = true;
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.Kind != ScopeKind.Type)
            {
                if (current.FindLocal(name, position) is { } local)
                    return new TypedExpression(local.Type, certain && local.IsTypeCertain, ExpressionKind.Value, local);
                continue;
            }

            if (current.Type is { } type && model.Fields(SemanticModel.SelfType(type), name).FirstOrDefault() is { } field)
                return FieldValue(field, certain && !current.IsIncomplete);
            if (current.IsIncomplete)
                certain = false;
        }

        foreach (var (type, member) in scope.File.StaticImports)
        {
            if (member == name && model.Fields(new ClassTypeReference(type, []), name).FirstOrDefault(f => f.Member.IsStatic) is { } imported)
                return FieldValue(imported, certain);
        }

        foreach (var type in scope.File.StaticOnDemandImports)
        {
            if (model.Fields(new ClassTypeReference(type, []), name).FirstOrDefault(f => f.Member.IsStatic) is { } imported)
                return FieldValue(imported, certain);
        }

        certain &= scope.File.IsComplete;
        switch (model.LookupTypeName(name, scope))
        {
            case ClassTypeReference typeName:
                return new TypedExpression(typeName, certain, ExpressionKind.Type, typeName.BinaryName);
            case TypeVariableReference variable:
                return new TypedExpression(variable, false, ExpressionKind.Type);
        }

        return model.Project.IsPackage(name) ? new TypedExpression(null, certain, ExpressionKind.Package, name) : TypedExpression.Unknown;
    }

    /// <summary>Gets the class whose members a value of a type has: the class itself, a type variable's bound, or <c>Object</c> for arrays.</summary>
    public static ClassTypeReference? ReceiverClass(TypeReference? type, Scope? scope) => type switch
    {
        ClassTypeReference classType => classType,
        TypeVariableReference variable => scope?.FindTypeParameter(variable.Name)?.Bounds.OfType<ClassTypeReference>()
            .OrderBy(b => b.BinaryName == JavaTypes.Object.BinaryName).FirstOrDefault() ?? JavaTypes.Object,
        ArrayTypeReference => JavaTypes.Object,
        _ => null,
    };

    /// <summary>Finds the methods a call can mean and chooses one by its arguments.</summary>
    public CallResolution ResolveCall(MethodInvocationExpressionSyntax call, Scope scope)
    {
        if (calls.TryGetValue(call, out var known))
            return known;

        var (candidates, receiverCertain) = CallCandidates(call, scope);
        var (chosen, unique) = Choose(candidates, call.Arguments, scope);
        var resolution = new CallResolution(candidates, chosen, unique, receiverCertain);
        calls[call] = resolution;
        return resolution;
    }

    /// <summary>Finds the constructors a <c>new</c> can mean and chooses one by its arguments.</summary>
    public CallResolution ResolveCreation(ObjectCreationExpressionSyntax creation, Scope scope)
    {
        if (calls.TryGetValue(creation, out var known))
            return known;

        var type = CreatedType(creation, scope) as ClassTypeReference;
        var candidates = type is null ? [] : model.Constructors(type).ToList();
        var (chosen, unique) = Choose(candidates, creation.Arguments, scope);
        var resolution = new CallResolution(candidates, chosen, unique, type is not null);
        calls[creation] = resolution;
        return resolution;
    }

    /// <summary>Gets the type a lambda or method reference argument must have, from the call it is passed to, or null.</summary>
    public TypeReference? ParameterTypeFor(SyntaxNode call, int argumentIndex, Scope scope)
    {
        var resolution = call switch
        {
            MethodInvocationExpressionSyntax invocation => ResolveCall(invocation, scope),
            ObjectCreationExpressionSyntax creation => ResolveCreation(creation, scope),
            _ => null,
        };
        var arguments = call switch
        {
            MethodInvocationExpressionSyntax invocation => invocation.Arguments,
            ObjectCreationExpressionSyntax creation => creation.Arguments,
            _ => null,
        };
        if (resolution?.Chosen is not { } chosen || arguments is null)
            return null;

        var bindings = Bindings(chosen, arguments, scope, argumentIndex, call as MethodInvocationExpressionSyntax);
        var parameter = ParameterAt(chosen.Member, argumentIndex, arguments.Arguments.Count);
        return parameter is null ? null : JavaTypes.Substitute(chosen.Substitute(parameter), bindings);
    }

    /// <summary>Gets the abstract method of a functional interface, the one a lambda implements, or null.</summary>
    public MemberRef<MethodSymbol>? FunctionalMethod(TypeReference? target)
    {
        if (target is not ClassTypeReference type || model.FindClass(type.BinaryName) is not { Kind: ClassKind.Interface })
            return null;

        MemberRef<MethodSymbol>? found = null;
        foreach (var method in model.Methods(type))
        {
            if (!method.Member.IsAbstract || method.Member.IsStatic || IsObjectMethod(method.Member))
                continue;
            if (found is not null && (found.Member.Name != method.Member.Name || found.Member.ParameterTypes.Count != method.Member.ParameterTypes.Count))
                return null;
            found ??= method;
        }

        return found;
    }

    /// <summary>Gets the types of a lambda's parameters: those written, or those of the functional interface it implements.</summary>
    public IReadOnlyList<TypeReference?> LambdaParameterTypes(LambdaExpressionSyntax lambda, TypeReference? target, Scope scope)
    {
        var parameters = lambda.Parameters;
        var types = new TypeReference?[parameters.Count];
        var method = FunctionalMethod(target);
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Type is { } written and not VarTypeSyntax)
                types[i] = model.ResolveType(written, scope);
            else if (method is not null && i < method.Member.ParameterTypes.Count)
                types[i] = WithoutOpenVariables(method.Substitute(method.Member.ParameterTypes[i]), method.Member);
        }

        return types;
    }

    private TypedExpression Compute(ExpressionSyntax expression, Scope scope) => expression switch
    {
        LiteralExpressionSyntax literal => Literal(literal.Kind),
        ParenthesizedExpressionSyntax parenthesized => TypeOf(parenthesized.Expression, scope) with { Kind = ExpressionKind.Value },
        NameExpressionSyntax name => name.Name.IsMissing ? TypedExpression.Unknown : ResolveName(name.Name.Text, scope, name.Start),
        FieldAccessExpressionSyntax access => MemberAccess(access, scope),
        MethodInvocationExpressionSyntax call => Invocation(call, scope),
        ObjectCreationExpressionSyntax creation => Creation(creation, scope),
        ArrayCreationExpressionSyntax array => ArrayCreation(array, scope),
        ArrayAccessExpressionSyntax access => TypeOf(access.Array, scope) is { Type: ArrayTypeReference array } typed
            ? new TypedExpression(array.ElementType, typed.IsCertain)
            : TypedExpression.Unknown,
        CastExpressionSyntax cast => model.ResolveType(cast.Type, scope) is { } castType
            ? new TypedExpression(castType, JavaTypes.IsConcrete(castType))
            : TypedExpression.Unknown,
        ConditionalExpressionSyntax conditional => Conditional(conditional, scope),
        BinaryExpressionSyntax binary => Binary(binary, scope),
        UnaryExpressionSyntax unary => Unary(unary, scope),
        AssignmentExpressionSyntax assignment => TypeOf(assignment.Left, scope) with { Kind = ExpressionKind.Value, Symbol = null },
        InstanceofExpressionSyntax => TypedExpression.Certain(PrimitiveTypeReference.Boolean),
        ThisExpressionSyntax self => This(self.Qualifier, scope),
        SuperExpressionSyntax super => Super(super.Qualifier, scope),
        ClassLiteralExpressionSyntax literal => model.ResolveType(literal.Type, scope) is { } literalType
            ? TypedExpression.Certain(JavaTypes.Class("java.lang.Class", JavaTypes.AsReference(literalType)))
            : TypedExpression.Certain(JavaTypes.Class("java.lang.Class")),
        SwitchExpressionSyntax switchExpression => SwitchValue(switchExpression, scope),
        _ => TypedExpression.Unknown,
    };

    private static TypedExpression Literal(LiteralKind kind) => TypedExpression.Certain(kind switch
    {
        LiteralKind.Integer => PrimitiveTypeReference.Int,
        LiteralKind.Long => PrimitiveTypeReference.Long,
        LiteralKind.Float => PrimitiveTypeReference.Float,
        LiteralKind.Double => PrimitiveTypeReference.Double,
        LiteralKind.Character => PrimitiveTypeReference.Char,
        LiteralKind.True or LiteralKind.False => PrimitiveTypeReference.Boolean,
        LiteralKind.Null => NullTypeReference.Instance,
        _ => JavaTypes.String,
    });

    private static TypedExpression FieldValue(MemberRef<FieldSymbol> field, bool certain)
    {
        var type = field.Substitute(field.Member.Type);
        return new TypedExpression(type, certain && field.IsExact && JavaTypes.IsConcrete(type), ExpressionKind.Value, field);
    }

    private TypedExpression MemberAccess(FieldAccessExpressionSyntax access, Scope scope)
    {
        if (access.Name.IsMissing)
            return TypedExpression.Unknown;

        var name = access.Name.Text;
        var target = TypeOf(access.Target, scope);
        switch (target.Kind)
        {
            case ExpressionKind.Package:
            {
                var qualified = (string)target.Symbol! + "." + name;
                if (model.Project.Contains(qualified))
                    return new TypedExpression(new ClassTypeReference(qualified, []), target.IsCertain, ExpressionKind.Type, qualified);
                return model.Project.IsPackage(qualified) ? new TypedExpression(null, target.IsCertain, ExpressionKind.Package, qualified) : TypedExpression.Unknown;
            }

            case ExpressionKind.Type when target.Type is ClassTypeReference owner:
            {
                if (model.Fields(owner, name).FirstOrDefault(f => f.Member.IsStatic) is { } field)
                    return FieldValue(field, target.IsCertain);
                return model.FindMemberType(owner.BinaryName, name) is { } nested
                    ? new TypedExpression(new ClassTypeReference(nested, []), target.IsCertain, ExpressionKind.Type, nested)
                    : TypedExpression.Unknown;
            }

            case ExpressionKind.Value:
            {
                if (target.Type is ArrayTypeReference && name == "length")
                    return new TypedExpression(PrimitiveTypeReference.Int, target.IsCertain);
                if (ReceiverClass(target.Type, scope) is not { } receiver)
                    return TypedExpression.Unknown;
                return model.Fields(receiver, name).FirstOrDefault() is { } field
                    ? FieldValue(field, target.IsCertain && target.Type is ClassTypeReference)
                    : TypedExpression.Unknown;
            }

            default:
                return TypedExpression.Unknown;
        }
    }

    private TypedExpression Invocation(MethodInvocationExpressionSyntax call, Scope scope)
    {
        if (call.Name.IsMissing)
            return TypedExpression.Unknown;

        if (call.Name.Text == "clone" && call.Target is { } target && TypeOf(target, scope) is { Kind: ExpressionKind.Value, Type: ArrayTypeReference array } typed)
            return new TypedExpression(array, typed.IsCertain);

        var resolution = ResolveCall(call, scope);
        if (resolution.Chosen is not { } chosen)
            return TypedExpression.Unknown;

        var bindings = Bindings(chosen, call.Arguments, scope, -1, call);
        var returnType = JavaTypes.Substitute(chosen.Substitute(chosen.Member.ReturnType), bindings);
        var open = JavaTypes.HasOpenParts(returnType) && returnType is not ClassTypeReference;
        returnType = WithoutOpenVariables(returnType, chosen.Member);
        var certain = resolution.IsReceiverCertain && resolution.IsUnique && chosen.IsExact && !open && JavaTypes.IsConcrete(returnType);
        return new TypedExpression(returnType, certain, ExpressionKind.Value, chosen);
    }

    private (IReadOnlyList<MemberRef<MethodSymbol>> Candidates, bool ReceiverCertain) CallCandidates(MethodInvocationExpressionSyntax call, Scope scope)
    {
        var name = call.Name.Text;
        if (call.Target is null)
        {
            var certain = true;
            for (var current = scope; current is not null; current = current.Parent)
            {
                if (current.Kind != ScopeKind.Type)
                    continue;
                if (current.Type is { } type)
                {
                    var methods = model.Methods(SemanticModel.SelfType(type), name).ToList();
                    if (methods.Count > 0)
                        return (methods, certain && !current.IsIncomplete);
                }

                certain &= !current.IsIncomplete;
            }

            var imported = scope.File.StaticImports.Where(i => i.Member == name)
                .SelectMany(i => model.Methods(new ClassTypeReference(i.Type, []), name))
                .Concat(scope.File.StaticOnDemandImports.SelectMany(t => model.Methods(new ClassTypeReference(t, []), name)))
                .Where(m => m.Member.IsStatic)
                .ToList();
            return (imported, certain && scope.File.IsComplete);
        }

        if (call.Target is SuperExpressionSyntax super)
        {
            var superType = Super(super.Qualifier, scope);
            return superType.Type is ClassTypeReference superClass ? (model.Methods(superClass, name).ToList(), superType.IsCertain) : ([], false);
        }

        var receiver = TypeOf(call.Target, scope);
        return receiver.Kind switch
        {
            ExpressionKind.Type when receiver.Type is ClassTypeReference owner => (model.Methods(owner, name).ToList(), receiver.IsCertain),
            ExpressionKind.Value when ReceiverClass(receiver.Type, scope) is { } owner =>
                (model.Methods(owner, name).ToList(), receiver.IsCertain && receiver.Type is ClassTypeReference or ArrayTypeReference),
            _ => ([], false),
        };
    }

    /// <summary>Chooses among methods by the number of arguments, then by how well the known argument types fit.</summary>
    public (MemberRef<MethodSymbol>? Chosen, bool Unique) Choose(IReadOnlyList<MemberRef<MethodSymbol>> candidates, ArgumentListSyntax arguments, Scope scope)
    {
        var count = arguments.Arguments.Count;
        var applicable = candidates.Where(c => Accepts(c.Member, count)).ToList();
        if (applicable.Count == 0)
            return (candidates.Count > 0 ? candidates[0] : null, false);
        if (applicable.Count == 1)
            return (applicable[0], true);

        var argumentTypes = arguments.Arguments
            .Select(a => a is LambdaExpressionSyntax or MethodReferenceExpressionSyntax ? TypedExpression.Unknown : TypeOf(a, scope))
            .ToList();
        var best = -1;
        var chosen = new List<MemberRef<MethodSymbol>>();
        foreach (var candidate in applicable)
        {
            if (Score(candidate, argumentTypes) is not { } score || score < best)
                continue;
            if (score > best)
            {
                best = score;
                chosen.Clear();
            }

            chosen.Add(candidate);
        }

        if (chosen.Count == 0)
            return (applicable[0], false);

        var returns = chosen.Select(c => JavaTypes.Erase(c.Substitute(c.Member.ReturnType))).Distinct().Count();
        return (chosen[0], chosen.Count == 1 || returns == 1);
    }

    private int? Score(MemberRef<MethodSymbol> candidate, List<TypedExpression> arguments)
    {
        var score = 0;
        for (var i = 0; i < arguments.Count; i++)
        {
            var parameter = ParameterAt(candidate.Member, i, arguments.Count);
            if (parameter is null)
                return null;
            if (arguments[i].Type is not { } argument || arguments[i].Kind != ExpressionKind.Value)
            {
                score += 1;
                continue;
            }

            var expected = candidate.Substitute(parameter);
            switch (IsAssignable(argument, expected))
            {
                case true:
                    score += JavaTypes.Erase(argument) == JavaTypes.Erase(expected) ? 3 : 2;
                    break;
                case false:
                    return null;
                default:
                    score += 1;
                    break;
            }
        }

        return score;
    }

    private static bool Accepts(MethodSymbol method, int count) =>
        method.ParameterTypes.Count == count || (method.IsVarargs && count >= method.ParameterTypes.Count - 1);

    /// <summary>Gets the parameter an argument is passed to, the element type of a varargs array for the arguments it collects.</summary>
    public static TypeReference? ParameterAt(MethodSymbol method, int index, int argumentCount)
    {
        var parameters = method.ParameterTypes;
        if (parameters.Count == 0)
            return null;
        if (method.IsVarargs && index >= parameters.Count - 1 && parameters[^1] is ArrayTypeReference varargs
            && (argumentCount != parameters.Count || index > parameters.Count - 1))
        {
            return varargs.ElementType;
        }

        return index < parameters.Count ? parameters[index] : null;
    }

    /// <summary>Whether a value of one type can be passed where another is expected; null when the analyzer cannot tell.</summary>
    public bool? IsAssignable(TypeReference from, TypeReference to)
    {
        if (to is TypeVariableReference or UnresolvedTypeReference || from is UnresolvedTypeReference or TypeVariableReference)
            return null;
        if (from is NullTypeReference)
            return to is not PrimitiveTypeReference;

        if (from is PrimitiveTypeReference fromPrimitive)
        {
            return to switch
            {
                PrimitiveTypeReference toPrimitive => Widens(fromPrimitive.Kind, toPrimitive.Kind),
                ClassTypeReference => IsSubclass(JavaTypes.Box(fromPrimitive.Kind), (ClassTypeReference)to),
                _ => false,
            };
        }

        return to switch
        {
            PrimitiveTypeReference toPrimitive => JavaTypes.Unbox(from) is { } unboxed ? Widens(unboxed, toPrimitive.Kind) : false,
            ClassTypeReference toClass => from switch
            {
                ClassTypeReference fromClass => IsSubclass(fromClass, toClass),
                ArrayTypeReference => toClass.BinaryName is "java.lang.Object" or "java.lang.Cloneable" or "java.io.Serializable",
                _ => null,
            },
            ArrayTypeReference toArray => from is ArrayTypeReference fromArray
                ? fromArray.ElementType is PrimitiveTypeReference || toArray.ElementType is PrimitiveTypeReference
                    ? fromArray.ElementType == toArray.ElementType
                    : IsAssignable(fromArray.ElementType, toArray.ElementType)
                : false,
            _ => null,
        };
    }

    private bool? IsSubclass(ClassTypeReference from, ClassTypeReference to)
    {
        if (to.BinaryName == "java.lang.Object" || from.BinaryName == to.BinaryName)
            return true;
        if (model.Hierarchy(new ClassTypeReference(from.BinaryName, [])).Any(e => e.Symbol.BinaryName == to.BinaryName))
            return true;
        return model.IsFullyKnown(from.BinaryName) ? false : null;
    }

    private static bool Widens(PrimitiveKind from, PrimitiveKind to)
    {
        if (from == to)
            return true;
        return from switch
        {
            PrimitiveKind.Byte => to is PrimitiveKind.Short or PrimitiveKind.Int or PrimitiveKind.Long or PrimitiveKind.Float or PrimitiveKind.Double,
            PrimitiveKind.Short or PrimitiveKind.Char => to is PrimitiveKind.Int or PrimitiveKind.Long or PrimitiveKind.Float or PrimitiveKind.Double,
            PrimitiveKind.Int => to is PrimitiveKind.Long or PrimitiveKind.Float or PrimitiveKind.Double,
            PrimitiveKind.Long => to is PrimitiveKind.Float or PrimitiveKind.Double,
            PrimitiveKind.Float => to is PrimitiveKind.Double,
            _ => false,
        };
    }

    /// <summary>Infers a generic method's type variables from the explicit type arguments and the types of the arguments, skipping one.</summary>
    private Dictionary<string, TypeReference?>? Bindings(MemberRef<MethodSymbol> chosen, ArgumentListSyntax arguments, Scope scope, int skip,
        MethodInvocationExpressionSyntax? call)
    {
        var method = chosen.Member;
        if (method.TypeParameters.Count == 0)
            return null;

        var variables = method.TypeParameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var bindings = new Dictionary<string, TypeReference?>(StringComparer.Ordinal);
        if (call?.TypeArguments is { IsDiamond: false } explicitArguments)
        {
            for (var i = 0; i < explicitArguments.Arguments.Count && i < method.TypeParameters.Count; i++)
                bindings[method.TypeParameters[i].Name] = model.ResolveType(explicitArguments.Arguments[i], scope);
            return bindings;
        }

        var count = arguments.Arguments.Count;
        var lambdas = new List<(int Index, LambdaExpressionSyntax Lambda)>();
        for (var i = 0; i < count; i++)
        {
            var argument = arguments.Arguments[i];
            if (i == skip || ParameterAt(method, i, count) is not { } parameter)
                continue;
            if (argument is LambdaExpressionSyntax lambda)
            {
                lambdas.Add((i, lambda));
                continue;
            }

            if (argument is not MethodReferenceExpressionSyntax && TypeOf(argument, scope) is { Kind: ExpressionKind.Value, Type: { } type })
                Unify(chosen.Substitute(parameter), type, bindings, variables);
        }

        foreach (var (index, lambda) in lambdas)
        {
            var target = JavaTypes.Substitute(chosen.Substitute(ParameterAt(method, index, count)!), bindings);
            if (FunctionalMethod(target) is not { } functional || LambdaValue(lambda, target, scope) is not { } value)
                continue;
            var returns = functional.Substitute(functional.Member.ReturnType);
            Unify(returns, JavaTypes.AsReference(value), bindings, variables);
        }

        return bindings;
    }

    private void Unify(TypeReference parameter, TypeReference argument, Dictionary<string, TypeReference?> bindings, HashSet<string> variables)
    {
        switch (parameter)
        {
            case TypeVariableReference variable when variables.Contains(variable.Name):
                if (argument is not NullTypeReference && !bindings.ContainsKey(variable.Name))
                    bindings[variable.Name] = JavaTypes.AsReference(argument);
                break;
            case ArrayTypeReference parameterArray when argument is ArrayTypeReference argumentArray:
                Unify(parameterArray.ElementType, argumentArray.ElementType, bindings, variables);
                break;
            case ClassTypeReference { TypeArguments.Count: > 0 } generic when argument is ClassTypeReference argumentClass:
            {
                var match = model.Hierarchy(argumentClass).FirstOrDefault(e => e.Symbol.BinaryName == generic.BinaryName)?.Type;
                if (match is null || match.TypeArguments.Count != generic.TypeArguments.Count)
                    break;
                for (var i = 0; i < generic.TypeArguments.Count; i++)
                {
                    if (generic.TypeArguments[i].Type is { } expected && match.TypeArguments[i].Type is { } actual)
                        Unify(expected, actual, bindings, variables);
                }

                break;
            }
        }
    }

    // The value of a lambda with an expression body, typed with its parameters from the target.
    private TypeReference? LambdaValue(LambdaExpressionSyntax lambda, TypeReference target, Scope scope)
    {
        if (lambda.Body is not ExpressionSyntax body)
            return null;

        var lambdaScope = Scope.ForLambda(scope);
        var types = LambdaParameterTypes(lambda, target, scope);
        for (var i = 0; i < lambda.Parameters.Count; i++)
        {
            var parameter = lambda.Parameters[i];
            lambdaScope.Declare(new LocalSymbol(parameter.Name.Text, LocalKind.LambdaParameter, parameter.Name.Span, types[i], false));
        }

        var value = TypeOf(body, lambdaScope).Type;
        return value is PrimitiveTypeReference { Kind: PrimitiveKind.Void } ? null : value;
    }

    // Type variables nothing bound stand for their first bound, as the compiler erases them.
    private static TypeReference WithoutOpenVariables(TypeReference type, MethodSymbol method)
    {
        if (type is TypeVariableReference variable && method.TypeParameters.FirstOrDefault(p => p.Name == variable.Name) is { } parameter)
            return parameter.Bounds is [ClassTypeReference bound, ..] ? bound with { TypeArguments = [] } : JavaTypes.Object;
        return type;
    }

    private static bool IsObjectMethod(MethodSymbol method) => method.Name switch
    {
        "equals" => method.ParameterTypes is [ClassTypeReference { BinaryName: "java.lang.Object" }],
        "hashCode" or "toString" => method.ParameterTypes.Count == 0,
        _ => false,
    };

    private TypeReference? CreatedType(ObjectCreationExpressionSyntax creation, Scope scope)
    {
        if (creation.Outer is { } outer && creation.Type is ClassTypeSyntax { Qualifier: null } inner
            && ReceiverClass(TypeOf(outer, scope).Type, scope) is { } outerType
            && model.FindMemberType(outerType.BinaryName, inner.Name.Text) is { } nested)
        {
            return new ClassTypeReference(nested, []);
        }

        return model.ResolveType(creation.Type, scope);
    }

    private TypedExpression Creation(ObjectCreationExpressionSyntax creation, Scope scope)
    {
        if (CreatedType(creation, scope) is not ClassTypeReference type)
            return TypedExpression.Unknown;

        if (creation.Body is { } body)
        {
            var name = model.DeclareLocalType(creation, "", binary => SourceSymbols.BuildAnonymous(model, binary, body, type, scope));
            return new TypedExpression(type, false, ExpressionKind.Value, name);
        }

        return new TypedExpression(type, model.FindClass(type.BinaryName) is not null);
    }

    private TypedExpression ArrayCreation(ArrayCreationExpressionSyntax creation, Scope scope)
    {
        var type = model.ResolveType(creation.ElementType, scope);
        if (type is null)
            return TypedExpression.Unknown;

        for (var i = 0; i < creation.Dimensions.Count; i++)
            type = new ArrayTypeReference(type);
        return new TypedExpression(type, JavaTypes.IsConcrete(type));
    }

    private TypedExpression Conditional(ConditionalExpressionSyntax conditional, Scope scope)
    {
        var whenTrue = TypeOf(conditional.WhenTrue, scope);
        var whenFalse = TypeOf(conditional.WhenFalse, scope);
        if (whenTrue.Type is NullTypeReference)
            return whenFalse with { Kind = ExpressionKind.Value, Symbol = null };
        if (whenFalse.Type is NullTypeReference || whenTrue.Type is null || whenFalse.Type is null)
            return whenTrue with { Kind = ExpressionKind.Value, Symbol = null, IsCertain = whenTrue.IsCertain && whenFalse.Type is NullTypeReference };

        if (whenTrue.Type is PrimitiveTypeReference && whenFalse.Type is PrimitiveTypeReference && JavaTypes.Promote(whenTrue.Type, whenFalse.Type) is { } promoted)
            return new TypedExpression(promoted, whenTrue.IsCertain && whenFalse.IsCertain);

        var same = JavaTypes.Erase(whenTrue.Type) == JavaTypes.Erase(whenFalse.Type);
        return new TypedExpression(whenTrue.Type, same && whenTrue.IsCertain && whenFalse.IsCertain);
    }

    private TypedExpression Binary(BinaryExpressionSyntax binary, Scope scope)
    {
        switch (binary.Operator)
        {
            case BinaryOperator.LessThan or BinaryOperator.GreaterThan or BinaryOperator.LessThanOrEqual or BinaryOperator.GreaterThanOrEqual
                or BinaryOperator.Equals or BinaryOperator.NotEquals or BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr:
                return TypedExpression.Certain(PrimitiveTypeReference.Boolean);
            case BinaryOperator.LeftShift or BinaryOperator.RightShift or BinaryOperator.UnsignedRightShift:
            {
                var left = TypeOf(binary.Left, scope);
                return JavaTypes.Promote(left.Type) is { } shifted ? new TypedExpression(shifted, left.IsCertain) : TypedExpression.Unknown;
            }
        }

        var leftOperand = TypeOf(binary.Left, scope);
        var rightOperand = TypeOf(binary.Right, scope);
        if (binary.Operator == BinaryOperator.Add
            && (leftOperand is { IsCertain: true, Type: ClassTypeReference { BinaryName: "java.lang.String" } }
                || rightOperand is { IsCertain: true, Type: ClassTypeReference { BinaryName: "java.lang.String" } }))
        {
            return TypedExpression.Certain(JavaTypes.String);
        }

        if (binary.Operator is BinaryOperator.BitwiseAnd or BinaryOperator.BitwiseOr or BinaryOperator.ExclusiveOr
            && JavaTypes.IsBoolean(leftOperand.Type) && JavaTypes.IsBoolean(rightOperand.Type))
        {
            return new TypedExpression(PrimitiveTypeReference.Boolean, leftOperand.IsCertain && rightOperand.IsCertain);
        }

        return JavaTypes.Promote(leftOperand.Type, rightOperand.Type) is { } promoted
            ? new TypedExpression(promoted, leftOperand.IsCertain && rightOperand.IsCertain)
            : TypedExpression.Unknown;
    }

    private TypedExpression Unary(UnaryExpressionSyntax unary, Scope scope)
    {
        if (unary.Operator == UnaryOperator.LogicalNot)
            return TypedExpression.Certain(PrimitiveTypeReference.Boolean);

        var operand = TypeOf(unary.Operand, scope);
        if (unary.Operator is UnaryOperator.PreIncrement or UnaryOperator.PreDecrement or UnaryOperator.PostIncrement or UnaryOperator.PostDecrement)
            return operand with { Kind = ExpressionKind.Value, Symbol = null };
        return JavaTypes.Promote(operand.Type) is { } promoted ? new TypedExpression(promoted, operand.IsCertain) : TypedExpression.Unknown;
    }

    private TypedExpression This(ExpressionSyntax? qualifier, Scope scope)
    {
        if (qualifier is null)
        {
            return scope.EnclosingType() is { Type: { } type }
                ? new TypedExpression(SemanticModel.SelfType(type), true, ExpressionKind.Value, type.BinaryName)
                : TypedExpression.Unknown;
        }

        var outer = TypeOf(qualifier, scope);
        return outer is { Kind: ExpressionKind.Type, Type: ClassTypeReference outerType } && model.FindClass(outerType.BinaryName) is { } symbol
            ? new TypedExpression(SemanticModel.SelfType(symbol), outer.IsCertain, ExpressionKind.Value, symbol.BinaryName)
            : TypedExpression.Unknown;
    }

    private TypedExpression Super(ExpressionSyntax? qualifier, Scope scope)
    {
        var self = This(qualifier, scope);
        if (self.Type is not ClassTypeReference type || model.FindClass(type.BinaryName) is not { } symbol)
            return TypedExpression.Unknown;

        // Outer.super names a superinterface when Outer is one; otherwise it is the superclass.
        if (qualifier is not null && symbol.Kind == ClassKind.Interface)
            return new TypedExpression(type, self.IsCertain);
        return symbol.SuperClass is { } superClass ? new TypedExpression(superClass, self.IsCertain) : TypedExpression.Unknown;
    }

    private TypedExpression SwitchValue(SwitchExpressionSyntax switchExpression, Scope scope)
    {
        foreach (var arm in switchExpression.Cases)
        {
            if (arm.ArrowBody is ExpressionSyntax value && TypeOf(value, scope) is { Type: not null and not NullTypeReference } typed)
                return typed with { IsCertain = false, Kind = ExpressionKind.Value, Symbol = null };
        }

        return TypedExpression.Unknown;
    }
}
