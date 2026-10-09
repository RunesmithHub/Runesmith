using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Syntax;
using JavaModifiers = Runesmith.Languages.Java.ClassFiles.JavaModifiers;
using SyntaxModifiers = Runesmith.Languages.Java.Syntax.JavaModifiers;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>Builds the symbols of types declared in source, the same kind of symbols class files give, so the rest of the analyzer treats
/// both alike. Members are built the first time they are asked for.</summary>
internal static class SourceSymbols
{
    /// <summary>Builds a type of a project's sources; its declarations by member are filled in when its members are built.</summary>
    public static (ClassSymbol Symbol, Dictionary<object, SyntaxNode> Members) Build(SemanticModel model, SourceType type)
    {
        var enclosing = type.Outer is null ? null : TypeScope(model, type.Outer);
        var nested = type.File.Types.Where(t => t.Outer == type).Select(t => t.BinaryName).ToList();
        var outerKind = type.Outer?.Declaration is InterfaceDeclarationSyntax or AnnotationTypeDeclarationSyntax;
        var members = new Dictionary<object, SyntaxNode>(ReferenceEqualityComparer.Instance);
        var (symbol, hidden) = Create(model, new Declared(type.BinaryName, type.SimpleName, type.Declaration, type.Outer?.BinaryName, outerKind, false),
            enclosing, nested, members);
        type.MayHaveHiddenMembers = hidden;
        return (symbol, members);
    }

    /// <summary>Gets the scope inside a type of the project's sources: its members, those of the types around it, then the file's imports.</summary>
    public static Scope TypeScope(SemanticModel model, SourceType type)
    {
        var parent = type.Outer is null ? null : TypeScope(model, type.Outer);
        return Scope.ForType(model.FileScope, parent, model.FindClass(type.BinaryName), type.BinaryName, () => !model.IsFullyKnown(type.BinaryName));
    }

    /// <summary>Builds a class, interface, enum or record declared inside a block, and the member types declared in it.</summary>
    public static ClassSymbol BuildLocal(SemanticModel model, string binaryName, TypeDeclarationSyntax declaration, Scope scope)
    {
        var nested = DeclareNestedLocals(model, binaryName, declaration.Body, scope);
        return Create(model, new Declared(binaryName, declaration.Name.Text, declaration, null, false, true), scope, nested, null).Symbol;
    }

    /// <summary>Builds the class of an anonymous class body: a subclass of a class, or an implementation of an interface.</summary>
    public static ClassSymbol BuildAnonymous(SemanticModel model, string binaryName, ClassBodySyntax body, ClassTypeReference baseType, Scope scope)
    {
        var baseIsInterface = model.FindClass(baseType.BinaryName)?.Kind is ClassKind.Interface or ClassKind.Annotation;
        var nested = DeclareNestedLocals(model, binaryName, body, scope);
        ClassSymbol? symbol = null;
        symbol = new ClassSymbol(() => Members(model, symbol!, body, null, Scope.ForType(model.FileScope, scope, symbol, binaryName,
            () => !model.IsFullyKnown(baseType.BinaryName)), null, null))
        {
            BinaryName = binaryName,
            SimpleName = "",
            OuterBinaryName = null,
            Kind = ClassKind.Class,
            Modifiers = JavaModifiers.Final,
            MajorVersion = ClassSymbol.HighestMajorVersion,
            IsDeprecated = false,
            TypeParameters = [],
            SuperClass = baseIsInterface ? JavaTypes.Object : baseType,
            Interfaces = baseIsInterface ? [baseType] : [],
            PermittedSubclasses = [],
            RecordComponents = [],
            NestedClasses = nested,
            NestHost = null,
            NestMembers = [],
            EnclosingMethod = null,
            Module = null,
        };
        return symbol;
    }

    /// <summary>Converts the modifiers of a declaration.</summary>
    public static JavaModifiers Convert(SyntaxModifiers modifiers)
    {
        var result = JavaModifiers.None;
        if ((modifiers & SyntaxModifiers.Public) != 0)
            result |= JavaModifiers.Public;
        if ((modifiers & SyntaxModifiers.Protected) != 0)
            result |= JavaModifiers.Protected;
        if ((modifiers & SyntaxModifiers.Private) != 0)
            result |= JavaModifiers.Private;
        if ((modifiers & SyntaxModifiers.Static) != 0)
            result |= JavaModifiers.Static;
        if ((modifiers & SyntaxModifiers.Abstract) != 0)
            result |= JavaModifiers.Abstract;
        if ((modifiers & SyntaxModifiers.Final) != 0)
            result |= JavaModifiers.Final;
        if ((modifiers & SyntaxModifiers.Native) != 0)
            result |= JavaModifiers.Native;
        if ((modifiers & SyntaxModifiers.Synchronized) != 0)
            result |= JavaModifiers.Synchronized;
        if ((modifiers & SyntaxModifiers.Transient) != 0)
            result |= JavaModifiers.Transient;
        if ((modifiers & SyntaxModifiers.Volatile) != 0)
            result |= JavaModifiers.Volatile;
        if ((modifiers & SyntaxModifiers.Strictfp) != 0)
            result |= JavaModifiers.Strict;
        if ((modifiers & SyntaxModifiers.Default) != 0)
            result |= JavaModifiers.Default;
        if ((modifiers & SyntaxModifiers.Sealed) != 0)
            result |= JavaModifiers.Sealed;
        return result;
    }

    /// <summary>Builds the signature of a method declared in source.</summary>
    public static MethodSymbol Method(SemanticModel model, MethodDeclarationSyntax method, Scope typeScope, bool inInterface)
    {
        var flags = method.Modifiers.Flags;
        var scope = Scope.ForMethod(model.FileScope, typeScope, (flags & SyntaxModifiers.Static) != 0);
        var typeParameters = model.DeclareTypeParameters(method.TypeParameters, scope);
        var modifiers = Convert(flags);
        if (inInterface)
        {
            if ((modifiers & JavaModifiers.Private) == 0)
                modifiers |= JavaModifiers.Public;
            if (method.Body is null && (modifiers & (JavaModifiers.Static | JavaModifiers.Private)) == 0)
                modifiers |= JavaModifiers.Abstract;
        }

        var (types, names, varargs) = Parameters(model, method.Parameters, scope);
        if (varargs)
            modifiers |= JavaModifiers.Varargs;
        var returnType = SemanticModel.WithDimensions(model.ResolveType(method.ReturnType, scope), method.Dimensions.Count) ?? Unknown(method.ReturnType);
        return new MethodSymbol(method.Name.Text, modifiers, typeParameters, types, names, true, returnType,
            [.. method.Throws.Select(t => model.ResolveType(t, scope)).OfType<TypeReference>()], false);
    }

    /// <summary>Builds the signature of a constructor declared in source.</summary>
    public static MethodSymbol Constructor(SemanticModel model, ConstructorDeclarationSyntax constructor, Scope typeScope)
    {
        var scope = Scope.ForMethod(model.FileScope, typeScope, false);
        var typeParameters = model.DeclareTypeParameters(constructor.TypeParameters, scope);
        var (types, names, varargs) = Parameters(model, constructor.Parameters, scope);
        var modifiers = Convert(constructor.Modifiers.Flags) | (varargs ? JavaModifiers.Varargs : JavaModifiers.None);
        return new MethodSymbol(MethodSymbol.ConstructorName, modifiers, typeParameters, types, names, true, PrimitiveTypeReference.Void,
            [.. constructor.Throws.Select(t => model.ResolveType(t, scope)).OfType<TypeReference>()], false);
    }

    private static (IReadOnlyList<TypeReference> Types, IReadOnlyList<string> Names, bool Varargs) Parameters(SemanticModel model, ParameterListSyntax list,
        Scope scope)
    {
        var types = new List<TypeReference>();
        var names = new List<string>();
        var varargs = false;
        foreach (var parameter in list.Parameters)
        {
            if (parameter.IsReceiver)
                continue;

            var type = SemanticModel.WithDimensions(parameter.Type is null ? null : model.ResolveType(parameter.Type, scope), parameter.Dimensions.Count)
                ?? Unknown(parameter.Type);
            if (parameter.IsVarargs)
            {
                type = new ArrayTypeReference(type);
                varargs = true;
            }

            types.Add(type);
            names.Add(parameter.Name.Text);
        }

        return (types, names, varargs);
    }

    private static UnresolvedTypeReference Unknown(SyntaxNode? syntax) => new(syntax is ClassTypeSyntax named ? named.QualifiedName : "?");

    /// <summary>What a type declaration needs besides its syntax.</summary>
    private sealed record Declared(string BinaryName, string SimpleName, TypeDeclarationSyntax? Declaration, string? OuterBinaryName, bool InInterface,
        bool IsLocal);

    private static (ClassSymbol Symbol, bool Hidden) Create(SemanticModel model, Declared declared, Scope? enclosing, IReadOnlyList<string> nested,
        Dictionary<object, SyntaxNode>? declarations)
    {
        var declaration = declared.Declaration;
        var header = Scope.ForMethod(model.FileScope, enclosing, false);
        var typeParameters = model.DeclareTypeParameters(declaration?.TypeParameters, header);
        var kind = declaration switch
        {
            InterfaceDeclarationSyntax => ClassKind.Interface,
            AnnotationTypeDeclarationSyntax => ClassKind.Annotation,
            EnumDeclarationSyntax => ClassKind.Enum,
            RecordDeclarationSyntax => ClassKind.Record,
            _ => ClassKind.Class,
        };

        var modifiers = declaration is null ? JavaModifiers.Final : Convert(declaration.Modifiers.Flags);
        if (kind is ClassKind.Interface or ClassKind.Annotation)
            modifiers |= JavaModifiers.Abstract;
        if (kind is ClassKind.Record || (kind is ClassKind.Enum && !HasConstantBodies(declaration!)))
            modifiers |= JavaModifiers.Final;
        if (declared.OuterBinaryName is not null && (kind is not ClassKind.Class || declared.InInterface))
            modifiers |= JavaModifiers.Static;
        if (declared.InInterface)
            modifiers |= JavaModifiers.Public;

        var self = new ClassTypeReference(declared.BinaryName, [.. typeParameters.Select(p => new TypeArgument(WildcardKind.None, new TypeVariableReference(p.Name)))]);
        var (superClass, interfaces) = Supertypes(model, declaration, kind, self, header);
        var components = new List<RecordComponent>();

        ClassSymbol? symbol = null;
        symbol = new ClassSymbol(() =>
        {
            var scope = Scope.ForType(model.FileScope, enclosing, symbol, declared.BinaryName, () => !model.IsFullyKnown(declared.BinaryName));
            return Members(model, symbol!, declaration?.Body, declaration, scope, declarations, declaration is null ? model.Tree.Root.Members : null);
        })
        {
            BinaryName = declared.BinaryName,
            SimpleName = declared.SimpleName,
            OuterBinaryName = declared.OuterBinaryName,
            Kind = kind,
            Modifiers = modifiers,
            MajorVersion = ClassSymbol.HighestMajorVersion,
            IsDeprecated = declaration?.Modifiers.Annotations.Any(a => a.Name.ToString() is "Deprecated" or "java.lang.Deprecated") ?? false,
            TypeParameters = typeParameters,
            SuperClass = superClass,
            Interfaces = interfaces,
            PermittedSubclasses = [],
            RecordComponents = components,
            NestedClasses = nested,
            NestHost = null,
            NestMembers = [],
            EnclosingMethod = null,
            Module = null,
        };

        // Component types see the record's members, inherited member types too, so they are read once the symbol exists.
        if (declaration is RecordDeclarationSyntax record)
        {
            var scope = Scope.ForType(model.FileScope, enclosing, symbol, declared.BinaryName, () => false);
            components.AddRange(record.Header.Components.Select(c => new RecordComponent(c.Name.Text, ComponentType(model, c, scope))));
        }

        return (symbol, declaration is not null && MayHideMembers(model, declaration, header));
    }

    private static TypeReference ComponentType(SemanticModel model, RecordComponentSyntax component, Scope scope)
    {
        var type = model.ResolveType(component.Type, scope) ?? Unknown(component.Type);
        return component.IsVarargs ? new ArrayTypeReference(type) : type;
    }

    private static (ClassTypeReference? SuperClass, IReadOnlyList<ClassTypeReference> Interfaces) Supertypes(SemanticModel model,
        TypeDeclarationSyntax? declaration, ClassKind kind, ClassTypeReference self, Scope header)
    {
        IEnumerable<TypeSyntax> implemented = declaration switch
        {
            ClassDeclarationSyntax c => c.Implements,
            InterfaceDeclarationSyntax i => i.Extends,
            EnumDeclarationSyntax e => e.Implements,
            RecordDeclarationSyntax r => r.Implements,
            _ => [],
        };

        // A supertype no library has stays by its name, so the type's hierarchy is known to be incomplete.
        var interfaces = implemented.Select(t => model.ResolveType(t, header) as ClassTypeReference ?? new ClassTypeReference(UnknownName(t), [])).ToList();
        if (kind == ClassKind.Annotation)
            interfaces.Add(JavaTypes.Class("java.lang.annotation.Annotation"));

        ClassTypeReference? superClass = kind switch
        {
            ClassKind.Interface or ClassKind.Annotation => null,
            ClassKind.Enum => JavaTypes.Class("java.lang.Enum", self with { TypeArguments = [] }),
            ClassKind.Record => JavaTypes.Class("java.lang.Record"),
            _ when declaration is ClassDeclarationSyntax { Extends: { } extends } =>
                model.ResolveType(extends, header) as ClassTypeReference ?? new ClassTypeReference(UnknownName(extends), []),
            _ => JavaTypes.Object,
        };
        return (superClass, interfaces);
    }

    // A name no library has, marked so no class path can have it.
    private static string UnknownName(TypeSyntax syntax) => "?" + (syntax as ClassTypeSyntax)?.QualifiedName;

    private static bool HasConstantBodies(TypeDeclarationSyntax declaration) => declaration.Body.EnumConstants.Any(c => c.Body is not null);

    // Annotation processors such as Lombok add members to the classes they annotate; an annotation the analyzer cannot resolve may be one.
    private static bool MayHideMembers(SemanticModel model, TypeDeclarationSyntax declaration, Scope scope)
    {
        var annotations = declaration.Modifiers.Annotations.AsEnumerable();
        foreach (var member in declaration.Body.Members)
            annotations = annotations.Concat(member.Modifiers.Annotations);

        foreach (var annotation in annotations)
        {
            var name = annotation.Name.ToString();
            var resolved = annotation.Name.Parts.Count == 1
                ? (model.LookupTypeName(name, scope) as ClassTypeReference)?.BinaryName
                : model.FileScope.ResolveCanonical(name);
            if (resolved is null || resolved.StartsWith("lombok.", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static List<string> DeclareNestedLocals(SemanticModel model, string outerName, ClassBodySyntax body, Scope scope)
    {
        var names = new List<string>();
        foreach (var member in body.Members.OfType<TypeDeclarationSyntax>())
        {
            if (member.Name.IsMissing)
                continue;
            names.Add(model.DeclareLocalType(member, member.Name.Text, name => BuildLocal(model, name, member, scope)));
        }

        return names;
    }

    private static (IReadOnlyList<FieldSymbol>, IReadOnlyList<MethodSymbol>) Members(SemanticModel model, ClassSymbol symbol, ClassBodySyntax? body,
        TypeDeclarationSyntax? declaration, Scope scope, Dictionary<object, SyntaxNode>? declarations, SyntaxList<MemberDeclarationSyntax>? implicitMembers)
    {
        var fields = new List<FieldSymbol>();
        var methods = new List<MethodSymbol>();
        var inInterface = symbol.Kind is ClassKind.Interface or ClassKind.Annotation;
        var self = SemanticModel.SelfType(symbol);
        void Remember(object member, SyntaxNode node)
        {
            if (declarations is not null)
                declarations[member] = node;
        }

        foreach (var constant in (IEnumerable<EnumConstantSyntax>?)body?.EnumConstants ?? [])
        {
            var field = new FieldSymbol(constant.Name.Text, self with { TypeArguments = [] }, JavaModifiers.Public | JavaModifiers.Static | JavaModifiers.Final,
                null, true, false);
            fields.Add(field);
            Remember(field, constant);
        }

        foreach (var member in (IEnumerable<MemberDeclarationSyntax>?)implicitMembers ?? (IEnumerable<MemberDeclarationSyntax>?)body?.Members ?? [])
        {
            switch (member)
            {
                case FieldDeclarationSyntax field:
                {
                    var modifiers = Convert(field.Modifiers.Flags);
                    if (inInterface)
                        modifiers |= JavaModifiers.Public | JavaModifiers.Static | JavaModifiers.Final;
                    var fieldScope = Scope.ForMethod(model.FileScope, scope, (modifiers & JavaModifiers.Static) != 0);
                    var type = model.ResolveType(field.Type, fieldScope);
                    foreach (var variable in field.Variables)
                    {
                        if (variable.Name.IsMissing)
                            continue;
                        var symbolField = new FieldSymbol(variable.Name.Text, SemanticModel.WithDimensions(type, variable.Dimensions.Count) ?? Unknown(field.Type),
                            modifiers, null, false, false);
                        fields.Add(symbolField);
                        Remember(symbolField, variable);
                    }

                    break;
                }

                case MethodDeclarationSyntax method when !method.Name.IsMissing:
                {
                    var built = Method(model, method, scope, inInterface);
                    methods.Add(built);
                    Remember(built, method);
                    break;
                }

                case ConstructorDeclarationSyntax constructor:
                {
                    var built = Constructor(model, constructor, scope);
                    methods.Add(built);
                    Remember(built, constructor);
                    break;
                }
            }
        }

        AddImplicitMembers(symbol, declaration, self, fields, methods, Remember);
        return (fields, methods);
    }

    private static void AddImplicitMembers(ClassSymbol symbol, TypeDeclarationSyntax? declaration, ClassTypeReference self, List<FieldSymbol> fields,
        List<MethodSymbol> methods, Action<object, SyntaxNode> remember)
    {
        var access = symbol.Modifiers & (JavaModifiers.Public | JavaModifiers.Protected | JavaModifiers.Private);
        switch (symbol.Kind)
        {
            case ClassKind.Record when declaration is RecordDeclarationSyntax record:
            {
                var components = record.Header.Components;
                for (var i = 0; i < components.Count; i++)
                {
                    var component = components[i];
                    var type = symbol.RecordComponents[i].Type;
                    var field = new FieldSymbol(component.Name.Text, type, JavaModifiers.Private | JavaModifiers.Final, null, false, false);
                    fields.Add(field);
                    remember(field, component);
                    if (!methods.Any(m => m.Name == component.Name.Text && m.ParameterTypes.Count == 0))
                    {
                        var accessor = new MethodSymbol(component.Name.Text, JavaModifiers.Public, [], [], [], true, type, [], false);
                        methods.Add(accessor);
                        remember(accessor, component);
                    }
                }

                if (!methods.Any(m => m.IsConstructor && m.ParameterTypes.Count == components.Count))
                {
                    var canonical = new MethodSymbol(MethodSymbol.ConstructorName, access, [], [.. symbol.RecordComponents.Select(c => c.Type)],
                        [.. symbol.RecordComponents.Select(c => c.Name)], true, PrimitiveTypeReference.Void, [], false);
                    methods.Add(canonical);
                    remember(canonical, record.Header);
                }

                break;
            }

            case ClassKind.Enum:
                methods.Add(new MethodSymbol("values", JavaModifiers.Public | JavaModifiers.Static, [], [], [], true, new ArrayTypeReference(self), [], false));
                methods.Add(new MethodSymbol("valueOf", JavaModifiers.Public | JavaModifiers.Static, [], [JavaTypes.String], ["name"], true, self, [], false));
                if (!methods.Any(m => m.IsConstructor))
                    methods.Add(new MethodSymbol(MethodSymbol.ConstructorName, JavaModifiers.Private, [], [], [], true, PrimitiveTypeReference.Void, [], false));
                break;
            case ClassKind.Class when !methods.Any(m => m.IsConstructor):
                methods.Add(new MethodSymbol(MethodSymbol.ConstructorName, access, [], [], [], true, PrimitiveTypeReference.Void, [], false));
                break;
        }
    }
}
