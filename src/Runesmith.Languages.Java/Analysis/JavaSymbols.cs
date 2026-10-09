using System.Text;
using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Semantics;
using Runesmith.Languages.Java.Syntax;
using Runesmith.LanguageServices;
using Runesmith.Text;
using JavaModifiers = Runesmith.Languages.Java.ClassFiles.JavaModifiers;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>A type, by its binary name.</summary>
internal sealed record TypeName(string BinaryName);

/// <summary>A package, by its name.</summary>
internal sealed record PackageName(string Name);

/// <summary>Finds the symbol a name in source stands for, and describes symbols: their signatures, documentation and declarations.</summary>
/// <remarks>Symbols are <see cref="LocalSymbol"/>s, field and method <see cref="MemberRef{T}"/>s, <see cref="TypeName"/>s and
/// <see cref="PackageName"/>s.</remarks>
internal static class JavaSymbols
{
    /// <summary>Finds the symbol of the name at an offset, and the name's span.</summary>
    public static (object Symbol, SourceSpan Span)? Find(SemanticModel model, int offset)
    {
        var tree = model.Tree;
        var token = tree.FindToken(offset);
        if (token.Kind != TokenKind.Identifier || !token.Span.ContainsInclusive(offset))
        {
            if (tree.FindTokenBefore(offset) is not { Kind: TokenKind.Identifier } before || before.Span.End != offset)
                return null;
            token = before;
        }

        var typer = new ExpressionTyper(model);
        var builder = new ScopeBuilder(typer);
        var scope = builder.ScopeAt(token.Span.Start, out var path);
        var name = path[^1] as IdentifierSyntax;
        if (name is null || path.Count < 2)
            return null;

        var parent = path[^2];
        var symbol = Resolve(model, typer, builder, scope, name, parent, path);
        return symbol is null ? null : (symbol, name.Span);
    }

    private static object? Resolve(SemanticModel model, ExpressionTyper typer, ScopeBuilder builder, Scope scope, IdentifierSyntax name, SyntaxNode parent,
        IReadOnlyList<SyntaxNode> path)
    {
        switch (parent)
        {
            case TypeDeclarationSyntax type when type.Name == name:
                return model.File.TypeOf(type) is { } declared ? new TypeName(declared.BinaryName) : FindLocalType(scope, type.Name.Text);
            case MethodDeclarationSyntax or ConstructorDeclarationSyntax or VariableDeclaratorSyntax or EnumConstantSyntax or RecordComponentSyntax
                when MemberDeclaredBy(model, scope, parent) is { } member:
                return member;
            case VariableDeclaratorSyntax or ParameterSyntax or ForEachStatementSyntax or CatchClauseSyntax or TypePatternSyntax:
                return DeclaredLocal(builder, scope, name, path);
            case NameExpressionSyntax nameExpression:
                return Symbol(typer.TypeOf(nameExpression, scope));
            case FieldAccessExpressionSyntax access when access.Name == name:
                return Symbol(typer.TypeOf(access, scope));
            case MethodInvocationExpressionSyntax call when call.Name == name:
                return typer.ResolveCall(call, scope).Chosen;
            case ClassTypeSyntax classType when classType.Name == name:
                return model.ResolveType(classType, scope) is ClassTypeReference resolved && model.FindClass(resolved.BinaryName) is not null
                    ? new TypeName(resolved.BinaryName)
                    : null;
            case NameSyntax qualified:
            {
                var parts = qualified.Parts.TakeWhile(p => p != name).Select(p => p.Text).Append(name.Text).ToList();
                var text = string.Join('.', parts);
                if (model.FileScope.ResolveCanonical(text) is { } type)
                    return new TypeName(type);
                return model.Project.IsPackage(text) ? new PackageName(text) : null;
            }

            default:
                return null;
        }
    }

    private static object? Symbol(TypedExpression typed) => typed.Kind switch
    {
        ExpressionKind.Type when typed.Symbol is string binary => new TypeName(binary),
        ExpressionKind.Package when typed.Symbol is string package => new PackageName(package),
        ExpressionKind.Value when typed.Symbol is LocalSymbol or MemberRef<FieldSymbol> or MemberRef<MethodSymbol> => typed.Symbol,
        _ => null,
    };

    private static TypeName? FindLocalType(Scope scope, string name)
    {
        for (var current = scope; current is not null; current = current.Parent)
        {
            if (current.LocalTypes.TryGetValue(name, out var binary))
                return new TypeName(binary);
            if (current.Kind == ScopeKind.Type && current.Type?.SimpleName == name)
                return new TypeName(current.TypeName!);
        }

        return null;
    }

    // A local's declaration is not in the scope at its own name, so its declaration is read again into a scope of its own.
    private static LocalSymbol? DeclaredLocal(ScopeBuilder builder, Scope scope, IdentifierSyntax name, IReadOnlyList<SyntaxNode> path)
    {
        var declared = Scope.ForBlock(scope);
        var parent = path[^2];
        var grandparent = path.Count >= 3 ? path[^3] : null;
        switch (parent)
        {
            case VariableDeclaratorSyntax when grandparent is LocalVariableDeclarationSyntax local:
                builder.DeclareLocals(local, declared, LocalKind.Local);
                break;
            case ParameterSyntax when grandparent is ParameterListSyntax list:
                builder.DeclareParameters(list, declared);
                break;
            case ParameterSyntax when grandparent is LambdaExpressionSyntax lambda:
                return builder.EnterLambda(lambda, scope, null).FindLocal(name.Text);
            case ForEachStatementSyntax forEach:
                builder.DeclareForEach(forEach, declared);
                break;
            case CatchClauseSyntax catchClause:
                builder.DeclareCatch(catchClause, declared);
                break;
            case TypePatternSyntax:
                foreach (var node in path.Reverse())
                {
                    if (node is InstanceofExpressionSyntax test)
                    {
                        builder.DeclareBindings(test, declared, whenTrue: true, certain: true);
                        break;
                    }
                }

                break;
        }

        return declared.FindLocal(name.Text);
    }

    // The field, method or constructor a declaration in a type's body declares, found among the members of the type around it.
    private static object? MemberDeclaredBy(SemanticModel model, Scope scope, SyntaxNode declaration)
    {
        if (declaration is VariableDeclaratorSyntax && scope.Kind != ScopeKind.Type && scope.Parent?.Kind != ScopeKind.Type)
            return null;
        if (scope.EnclosingType() is not { Type: { } type } || model.Project.FindSourceType(type.BinaryName) is not { } source)
            return null;

        foreach (var method in type.Methods)
        {
            if (source.DeclarationOf(method) == declaration)
                return new MemberRef<MethodSymbol>(method, type, null, true);
        }

        foreach (var field in type.Fields)
        {
            if (source.DeclarationOf(field) == declaration)
                return new MemberRef<FieldSymbol>(field, type, null, true);
        }

        return null;
    }

    /// <summary>Describes a symbol as a line of Java, such as <c>int String.length()</c>.</summary>
    public static string Describe(SemanticModel model, object symbol) => symbol switch
    {
        LocalSymbol local => $"{JavaTypes.Display(local.Type)} {local.Name}",
        MemberRef<FieldSymbol> field => $"{Modifiers(field.Member.Modifiers)}{JavaTypes.Display(field.Substitute(field.Member.Type))} {field.Owner.SimpleName}.{field.Member.Name}",
        MemberRef<MethodSymbol> method => DescribeMethod(method),
        TypeName type => DescribeType(model, type.BinaryName),
        PackageName package => "package " + package.Name,
        _ => "",
    };

    public static string DescribeMethod(MemberRef<MethodSymbol> method)
    {
        var member = method.Member;
        var text = new StringBuilder(Modifiers(member.Modifiers & ~JavaModifiers.Varargs));
        if (member.TypeParameters.Count > 0)
            text.Append('<').AppendJoin(", ", member.TypeParameters.Select(p => p.ToJavaString(false))).Append("> ");
        if (!member.IsConstructor)
            text.Append(JavaTypes.Display(method.Substitute(member.ReturnType))).Append(' ').Append(method.Owner.SimpleName).Append('.');
        text.Append(member.IsConstructor ? method.Owner.SimpleName : member.Name).Append('(').AppendJoin(", ", Parameters(method)).Append(')');
        return text.ToString();
    }

    /// <summary>Gets each parameter of a method with its owner's type variables replaced, such as <c>String x</c>.</summary>
    public static IEnumerable<string> Parameters(MemberRef<MethodSymbol> method)
    {
        var member = method.Member;
        for (var i = 0; i < member.ParameterTypes.Count; i++)
        {
            var type = method.Substitute(member.ParameterTypes[i]);
            var text = member.IsVarargs && i == member.ParameterTypes.Count - 1 && type is ArrayTypeReference array
                ? JavaTypes.Display(array.ElementType) + "..."
                : JavaTypes.Display(type);
            yield return text + " " + member.ParameterNames[i];
        }
    }

    public static string DescribeType(SemanticModel model, string binaryName)
    {
        if (model.FindClass(binaryName) is not { } symbol)
            return ClassNames.SourceName(binaryName);

        var keyword = symbol.Kind switch
        {
            ClassKind.Interface => "interface",
            ClassKind.Enum => "enum",
            ClassKind.Record => "record",
            ClassKind.Annotation => "@interface",
            _ => "class",
        };
        var parameters = symbol.TypeParameters.Count > 0 ? "<" + string.Join(", ", symbol.TypeParameters.Select(p => p.ToJavaString(false))) + ">" : "";
        var name = model.IsLocalType(binaryName) ? symbol.SimpleName : ClassNames.SourceName(binaryName);
        return $"{Modifiers(symbol.Modifiers & ~(JavaModifiers.Abstract | JavaModifiers.Sealed))}{keyword} {name}{parameters}";
    }

    private static string Modifiers(JavaModifiers modifiers)
    {
        var text = new StringBuilder();
        if ((modifiers & JavaModifiers.Public) != 0)
            text.Append("public ");
        if ((modifiers & JavaModifiers.Protected) != 0)
            text.Append("protected ");
        if ((modifiers & JavaModifiers.Private) != 0)
            text.Append("private ");
        if ((modifiers & JavaModifiers.Static) != 0)
            text.Append("static ");
        if ((modifiers & JavaModifiers.Abstract) != 0)
            text.Append("abstract ");
        if ((modifiers & JavaModifiers.Final) != 0)
            text.Append("final ");
        if ((modifiers & JavaModifiers.Default) != 0)
            text.Append("default ");
        return text.ToString();
    }

    /// <summary>Gets a symbol's documentation as Markdown, from its documentation comment in the project's sources; the JDK's API has none.</summary>
    public static string? Documentation(SemanticModel model, object symbol)
    {
        var (tree, node) = Declaration(model, symbol);
        if (tree is null || node is null)
            return null;

        // A field's comment is before its declaration, not before one of its variables.
        if (node is VariableDeclaratorSyntax)
            node = tree.GetPath(node.Start).LastOrDefault(n => n is FieldDeclarationSyntax) ?? node;
        return tree.LeadingDocComment(node) is { } comment ? JavadocMarkdown.Convert(comment) : null;
    }

    /// <summary>Gets where a symbol is declared in the project's sources, or null for library symbols.</summary>
    public static SourceLocation? Location(SemanticModel model, object symbol, string currentPath, TextSnapshot currentText)
    {
        if (symbol is LocalSymbol local)
            return Located(currentPath, currentText, local.NameSpan);

        var (tree, node) = Declaration(model, symbol);
        if (tree is null || node is null || NameOf(node) is not { } name)
            return null;

        var path = tree == model.Tree ? currentPath : FileOf(model, symbol) ?? currentPath;
        return tree == model.Tree ? Located(path, currentText, name) : Located(path, tree.Text, name);
    }

    private static string? FileOf(SemanticModel model, object symbol) => symbol switch
    {
        TypeName type => model.Project.FindSourceType(type.BinaryName)?.File.Path,
        MemberRef<FieldSymbol> field => model.Project.FindSourceType(field.Owner.BinaryName)?.File.Path,
        MemberRef<MethodSymbol> method => model.Project.FindSourceType(method.Owner.BinaryName)?.File.Path,
        _ => null,
    };

    private static (JavaSyntaxTree? Tree, SyntaxNode? Node) Declaration(SemanticModel model, object symbol)
    {
        switch (symbol)
        {
            case TypeName type:
                if (model.Project.FindSourceType(type.BinaryName) is { Declaration: { } declaration } source)
                    return (source.File.Tree, declaration);
                return model.LocalDeclaration(type.BinaryName) is TypeDeclarationSyntax local ? (model.Tree, local) : (null, null);
            case MemberRef<FieldSymbol> field:
                return MemberDeclaration(model, field.Owner, field.Member);
            case MemberRef<MethodSymbol> method:
                return MemberDeclaration(model, method.Owner, method.Member);
            default:
                return (null, null);
        }
    }

    private static (JavaSyntaxTree?, SyntaxNode?) MemberDeclaration(SemanticModel model, ClassSymbol owner, object member)
    {
        if (model.Project.FindSourceType(owner.BinaryName) is not { } source)
            return (null, null);

        _ = owner.Methods;
        return source.DeclarationOf(member) is { } node ? (source.File.Tree, node) : (null, null);
    }

    private static SourceSpan? NameOf(SyntaxNode node) => node switch
    {
        TypeDeclarationSyntax type => type.Name.Span,
        MethodDeclarationSyntax method => method.Name.Span,
        ConstructorDeclarationSyntax constructor => constructor.Name.Span,
        VariableDeclaratorSyntax variable => variable.Name.Span,
        EnumConstantSyntax constant => constant.Name.Span,
        RecordComponentSyntax component => component.Name.Span,
        RecordHeaderSyntax header => header.Span,
        _ => null,
    };

    private static SourceLocation Located(string path, TextSnapshot text, SourceSpan span) =>
        new(path, text.GetPosition(span.Start), text.GetPosition(span.End));

    private static SourceLocation Located(string path, string text, SourceSpan span) => new(path, PositionIn(text, span.Start), PositionIn(text, span.End));

    private static TextPosition PositionIn(string text, int offset)
    {
        var line = 0;
        var lineStart = 0;
        for (var i = 0; i < offset && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return new TextPosition(line, offset - lineStart);
    }
}
