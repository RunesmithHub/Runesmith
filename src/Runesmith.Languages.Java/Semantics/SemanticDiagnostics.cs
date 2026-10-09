using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Syntax;
using SyntaxModifiers = Runesmith.Languages.Java.Syntax.JavaModifiers;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>A semantic problem the analyzer is sure of.</summary>
internal sealed record SemanticProblem(SourceSpan Span, string Code, string Message);

/// <summary>Finds the semantic problems of a file that are certain: imports that name nothing, type names that resolve to nothing in a scope
/// whose names are all known, and members missing from a type whose members are all known. Anything less certain is left alone.</summary>
internal sealed class SemanticDiagnostics
{
    /// <summary>The code of imports that name nothing.</summary>
    public const string ImportCode = "JAVA-IMPORT";

    /// <summary>The code of type names that resolve to nothing.</summary>
    public const string TypeCode = "JAVA-TYPE";

    /// <summary>The code of members a type does not have.</summary>
    public const string MemberCode = "JAVA-MEMBER";

    private readonly SemanticModel model;
    private readonly ExpressionTyper typer;
    private readonly ScopeBuilder scopes;
    private readonly List<SemanticProblem> problems = [];
    private readonly CancellationToken cancellationToken;
    private readonly bool checkNames;

    private SemanticDiagnostics(SemanticModel model, CancellationToken cancellationToken)
    {
        this.model = model;
        this.cancellationToken = cancellationToken;
        typer = new ExpressionTyper(model);
        scopes = new ScopeBuilder(typer);
        checkNames = !model.Project.Model.MayMissTypes;
    }

    /// <summary>Finds the problems of a file.</summary>
    public static IReadOnlyList<SemanticProblem> Find(SemanticModel model, CancellationToken cancellationToken)
    {
        if (!model.Project.HasJdk || model.Tree.Root.Module is not null)
            return [];

        var walker = new SemanticDiagnostics(model, cancellationToken);
        foreach (var problem in model.FileScope.Problems)
            walker.problems.Add(new SemanticProblem(problem.Span, ImportCode, problem.Message));
        walker.VisitRoot(model.Tree.Root);
        return walker.problems;
    }

    private void VisitRoot(CompilationUnitSyntax root)
    {
        var fileScope = Scope.ForFile(model.FileScope);
        var implicitScope = model.File.ImplicitClass is { } implicitClass ? SourceSymbols.TypeScope(model, implicitClass) : fileScope;
        foreach (var member in root.Members)
            VisitMember(member, implicitScope);
    }

    private void VisitMember(MemberDeclarationSyntax member, Scope scope)
    {
        cancellationToken.ThrowIfCancellationRequested();
        switch (member)
        {
            case TypeDeclarationSyntax type:
                VisitType(type, scope);
                break;
            case MethodDeclarationSyntax method:
            {
                var inner = scopes.EnterMethod(method, scope, declareParameters: true);
                CheckTypeParameters(method.TypeParameters, inner);
                CheckType(method.ReturnType, inner);
                CheckParameters(method.Parameters, inner);
                foreach (var thrown in method.Throws)
                    CheckType(thrown, inner);
                if (method.Body is { } body)
                    VisitStatement(body, inner);
                break;
            }

            case ConstructorDeclarationSyntax constructor:
            {
                var inner = scopes.EnterConstructor(constructor, scope, declareParameters: true);
                CheckTypeParameters(constructor.TypeParameters, inner);
                CheckParameters(constructor.Parameters, inner);
                foreach (var thrown in constructor.Throws)
                    CheckType(thrown, inner);
                VisitStatement(constructor.Body, inner);
                break;
            }

            case CompactConstructorDeclarationSyntax compact:
                VisitStatement(compact.Body, scopes.EnterCompactConstructor(scope));
                break;
            case InitializerDeclarationSyntax initializer:
                VisitStatement(initializer.Body, Scope.ForMethod(scope.File, scope, initializer.IsStatic));
                break;
            case FieldDeclarationSyntax field:
            {
                var inner = Scope.ForMethod(scope.File, scope, field.Modifiers.Has(SyntaxModifiers.Static));
                CheckType(field.Type, inner);
                foreach (var variable in field.Variables)
                {
                    if (variable.Initializer is { } initializer)
                        VisitExpression(initializer, inner);
                }

                break;
            }
        }
    }

    private void VisitType(TypeDeclarationSyntax type, Scope scope)
    {
        var header = scopes.EnterHeader(type, scope);
        CheckTypeParameters(type.TypeParameters, header);
        IEnumerable<TypeSyntax> supertypes = type switch
        {
            ClassDeclarationSyntax c => [.. (c.Extends is null ? [] : new[] { c.Extends }), .. c.Implements, .. c.Permits],
            InterfaceDeclarationSyntax i => [.. i.Extends, .. i.Permits],
            EnumDeclarationSyntax e => e.Implements,
            RecordDeclarationSyntax r => r.Implements,
            _ => [],
        };
        foreach (var supertype in supertypes)
            CheckType(supertype, header);

        var body = scopes.EnterType(type, scope);
        if (type is RecordDeclarationSyntax record)
        {
            foreach (var component in record.Header.Components)
                CheckType(component.Type, body);
        }

        foreach (var constant in type.Body.EnumConstants)
        {
            foreach (var argument in (IEnumerable<ExpressionSyntax>?)constant.Arguments?.Arguments ?? [])
                VisitExpression(argument, body);
            if (constant.Body is { } constantBody)
            {
                var anonymous = scopes.EnterEnumConstant(constant, body);
                foreach (var member in constantBody.Members)
                    VisitMember(member, anonymous);
            }
        }

        foreach (var member in type.Body.Members)
            VisitMember(member, body);
    }

    private void VisitStatement(StatementSyntax statement, Scope scope)
    {
        switch (statement)
        {
            case BlockSyntax block:
            {
                var inner = Scope.ForBlock(scope);
                foreach (var child in block.Statements)
                {
                    VisitStatement(child, inner);
                    if (child is not LocalVariableDeclarationSyntax and not LocalTypeDeclarationStatementSyntax)
                        scopes.DeclareStatement(child, inner);
                }

                break;
            }

            case LocalVariableDeclarationSyntax local:
                CheckType(local.Type, scope);
                foreach (var variable in local.Variables)
                {
                    if (variable.Initializer is { } initializer)
                        VisitExpression(initializer, scope);
                }

                scopes.DeclareLocals(local, scope, LocalKind.Local);
                break;
            case LocalTypeDeclarationStatementSyntax localType:
                scopes.DeclareLocalType(localType.Declaration, scope);
                VisitType(localType.Declaration, scope);
                break;
            case ExpressionStatementSyntax expression:
                VisitExpression(expression.Expression, scope);
                break;
            case IfStatementSyntax ifStatement:
                VisitExpression(ifStatement.Condition, scope);
                VisitStatement(ifStatement.Then, WithBindings(ifStatement.Condition, scope, whenTrue: true));
                if (ifStatement.Else is { } otherwise)
                    VisitStatement(otherwise, WithBindings(ifStatement.Condition, scope, whenTrue: false));
                break;
            case WhileStatementSyntax whileStatement:
                VisitExpression(whileStatement.Condition, scope);
                VisitStatement(whileStatement.Body, WithBindings(whileStatement.Condition, scope, whenTrue: true));
                break;
            case DoStatementSyntax doStatement:
                VisitStatement(doStatement.Body, scope);
                VisitExpression(doStatement.Condition, scope);
                break;
            case ForStatementSyntax forStatement:
            {
                var inner = Scope.ForBlock(scope);
                foreach (var initializer in forStatement.Initializers)
                    VisitStatement(initializer, inner);
                if (forStatement.Condition is { } condition)
                    VisitExpression(condition, inner);
                foreach (var update in forStatement.Updates)
                    VisitExpression(update, inner);
                VisitStatement(forStatement.Body, forStatement.Condition is { } test ? WithBindings(test, inner, whenTrue: true) : inner);
                break;
            }

            case ForEachStatementSyntax forEach:
            {
                VisitExpression(forEach.Expression, scope);
                CheckType(forEach.Type, scope);
                var inner = Scope.ForBlock(scope);
                scopes.DeclareForEach(forEach, inner);
                VisitStatement(forEach.Body, inner);
                break;
            }

            case ReturnStatementSyntax { Expression: { } value }:
                VisitExpression(value, scope);
                break;
            case ThrowStatementSyntax throwStatement:
                VisitExpression(throwStatement.Expression, scope);
                break;
            case YieldStatementSyntax yieldStatement:
                VisitExpression(yieldStatement.Expression, scope);
                break;
            case AssertStatementSyntax assertStatement:
                VisitExpression(assertStatement.Condition, scope);
                if (assertStatement.Message is { } message)
                    VisitExpression(message, scope);
                break;
            case SynchronizedStatementSyntax synchronizedStatement:
                VisitExpression(synchronizedStatement.Lock, scope);
                VisitStatement(synchronizedStatement.Body, scope);
                break;
            case LabeledStatementSyntax labeled:
                VisitStatement(labeled.Statement, scope);
                break;
            case SwitchStatementSyntax switchStatement:
                VisitSwitch(switchStatement.Selector, switchStatement.Cases, scope);
                break;
            case TryStatementSyntax tryStatement:
            {
                var inner = Scope.ForBlock(scope);
                foreach (var resource in tryStatement.Resources)
                {
                    if (resource is LocalVariableDeclarationSyntax declaration)
                        VisitStatement(declaration, inner);
                    else if (resource is ExpressionSyntax expression)
                        VisitExpression(expression, inner);
                }

                VisitStatement(tryStatement.Body, inner);
                foreach (var catchClause in tryStatement.Catches)
                {
                    CheckType(catchClause.Type, scope);
                    var catchScope = Scope.ForBlock(scope);
                    scopes.DeclareCatch(catchClause, catchScope);
                    VisitStatement(catchClause.Body, catchScope);
                }

                if (tryStatement.Finally is { } finallyBlock)
                    VisitStatement(finallyBlock, scope);
                break;
            }

            case ExplicitConstructorInvocationSyntax invocation:
                if (invocation.Qualifier is { } qualifier)
                    VisitExpression(qualifier, scope);
                foreach (var argument in invocation.Arguments.Arguments)
                    VisitExpression(argument, scope);
                break;
        }
    }

    private void VisitSwitch(ExpressionSyntax selector, SyntaxList<SwitchCaseSyntax> cases, Scope scope)
    {
        VisitExpression(selector, scope);
        var inner = Scope.ForBlock(scope);
        foreach (var switchCase in cases)
        {
            var caseScope = Scope.ForBlock(inner);
            foreach (var label in switchCase.Labels)
                CheckPattern(label, scope);
            scopes.DeclareCaseBindings(switchCase, selector, scope, caseScope);
            if (switchCase.Guard is { } guard)
                VisitExpression(guard, caseScope);

            foreach (var child in switchCase.Statements)
            {
                VisitStatement(child, caseScope);
                if (child is not LocalVariableDeclarationSyntax and not LocalTypeDeclarationStatementSyntax)
                    scopes.DeclareStatement(child, caseScope);
            }

            switch (switchCase.ArrowBody)
            {
                case ExpressionSyntax expression:
                    VisitExpression(expression, caseScope);
                    break;
                case StatementSyntax body:
                    VisitStatement(body, caseScope);
                    break;
            }
        }
    }

    private void VisitExpression(ExpressionSyntax expression, Scope scope)
    {
        switch (expression)
        {
            case LambdaExpressionSyntax lambda:
            {
                foreach (var parameter in lambda.Parameters)
                {
                    if (parameter.Type is { } type)
                        CheckType(type, scope);
                }

                var inner = scopes.EnterLambda(lambda, scope, target: null);
                if (lambda.Body is ExpressionSyntax value)
                    VisitExpression(value, inner);
                else if (lambda.Body is StatementSyntax block)
                    VisitStatement(block, inner);
                break;
            }

            case ObjectCreationExpressionSyntax creation:
            {
                if (creation.Outer is { } outer)
                    VisitExpression(outer, scope);
                else
                    CheckType(creation.Type, scope);
                foreach (var argument in creation.Arguments.Arguments)
                    VisitExpression(argument, scope);
                if (creation.Body is { } body)
                {
                    var anonymous = scopes.EnterAnonymous(creation, scope);
                    foreach (var member in body.Members)
                        VisitMember(member, anonymous);
                }

                break;
            }

            case MethodInvocationExpressionSyntax call:
                if (call.Target is { } target)
                    VisitExpression(target, scope);
                foreach (var argument in call.Arguments.Arguments)
                    VisitExpression(argument, scope);
                CheckCall(call, scope);
                break;
            case FieldAccessExpressionSyntax access:
                VisitExpression(access.Target, scope);
                CheckAccess(access, scope);
                break;
            case CastExpressionSyntax cast:
                CheckType(cast.Type, scope);
                VisitExpression(cast.Expression, scope);
                break;
            case InstanceofExpressionSyntax test:
                VisitExpression(test.Expression, scope);
                if (test.TypeOrPattern is TypeSyntax tested)
                    CheckType(tested, scope);
                else
                    CheckPattern(test.TypeOrPattern, scope);
                break;
            case BinaryExpressionSyntax { Operator: BinaryOperator.LogicalAnd or BinaryOperator.LogicalOr } logical:
                VisitExpression(logical.Left, scope);
                VisitExpression(logical.Right, WithBindings(logical.Left, scope, logical.Operator == BinaryOperator.LogicalAnd));
                break;
            case ConditionalExpressionSyntax conditional:
                VisitExpression(conditional.Condition, scope);
                VisitExpression(conditional.WhenTrue, WithBindings(conditional.Condition, scope, whenTrue: true));
                VisitExpression(conditional.WhenFalse, WithBindings(conditional.Condition, scope, whenTrue: false));
                break;
            case ArrayCreationExpressionSyntax array:
                CheckType(array.ElementType, scope);
                foreach (var dimension in array.Dimensions)
                {
                    if (dimension.Size is { } size)
                        VisitExpression(size, scope);
                }

                if (array.Initializer is { } arrayInitializer)
                    VisitExpression(arrayInitializer, scope);
                break;
            case ClassLiteralExpressionSyntax literal:
                CheckType(literal.Type, scope);
                break;
            case MethodReferenceExpressionSyntax reference:
                if (reference.Target is ExpressionSyntax referenced)
                    VisitExpression(referenced, scope);
                else if (reference.Target is TypeSyntax referencedType)
                    CheckType(referencedType, scope);
                break;
            case SwitchExpressionSyntax switchExpression:
                VisitSwitch(switchExpression.Selector, switchExpression.Cases, scope);
                break;
            default:
                foreach (var child in expression.ChildNodes())
                {
                    if (child is ExpressionSyntax inner)
                        VisitExpression(inner, scope);
                }

                break;
        }
    }

    private Scope WithBindings(ExpressionSyntax condition, Scope scope, bool whenTrue)
    {
        var inner = Scope.ForBlock(scope);
        scopes.DeclareBindings(condition, inner, whenTrue, certain: true);
        return inner;
    }

    private void CheckParameters(ParameterListSyntax parameters, Scope scope)
    {
        foreach (var parameter in parameters.Parameters)
        {
            if (parameter.Type is { } type)
                CheckType(type, scope);
        }
    }

    private void CheckTypeParameters(TypeParameterListSyntax? parameters, Scope scope)
    {
        foreach (var parameter in (IEnumerable<TypeParameterSyntax>?)parameters?.Parameters ?? [])
        {
            foreach (var bound in parameter.Bounds)
                CheckType(bound, scope);
        }
    }

    private void CheckPattern(SyntaxNode node, Scope scope)
    {
        switch (node)
        {
            case TypePatternSyntax typePattern:
                CheckType(typePattern.Type, scope);
                break;
            case RecordPatternSyntax record:
                CheckType(record.Type, scope);
                foreach (var subpattern in record.Subpatterns)
                    CheckPattern(subpattern, scope);
                break;
        }
    }

    private void CheckType(TypeSyntax type, Scope scope)
    {
        switch (type)
        {
            case ClassTypeSyntax classType:
                if (classType.Qualifier is null && !classType.Name.IsMissing && checkNames)
                {
                    var name = classType.Name.Text;
                    if (model.LookupTypeName(name, scope) is null && !model.MayHideType(name, scope))
                        problems.Add(new SemanticProblem(classType.Name.Span, TypeCode, $"Cannot find the type '{name}'"));
                }

                foreach (var argument in (IEnumerable<TypeSyntax>?)classType.TypeArguments?.Arguments ?? [])
                    CheckType(argument, scope);
                break;
            case ArrayTypeSyntax array:
                CheckType(array.ElementType, scope);
                break;
            case WildcardTypeSyntax { Bound: { } bound }:
                CheckType(bound, scope);
                break;
            case IntersectionTypeSyntax intersection:
                foreach (var part in intersection.Types)
                    CheckType(part, scope);
                break;
            case UnionTypeSyntax union:
                foreach (var part in union.Types)
                    CheckType(part, scope);
                break;
        }
    }

    private void CheckCall(MethodInvocationExpressionSyntax call, Scope scope)
    {
        if (!checkNames || call.Name.IsMissing || call.Target is null or SuperExpressionSyntax)
            return;

        var receiver = typer.TypeOf(call.Target, scope);
        if (KnownOwner(receiver) is not { } owner || model.Methods(owner, call.Name.Text).Any())
            return;

        problems.Add(new SemanticProblem(call.Name.Span, MemberCode,
            $"The method {call.Name.Text}(...) is not defined in the type {JavaTypes.Display(receiver.Type)}"));
    }

    private void CheckAccess(FieldAccessExpressionSyntax access, Scope scope)
    {
        if (!checkNames || access.Name.IsMissing)
            return;

        var name = access.Name.Text;
        var target = typer.TypeOf(access.Target, scope);
        if (target is { Kind: ExpressionKind.Value, IsCertain: true, Type: ArrayTypeReference } && name != "length")
        {
            problems.Add(new SemanticProblem(access.Name.Span, MemberCode, $"Arrays have no field '{name}', only 'length'"));
            return;
        }

        if (KnownOwner(target) is not { } owner || model.Fields(owner, name).Any()
            || (target.Kind == ExpressionKind.Type && model.FindMemberType(owner.BinaryName, name) is not null))
        {
            return;
        }

        problems.Add(new SemanticProblem(access.Name.Span, MemberCode, $"'{name}' is not a field of the type {JavaTypes.Display(target.Type)}"));
    }

    // The class of a value or type whose members are all known, or null when a missing member would not be certain.
    private ClassTypeReference? KnownOwner(TypedExpression target) =>
        target is { IsCertain: true, Kind: ExpressionKind.Value or ExpressionKind.Type, Type: ClassTypeReference owner } && model.IsFullyKnown(owner.BinaryName)
            ? owner
            : null;
}
