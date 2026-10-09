using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Syntax;
using SyntaxModifiers = Runesmith.Languages.Java.Syntax.JavaModifiers;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>Builds the scopes of a file: the one at a position, from the nodes around it, and the ones a walk over the file enters.</summary>
/// <remarks>Locals are declared where Java declares them, so a name is in scope from its declaration to the end of its block, and pattern
/// bindings where their condition is true. Bindings that only flow scoping would bring in are declared with an uncertain type.</remarks>
internal sealed class ScopeBuilder(ExpressionTyper typer)
{
    private SemanticModel Model => typer.Model;

    /// <summary>Gets the scope at an offset and the nodes that contain it, from the root down.</summary>
    public Scope ScopeAt(int offset, out IReadOnlyList<SyntaxNode> path)
    {
        path = Model.Tree.GetPath(offset);
        var scope = Scope.ForFile(Model.FileScope);
        for (var i = 0; i < path.Count; i++)
        {
            var node = path[i];
            var next = i + 1 < path.Count ? path[i + 1] : null;
            scope = Enter(node, next, scope, path, i, offset);
        }

        return scope;
    }

    private Scope Enter(SyntaxNode node, SyntaxNode? next, Scope scope, IReadOnlyList<SyntaxNode> path, int index, int offset)
    {
        switch (node)
        {
            case CompilationUnitSyntax when Model.File.ImplicitClass is { } implicitClass && next is MemberDeclarationSyntax:
                return SourceSymbols.TypeScope(Model, implicitClass);
            case TypeDeclarationSyntax type:
                return next == type.Body ? EnterType(type, scope) : EnterHeader(type, scope);
            case ObjectCreationExpressionSyntax { Body: { } body } creation when next == body:
                return EnterAnonymous(creation, scope);
            case EnumConstantSyntax { Body: { } constantBody } constant when next == constantBody:
                return EnterEnumConstant(constant, scope);
            case MethodDeclarationSyntax method:
                return EnterMethod(method, scope, declareParameters: next == method.Body);
            case ConstructorDeclarationSyntax constructor:
                return EnterConstructor(constructor, scope, declareParameters: next == constructor.Body);
            case CompactConstructorDeclarationSyntax:
                return EnterCompactConstructor(scope);
            case InitializerDeclarationSyntax initializer:
                return Scope.ForMethod(scope.File, scope, initializer.IsStatic);
            case FieldDeclarationSyntax field:
                return Scope.ForMethod(scope.File, scope, field.Modifiers.Has(SyntaxModifiers.Static));
            case BlockSyntax block:
            {
                var inner = Scope.ForBlock(scope);
                DeclareBefore(block.Statements, next, offset, inner);
                return inner;
            }

            case SwitchStatementSyntax or SwitchExpressionSyntax when next is SwitchCaseSyntax switchCase:
            {
                var inner = Scope.ForBlock(scope);
                var cases = node is SwitchStatementSyntax statement ? statement.Cases : ((SwitchExpressionSyntax)node).Cases;
                foreach (var earlier in cases)
                {
                    if (earlier == switchCase)
                        break;
                    if (!earlier.IsArrow)
                        DeclareBefore(earlier.Statements, null, int.MaxValue, inner);
                }

                return inner;
            }

            case SwitchCaseSyntax switchCase:
            {
                var inner = Scope.ForBlock(scope);
                var selector = path[index - 1] switch
                {
                    SwitchStatementSyntax statement => statement.Selector,
                    SwitchExpressionSyntax expression => expression.Selector,
                    _ => null,
                };
                if (!switchCase.Labels.Contains(next!))
                    DeclareCaseBindings(switchCase, selector, scope, inner);
                DeclareBefore(switchCase.Statements, next, offset, inner);
                return inner;
            }

            case LocalVariableDeclarationSyntax local:
                foreach (var variable in local.Variables)
                {
                    if (variable == next || variable.End > offset)
                        break;
                    DeclareVariable(local, variable, scope, LocalKind.Local);
                }

                return scope;
            case ForStatementSyntax loop:
            {
                var inner = Scope.ForBlock(scope);
                foreach (var initializer in loop.Initializers)
                {
                    if (initializer == next)
                        break;
                    DeclareStatement(initializer, inner);
                }

                if (next == loop.Body && loop.Condition is { } condition)
                    DeclareBindings(condition, inner, whenTrue: true, certain: true);
                return inner;
            }

            case ForEachStatementSyntax forEach when next == forEach.Body:
            {
                var inner = Scope.ForBlock(scope);
                DeclareForEach(forEach, inner);
                return inner;
            }

            case TryStatementSyntax tryStatement:
            {
                var inner = Scope.ForBlock(scope);
                foreach (var resource in tryStatement.Resources)
                {
                    if (resource == next || resource.Start >= offset)
                        break;
                    if (resource is LocalVariableDeclarationSyntax declaration)
                        DeclareLocals(declaration, inner, LocalKind.Resource);
                }

                return inner;
            }

            case CatchClauseSyntax catchClause when next == catchClause.Body:
            {
                var inner = Scope.ForBlock(scope);
                DeclareCatch(catchClause, inner);
                return inner;
            }

            case LambdaExpressionSyntax lambda when next == lambda.Body:
                return EnterLambda(lambda, scope, TargetType(path, index, scope));
            case IfStatementSyntax ifStatement when next == ifStatement.Then || next == ifStatement.Else:
                return WithBindings(ifStatement.Condition, scope, next == ifStatement.Then);
            case WhileStatementSyntax whileStatement when next == whileStatement.Body:
                return WithBindings(whileStatement.Condition, scope, whenTrue: true);
            case ConditionalExpressionSyntax conditional when next == conditional.WhenTrue || next == conditional.WhenFalse:
                return WithBindings(conditional.Condition, scope, next == conditional.WhenTrue);
            case BinaryExpressionSyntax { Operator: BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr } binary when next == binary.Right:
                return WithBindings(binary.Left, scope, binary.Operator == BinaryOperator.LogicalAnd);
            default:
                return scope;
        }
    }

    /// <summary>Gets the scope inside a type's body: its members, inherited ones, and those of the types around it.</summary>
    public Scope EnterType(TypeDeclarationSyntax type, Scope scope)
    {
        if (Model.File.TypeOf(type) is { } declared)
            return SourceSymbols.TypeScope(Model, declared);

        var name = DeclareLocalType(type, scope);
        return Scope.ForType(scope.File, scope, Model.FindClass(name), name, () => !Model.IsFullyKnown(name));
    }

    /// <summary>Gets the scope of a type's header, where its type parameters are, but not its members.</summary>
    public Scope EnterHeader(TypeDeclarationSyntax type, Scope scope)
    {
        var parent = Model.File.TypeOf(type)?.Outer is { } outer ? SourceSymbols.TypeScope(Model, outer) : scope;
        var header = Scope.ForMethod(scope.File, parent, isStatic: false);
        Model.DeclareTypeParameters(type.TypeParameters, header);
        return header;
    }

    public Scope EnterAnonymous(ObjectCreationExpressionSyntax creation, Scope scope)
    {
        var created = typer.TypeOf(creation, scope);
        if (created is not { Type: ClassTypeReference baseType, Symbol: string name })
            return Scope.ForType(scope.File, scope, null, "?", () => true);
        return Scope.ForType(scope.File, scope, Model.FindClass(name), name, () => !Model.IsFullyKnown(baseType.BinaryName));
    }

    public Scope EnterEnumConstant(EnumConstantSyntax constant, Scope scope)
    {
        if (scope.EnclosingType()?.Type is not { } enumType || constant.Body is not { } body)
            return Scope.ForType(scope.File, scope, null, "?", () => true);

        var self = new ClassTypeReference(enumType.BinaryName, []);
        var name = Model.DeclareLocalType(constant, "", binary => SourceSymbols.BuildAnonymous(Model, binary, body, self, scope));
        return Scope.ForType(scope.File, scope, Model.FindClass(name), name, () => !Model.IsFullyKnown(enumType.BinaryName));
    }

    public Scope EnterMethod(MethodDeclarationSyntax method, Scope scope, bool declareParameters)
    {
        var inner = Scope.ForMethod(scope.File, scope, method.Modifiers.Has(SyntaxModifiers.Static));
        Model.DeclareTypeParameters(method.TypeParameters, inner);
        if (declareParameters)
            DeclareParameters(method.Parameters, inner);
        return inner;
    }

    public Scope EnterConstructor(ConstructorDeclarationSyntax constructor, Scope scope, bool declareParameters)
    {
        var inner = Scope.ForMethod(scope.File, scope, isStatic: false);
        Model.DeclareTypeParameters(constructor.TypeParameters, inner);
        if (declareParameters)
            DeclareParameters(constructor.Parameters, inner);
        return inner;
    }

    /// <summary>Gets the scope of a record's compact constructor, whose parameters are the record's components.</summary>
    public Scope EnterCompactConstructor(Scope scope)
    {
        var inner = Scope.ForMethod(scope.File, scope, isStatic: false);
        if (scope.EnclosingType()?.Type is { } record && Model.LocalDeclaration(record.BinaryName) is null
            && Model.Project.FindSourceType(record.BinaryName)?.Declaration is RecordDeclarationSyntax declaration)
        {
            for (var i = 0; i < declaration.Header.Components.Count && i < record.RecordComponents.Count; i++)
            {
                var component = declaration.Header.Components[i];
                inner.Declare(new LocalSymbol(component.Name.Text, LocalKind.Parameter, component.Name.Span, record.RecordComponents[i].Type, true));
            }
        }

        return inner;
    }

    /// <summary>Gets the scope of a lambda's body, with its parameters typed from the target type when it is known.</summary>
    public Scope EnterLambda(LambdaExpressionSyntax lambda, Scope scope, TypeReference? target)
    {
        var inner = Scope.ForLambda(scope);
        var types = target is null ? null : typer.LambdaParameterTypes(lambda, target, scope);
        for (var i = 0; i < lambda.Parameters.Count; i++)
        {
            var parameter = lambda.Parameters[i];
            if (parameter.Name.IsMissing)
                continue;
            var written = parameter.Type is { } type and not VarTypeSyntax ? Model.ResolveType(type, scope) : null;
            inner.Declare(new LocalSymbol(parameter.Name.Text, LocalKind.LambdaParameter, parameter.Name.Span, written ?? types?[i], written is not null));
        }

        return inner;
    }

    /// <summary>Declares what a statement brings into the rest of its block: locals, local classes, and pattern bindings of an <c>if</c>
    /// without <c>else</c>, whose scope depends on whether its branch completes.</summary>
    public void DeclareStatement(StatementSyntax statement, Scope scope)
    {
        switch (statement)
        {
            case LocalVariableDeclarationSyntax local:
                DeclareLocals(local, scope, LocalKind.Local);
                break;
            case LocalTypeDeclarationStatementSyntax localType:
                DeclareLocalType(localType.Declaration, scope);
                break;
            case IfStatementSyntax { Else: null } ifStatement:
                DeclareBindings(ifStatement.Condition, scope, whenTrue: false, certain: false);
                break;
            case ExpressionStatementSyntax:
                break;
        }
    }

    public void DeclareLocals(LocalVariableDeclarationSyntax local, Scope scope, LocalKind kind)
    {
        foreach (var variable in local.Variables)
            DeclareVariable(local, variable, scope, kind);
    }

    /// <summary>Registers a local class and makes its name visible in a scope; returns its binary name.</summary>
    public string DeclareLocalType(TypeDeclarationSyntax declaration, Scope scope)
    {
        var name = Model.DeclareLocalType(declaration, declaration.Name.Text, binary => SourceSymbols.BuildLocal(Model, binary, declaration, scope));
        if (!declaration.Name.IsMissing)
            scope.DeclareLocalType(declaration.Name.Text, name);
        return name;
    }

    public void DeclareParameters(ParameterListSyntax parameters, Scope scope)
    {
        foreach (var parameter in parameters.Parameters)
        {
            if (parameter.IsReceiver || parameter.Name.IsMissing)
                continue;

            var type = SemanticModel.WithDimensions(parameter.Type is null ? null : Model.ResolveType(parameter.Type, scope), parameter.Dimensions.Count);
            if (parameter.IsVarargs && type is not null)
                type = new ArrayTypeReference(type);
            scope.Declare(new LocalSymbol(parameter.Name.Text, LocalKind.Parameter, parameter.Name.Span, type, true));
        }
    }

    public void DeclareForEach(ForEachStatementSyntax forEach, Scope scope)
    {
        if (forEach.Name.IsMissing)
            return;

        if (forEach.Type is VarTypeSyntax)
        {
            scope.Declare(new LocalSymbol(forEach.Name.Text, LocalKind.Local, forEach.Name.Span, () => ElementOf(typer.TypeOf(forEach.Expression, scope))));
            return;
        }

        scope.Declare(new LocalSymbol(forEach.Name.Text, LocalKind.Local, forEach.Name.Span, Model.ResolveType(forEach.Type, scope), true));
    }

    public void DeclareCatch(CatchClauseSyntax catchClause, Scope scope)
    {
        if (!catchClause.Name.IsMissing)
        {
            scope.Declare(new LocalSymbol(catchClause.Name.Text, LocalKind.CatchParameter, catchClause.Name.Span, Model.ResolveType(catchClause.Type, scope),
                catchClause.Type is not UnionTypeSyntax));
        }
    }

    /// <summary>Declares the pattern bindings of a switch case's labels.</summary>
    public void DeclareCaseBindings(SwitchCaseSyntax switchCase, ExpressionSyntax? selector, Scope selectorScope, Scope scope)
    {
        foreach (var label in switchCase.Labels)
        {
            if (label is PatternSyntax pattern)
                DeclarePattern(pattern, scope, () => selector is null ? TypedExpression.Unknown : typer.TypeOf(selector, selectorScope), certain: true);
        }
    }

    /// <summary>Declares the pattern bindings a condition introduces when it is true, or when it is false.</summary>
    public void DeclareBindings(ExpressionSyntax condition, Scope scope, bool whenTrue, bool certain)
    {
        switch (condition)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                DeclareBindings(parenthesized.Expression, scope, whenTrue, certain);
                break;
            case UnaryExpressionSyntax { Operator: UnaryOperator.LogicalNot } negation:
                DeclareBindings(negation.Operand, scope, !whenTrue, certain);
                break;
            case BinaryExpressionSyntax { Operator: BinaryOperator.LogicalAnd } both when whenTrue:
                DeclareBindings(both.Left, scope, whenTrue, certain);
                DeclareBindings(both.Right, scope, whenTrue, certain);
                break;
            case BinaryExpressionSyntax { Operator: BinaryOperator.LogicalOr } either when !whenTrue:
                DeclareBindings(either.Left, scope, whenTrue, certain);
                DeclareBindings(either.Right, scope, whenTrue, certain);
                break;
            case InstanceofExpressionSyntax { Pattern: { } pattern } test when whenTrue:
            {
                var outer = scope;
                DeclarePattern(pattern, scope, () => typer.TypeOf(test.Expression, outer), certain);
                break;
            }
        }
    }

    /// <summary>Gets the type a lambda at a position of a path must have, from what it is passed or assigned to, or null.</summary>
    public TypeReference? TargetType(IReadOnlyList<SyntaxNode> path, int index, Scope scope)
    {
        if (index < 1)
            return null;

        var node = path[index];
        switch (path[index - 1])
        {
            case ArgumentListSyntax arguments when index >= 2:
            {
                var position = IndexOf(arguments.Arguments, node);
                return position < 0 ? null : typer.ParameterTypeFor(path[index - 2], position, scope);
            }

            case VariableDeclaratorSyntax variable when variable.Initializer == node && index >= 2:
            {
                var declared = path[index - 2] switch
                {
                    LocalVariableDeclarationSyntax local when local.Type is not VarTypeSyntax => local.Type,
                    FieldDeclarationSyntax field => field.Type,
                    _ => null,
                };
                return declared is null ? null : SemanticModel.WithDimensions(Model.ResolveType(declared, scope), variable.Dimensions.Count);
            }

            case AssignmentExpressionSyntax assignment when assignment.Right == node:
                return typer.TypeOf(assignment.Left, scope).Type;
            case ReturnStatementSyntax:
                for (var i = index - 2; i >= 0; i--)
                {
                    if (path[i] is LambdaExpressionSyntax)
                        return null;
                    if (path[i] is MethodDeclarationSyntax method)
                        return Model.ResolveType(method.ReturnType, scope);
                }

                return null;
            case CastExpressionSyntax cast:
                return Model.ResolveType(cast.Type, scope);
            case ParenthesizedExpressionSyntax:
                return TargetType(path, index - 1, scope);
            case ConditionalExpressionSyntax conditional when conditional.Condition != node:
                return TargetType(path, index - 1, scope);
            default:
                return null;
        }
    }

    private Scope WithBindings(ExpressionSyntax condition, Scope scope, bool whenTrue)
    {
        var inner = Scope.ForBlock(scope);
        DeclareBindings(condition, inner, whenTrue, certain: true);
        return inner;
    }

    private void DeclareBefore(SyntaxList<StatementSyntax> statements, SyntaxNode? next, int offset, Scope scope)
    {
        foreach (var statement in statements)
        {
            if (statement == next || statement.Start >= offset)
                break;
            DeclareStatement(statement, scope);
        }
    }

    private void DeclareVariable(LocalVariableDeclarationSyntax local, VariableDeclaratorSyntax variable, Scope scope, LocalKind kind)
    {
        if (variable.Name.IsMissing)
            return;

        if (local.Type is VarTypeSyntax)
        {
            var initializer = variable.Initializer;
            scope.Declare(new LocalSymbol(variable.Name.Text, kind, variable.Name.Span,
                () => initializer is null or ArrayInitializerExpressionSyntax ? TypedExpression.Unknown : typer.TypeOf(initializer, scope)));
            return;
        }

        var type = SemanticModel.WithDimensions(Model.ResolveType(local.Type, scope), variable.Dimensions.Count);
        scope.Declare(new LocalSymbol(variable.Name.Text, kind, variable.Name.Span, type, true));
    }

    private void DeclarePattern(PatternSyntax pattern, Scope scope, Func<TypedExpression> matched, bool certain)
    {
        switch (pattern)
        {
            case TypePatternSyntax typePattern when !typePattern.Name.IsMissing && typePattern.Name.Text != "_":
                if (typePattern.Type is VarTypeSyntax)
                {
                    scope.Declare(new LocalSymbol(typePattern.Name.Text, LocalKind.Pattern, typePattern.Name.Span, () => matched() with { IsCertain = false }));
                }
                else
                {
                    scope.Declare(new LocalSymbol(typePattern.Name.Text, LocalKind.Pattern, typePattern.Name.Span, Model.ResolveType(typePattern.Type, scope),
                        certain));
                }

                break;
            case RecordPatternSyntax record:
            {
                var components = Model.ResolveType(record.Type, scope) is ClassTypeReference recordType && Model.FindClass(recordType.BinaryName) is { } symbol
                    ? symbol.RecordComponents
                    : [];
                for (var i = 0; i < record.Subpatterns.Count; i++)
                {
                    var component = i < components.Count ? components[i].Type : null;
                    DeclarePattern(record.Subpatterns[i], scope, () => new TypedExpression(component, false), certain);
                }

                break;
            }
        }
    }

    private TypedExpression ElementOf(TypedExpression iterable)
    {
        switch (iterable.Type)
        {
            case ArrayTypeReference array:
                return new TypedExpression(array.ElementType, iterable.IsCertain);
            case ClassTypeReference type:
            {
                var entry = Model.Hierarchy(type).FirstOrDefault(e => e.Symbol.BinaryName == JavaTypes.Iterable.BinaryName);
                if (entry?.Map is not { } map || !map.TryGetValue(entry.Symbol.TypeParameters[0].Name, out var element) || element is null)
                    return new TypedExpression(JavaTypes.Object, false);
                return new TypedExpression(element, iterable.IsCertain && entry.IsExact && JavaTypes.IsConcrete(element));
            }

            default:
                return TypedExpression.Unknown;
        }
    }

    private static int IndexOf(SyntaxList<ExpressionSyntax> list, SyntaxNode node)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == node)
                return i;
        }

        return -1;
    }
}
