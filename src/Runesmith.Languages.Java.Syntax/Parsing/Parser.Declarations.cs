namespace Runesmith.Languages.Java.Syntax;

internal sealed partial class Parser
{
    /// <summary>What kind of body members are read in, which decides what a member can be.</summary>
    private enum BodyKind
    {
        TopLevel,
        Class,
        Interface,
        Enum,
        Record,
        Annotation,
    }

    public CompilationUnitSyntax ParseCompilationUnit(int textLength)
    {
        PackageDeclarationSyntax? package = null;
        ModuleDeclarationSyntax? module = null;
        var imports = new List<ImportDeclarationSyntax>();
        var members = new List<MemberDeclarationSyntax>();

        if (Kind == TokenKind.PackageKeyword || (Kind == TokenKind.At && TokenAfterAnnotations(index) == TokenKind.PackageKeyword))
            package = ParsePackageDeclaration();

        while (Kind is TokenKind.ImportKeyword or TokenKind.Semicolon)
        {
            if (!Accept(TokenKind.Semicolon))
                imports.Add(ParseImportDeclaration());
        }

        if (IsModuleDeclarationStart())
            module = ParseModuleDeclaration();

        while (Kind != TokenKind.EndOfFile)
        {
            if (Accept(TokenKind.Semicolon))
                continue;

            var before = index;
            if (Kind == TokenKind.ImportKeyword)
            {
                ErrorAtCurrent("Imports must come before the declarations");
                ParseImportDeclaration();
                continue;
            }

            if (ParseMember(BodyKind.TopLevel) is { } member)
                members.Add(member);
            if (index == before)
                SkipUnexpected("at the top level of the file");
        }

        return new CompilationUnitSyntax(new SourceSpan(0, textLength), package, List(imports), module, List(members));
    }

    private TokenKind TokenAfterAnnotations(int i) => tokens[SkipAnnotations(i)].Kind;

    /// <summary>Gets the index of the first token after the annotations that start at a token, without parsing them.</summary>
    private int SkipAnnotations(int i)
    {
        var last = tokens.Count - 1;
        while (i < last && tokens[i].Kind == TokenKind.At && tokens[i + 1].Kind != TokenKind.InterfaceKeyword)
        {
            i++;
            while (i < last && IsName(tokens[i].Kind))
            {
                i++;
                if (tokens[i].Kind != TokenKind.Dot || i + 1 >= last)
                    break;
                i++;
            }

            if (tokens[i].Kind != TokenKind.OpenParen)
                continue;

            var depth = 0;
            for (; i < last; i++)
            {
                if (tokens[i].Kind == TokenKind.OpenParen)
                {
                    depth++;
                }
                else if (tokens[i].Kind == TokenKind.CloseParen && --depth == 0)
                {
                    i++;
                    break;
                }
            }
        }

        return Math.Min(i, last);
    }

    private bool IsModuleDeclarationStart()
    {
        var i = SkipAnnotations(index);
        var next = Math.Min(i + 1, tokens.Count - 1);
        return (Word(i, "open") && Word(next, "module")) || (Word(i, "module") && IsName(tokens[next].Kind));

        bool Word(int at, string word) => tokens[at].Kind == TokenKind.Identifier && TextOf(tokens[at]).SequenceEqual(word);
    }

    private PackageDeclarationSyntax ParsePackageDeclaration()
    {
        var start = CurrentStart;
        var annotations = ParseAnnotations();
        Expect(TokenKind.PackageKeyword);
        var name = ParseName();
        Expect(TokenKind.Semicolon);
        return new PackageDeclarationSyntax(From(start), annotations, name);
    }

    private ImportDeclarationSyntax ParseImportDeclaration()
    {
        var start = CurrentStart;
        Advance();
        var kind = ImportKind.Single;
        if (Accept(TokenKind.StaticKeyword))
        {
            kind = ImportKind.Static;
        }
        else if (IsContextual("module") && IsName(PeekKind(1)))
        {
            Advance();
            var moduleName = ParseName();
            Expect(TokenKind.Semicolon);
            return new ImportDeclarationSyntax(From(start), ImportKind.Module, moduleName);
        }

        var nameStart = CurrentStart;
        var parts = new List<IdentifierSyntax> { ParseIdentifier() };
        while (Kind == TokenKind.Dot)
        {
            if (PeekKind(1) == TokenKind.Asterisk)
            {
                var name = new NameSyntax(From(nameStart), List(parts));
                Advance();
                Advance();
                Expect(TokenKind.Semicolon);
                return new ImportDeclarationSyntax(From(start), kind == ImportKind.Static ? ImportKind.StaticOnDemand : ImportKind.OnDemand, name);
            }

            Advance();
            parts.Add(ParseIdentifier());
        }

        var single = new NameSyntax(From(nameStart), List(parts));
        Expect(TokenKind.Semicolon);
        return new ImportDeclarationSyntax(From(start), kind, single);
    }

    private ModuleDeclarationSyntax ParseModuleDeclaration()
    {
        var start = CurrentStart;
        var annotations = ParseAnnotations();
        var isOpen = false;
        if (IsContextual("open"))
        {
            Advance();
            isOpen = true;
        }

        Advance();
        var name = ParseName();
        var directives = new List<ModuleDirectiveSyntax>();
        if (Expect(TokenKind.OpenBrace))
        {
            while (Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
            {
                var before = index;
                if (ParseModuleDirective() is { } directive)
                    directives.Add(directive);
                if (index == before)
                    SkipUnexpected("in a module declaration");
            }

            Expect(TokenKind.CloseBrace);
        }

        return new ModuleDeclarationSyntax(From(start), annotations, isOpen, name, List(directives));
    }

    private ModuleDirectiveSyntax? ParseModuleDirective()
    {
        var start = CurrentStart;
        ModuleDirectiveKind kind;
        if (IsContextual("requires"))
            kind = ModuleDirectiveKind.Requires;
        else if (IsContextual("exports"))
            kind = ModuleDirectiveKind.Exports;
        else if (IsContextual("opens"))
            kind = ModuleDirectiveKind.Opens;
        else if (IsContextual("uses"))
            kind = ModuleDirectiveKind.Uses;
        else if (IsContextual("provides"))
            kind = ModuleDirectiveKind.Provides;
        else
            return null;

        Advance();
        var modifiers = RequiresModifiers.None;
        if (kind == ModuleDirectiveKind.Requires)
        {
            while (true)
            {
                if (IsContextual("transitive") && PeekKind(1) is not (TokenKind.Semicolon or TokenKind.Dot))
                    modifiers |= RequiresModifiers.Transitive;
                else if (Kind == TokenKind.StaticKeyword)
                    modifiers |= RequiresModifiers.Static;
                else
                    break;
                Advance();
            }
        }

        var name = ParseName();
        var targets = new List<NameSyntax>();
        var separator = kind switch
        {
            ModuleDirectiveKind.Exports or ModuleDirectiveKind.Opens => "to",
            ModuleDirectiveKind.Provides => "with",
            _ => null,
        };
        if (separator is not null && IsContextual(separator))
        {
            Advance();
            do
            {
                targets.Add(ParseName());
            }
            while (Accept(TokenKind.Comma));
        }
        else if (kind == ModuleDirectiveKind.Provides)
        {
            ErrorExpected("'with'");
        }

        Expect(TokenKind.Semicolon);
        return new ModuleDirectiveSyntax(From(start), kind, modifiers, name, List(targets));
    }

    private SyntaxList<AnnotationSyntax>? ParseAnnotations()
    {
        List<AnnotationSyntax>? annotations = null;
        while (Kind == TokenKind.At && PeekKind(1) != TokenKind.InterfaceKeyword)
            (annotations ??= []).Add(ParseAnnotation());
        return List(annotations);
    }

    private AnnotationSyntax ParseAnnotation()
    {
        var start = CurrentStart;
        Advance();
        var name = ParseName();
        var arguments = new List<AnnotationArgumentSyntax>();
        var hasParentheses = false;
        if (Accept(TokenKind.OpenParen))
        {
            hasParentheses = true;
            if (Kind != TokenKind.CloseParen)
            {
                if (IsName(Kind) && PeekKind(1) == TokenKind.Equals)
                {
                    do
                    {
                        var argumentStart = CurrentStart;
                        var argumentName = ParseIdentifier();
                        Expect(TokenKind.Equals);
                        var value = ParseElementValue();
                        arguments.Add(new AnnotationArgumentSyntax(From(argumentStart), argumentName, value));
                    }
                    while (Accept(TokenKind.Comma));
                }
                else
                {
                    var argumentStart = CurrentStart;
                    var value = ParseElementValue();
                    arguments.Add(new AnnotationArgumentSyntax(From(argumentStart), null, value));
                }
            }

            Expect(TokenKind.CloseParen);
        }

        return new AnnotationSyntax(From(start), name, List(arguments), hasParentheses);
    }

    private SyntaxNode ParseElementValue()
    {
        if (Kind == TokenKind.At)
            return ParseAnnotation();

        if (Kind != TokenKind.OpenBrace)
            return ParseConditional();

        var start = CurrentStart;
        Advance();
        var values = new List<SyntaxNode>();
        while (Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            var before = index;
            values.Add(ParseElementValue());
            if (!Accept(TokenKind.Comma))
                break;
            if (index == before)
                break;
        }

        Expect(TokenKind.CloseBrace);
        return new ElementValueArrayInitializerSyntax(From(start), List(values));
    }

    private ModifiersSyntax? ParseModifiers()
    {
        var start = CurrentStart;
        var flags = JavaModifiers.None;
        List<AnnotationSyntax>? annotations = null;
        var any = false;
        while (true)
        {
            JavaModifiers flag;
            switch (Kind)
            {
                case TokenKind.At when PeekKind(1) != TokenKind.InterfaceKeyword:
                    (annotations ??= []).Add(ParseAnnotation());
                    any = true;
                    continue;
                case TokenKind.PublicKeyword: flag = JavaModifiers.Public; break;
                case TokenKind.ProtectedKeyword: flag = JavaModifiers.Protected; break;
                case TokenKind.PrivateKeyword: flag = JavaModifiers.Private; break;
                case TokenKind.StaticKeyword: flag = JavaModifiers.Static; break;
                case TokenKind.AbstractKeyword: flag = JavaModifiers.Abstract; break;
                case TokenKind.FinalKeyword: flag = JavaModifiers.Final; break;
                case TokenKind.NativeKeyword: flag = JavaModifiers.Native; break;
                case TokenKind.SynchronizedKeyword: flag = JavaModifiers.Synchronized; break;
                case TokenKind.TransientKeyword: flag = JavaModifiers.Transient; break;
                case TokenKind.VolatileKeyword: flag = JavaModifiers.Volatile; break;
                case TokenKind.StrictfpKeyword: flag = JavaModifiers.Strictfp; break;
                case TokenKind.DefaultKeyword when PeekKind(1) is not (TokenKind.Colon or TokenKind.Arrow): flag = JavaModifiers.Default; break;
                case TokenKind.Identifier when IsNonSealed():
                    Advance();
                    Advance();
                    Advance();
                    flags |= JavaModifiers.NonSealed;
                    any = true;
                    continue;
                case TokenKind.Identifier when IsContextual("sealed") && IsModifierFollower(1):
                    flag = JavaModifiers.Sealed;
                    break;
                default:
                    return any ? new ModifiersSyntax(From(start), flags, List(annotations)) : null;
            }

            if ((flags & flag) != 0)
                ErrorAtCurrent($"'{TextOf(Current)}' is repeated");
            flags |= flag;
            any = true;
            Advance();
        }
    }

    private bool IsNonSealed() =>
        IsContextual("non") && PeekKind(1) == TokenKind.Minus && IsContextual(2, "sealed")
        && PeekToken(1).Span.Start == Current.Span.End && PeekToken(2).Span.Start == PeekToken(1).Span.End;

    private bool IsModifierFollower(int ahead) => PeekKind(ahead) switch
    {
        TokenKind.ClassKeyword or TokenKind.InterfaceKeyword or TokenKind.At or TokenKind.PublicKeyword or TokenKind.ProtectedKeyword
            or TokenKind.PrivateKeyword or TokenKind.StaticKeyword or TokenKind.AbstractKeyword or TokenKind.FinalKeyword
            or TokenKind.StrictfpKeyword => true,
        TokenKind.Identifier => IsContextual(ahead, "non") || IsContextual(ahead, "sealed"),
        _ => false,
    };

    private bool IsRecordStart() => IsContextual("record") && IsName(PeekKind(1)) && PeekKind(2) is TokenKind.OpenParen or TokenKind.LessThan;

    private bool IsTypeDeclarationStart() =>
        Kind is TokenKind.ClassKeyword or TokenKind.InterfaceKeyword or TokenKind.EnumKeyword
        || (Kind == TokenKind.At && PeekKind(1) == TokenKind.InterfaceKeyword)
        || IsRecordStart();

    private MemberDeclarationSyntax? ParseMember(BodyKind body)
    {
        var start = CurrentStart;
        var modifiers = ParseModifiers();
        if (Kind == TokenKind.OpenBrace && body != BodyKind.TopLevel)
        {
            var block = ParseBlock();
            return new InitializerDeclarationSyntax(From(start), modifiers, block);
        }

        if (IsTypeDeclarationStart())
            return ParseTypeDeclaration(start, modifiers);

        var typeParameters = Kind == TokenKind.LessThan ? ParseTypeParameters() : null;
        if (body != BodyKind.TopLevel && IsName(Kind) && PeekKind(1) == TokenKind.OpenParen)
            return ParseConstructor(start, modifiers, typeParameters);

        if (body == BodyKind.Record && IsName(Kind) && PeekKind(1) == TokenKind.OpenBrace)
        {
            var name = ParseIdentifier();
            var block = ParseBlock();
            return new CompactConstructorDeclarationSyntax(From(start), modifiers, name, block);
        }

        if (!IsTypeStart())
        {
            if (modifiers is null && typeParameters is null)
                return null;

            ErrorExpected("a declaration");
            return new IncompleteMemberSyntax(From(start), modifiers, null);
        }

        var type = ParseType();
        if (!IsName(Kind))
        {
            ErrorExpected("a name");
            if (Kind is TokenKind.Semicolon)
                Advance();
            return new IncompleteMemberSyntax(From(start), modifiers, type);
        }

        var memberName = ParseIdentifier();
        if (Kind == TokenKind.OpenParen)
            return ParseMethod(start, modifiers, typeParameters, type, memberName, body);

        var variables = ParseVariableDeclarators(memberName);
        Expect(TokenKind.Semicolon);
        return new FieldDeclarationSyntax(From(start), modifiers, type, variables);
    }

    private bool IsTypeStart() => IsName(Kind) || TokenKinds.IsPrimitiveType(Kind) || Kind == TokenKind.At;

    private TypeDeclarationSyntax ParseTypeDeclaration(int start, ModifiersSyntax? modifiers)
    {
        if (!HasStackRoom())
        {
            var missing = new IdentifierSyntax(new SourceSpan(lastEnd, 0), "", isMissing: true);
            var emptyBody = new ClassBodySyntax(new SourceSpan(lastEnd, 0), null, null, hasCloseBrace: false);
            return new ClassDeclarationSyntax(From(start), modifiers, missing, null, null, null, null, emptyBody);
        }

        switch (Kind)
        {
            case TokenKind.ClassKeyword:
            {
                Advance();
                var name = ParseIdentifier();
                var typeParameters = Kind == TokenKind.LessThan ? ParseTypeParameters() : null;
                TypeSyntax? extends = null;
                if (Accept(TokenKind.ExtendsKeyword))
                    extends = ParseType();
                var implements = Accept(TokenKind.ImplementsKeyword) ? ParseTypeList() : null;
                var permits = ParsePermits();
                var body = ParseClassBody(BodyKind.Class);
                return new ClassDeclarationSyntax(From(start), modifiers, name, typeParameters, extends, implements, permits, body);
            }

            case TokenKind.InterfaceKeyword:
            {
                Advance();
                var name = ParseIdentifier();
                var typeParameters = Kind == TokenKind.LessThan ? ParseTypeParameters() : null;
                var extends = Accept(TokenKind.ExtendsKeyword) ? ParseTypeList() : null;
                var permits = ParsePermits();
                var body = ParseClassBody(BodyKind.Interface);
                return new InterfaceDeclarationSyntax(From(start), modifiers, name, typeParameters, extends, permits, body);
            }

            case TokenKind.EnumKeyword:
            {
                Advance();
                var name = ParseIdentifier();
                var implements = Accept(TokenKind.ImplementsKeyword) ? ParseTypeList() : null;
                var body = ParseClassBody(BodyKind.Enum);
                return new EnumDeclarationSyntax(From(start), modifiers, name, implements, body);
            }

            case TokenKind.At:
            {
                Advance();
                Advance();
                var name = ParseIdentifier();
                var body = ParseClassBody(BodyKind.Annotation);
                return new AnnotationTypeDeclarationSyntax(From(start), modifiers, name, body);
            }

            default:
            {
                Advance();
                var name = ParseIdentifier();
                var typeParameters = Kind == TokenKind.LessThan ? ParseTypeParameters() : null;
                var header = ParseRecordHeader();
                var implements = Accept(TokenKind.ImplementsKeyword) ? ParseTypeList() : null;
                var body = ParseClassBody(BodyKind.Record);
                return new RecordDeclarationSyntax(From(start), modifiers, name, typeParameters, header, implements, body);
            }
        }
    }

    private SyntaxList<TypeSyntax>? ParsePermits()
    {
        if (!IsContextual("permits"))
            return null;

        Advance();
        return ParseTypeList();
    }

    private SyntaxList<TypeSyntax>? ParseTypeList()
    {
        var types = new List<TypeSyntax>();
        do
        {
            types.Add(ParseType());
        }
        while (Accept(TokenKind.Comma));
        return List(types);
    }

    private RecordHeaderSyntax ParseRecordHeader()
    {
        var start = CurrentStart;
        var components = new List<RecordComponentSyntax>();
        if (Expect(TokenKind.OpenParen))
        {
            while (Kind is not (TokenKind.CloseParen or TokenKind.EndOfFile))
            {
                var componentStart = CurrentStart;
                var before = index;
                var modifiers = ParseModifiers();
                var type = ParseType();
                ParseAnnotations();
                var isVarargs = Accept(TokenKind.Ellipsis);
                var name = ParseIdentifier();
                components.Add(new RecordComponentSyntax(From(componentStart), modifiers, type, isVarargs, name));
                if (!Accept(TokenKind.Comma) || index == before)
                    break;
            }

            Expect(TokenKind.CloseParen);
        }

        return new RecordHeaderSyntax(From(start), List(components));
    }

    private ClassBodySyntax ParseClassBody(BodyKind body)
    {
        var start = CurrentStart;
        if (!Expect(TokenKind.OpenBrace))
            return new ClassBodySyntax(From(start), null, null, hasCloseBrace: false);

        List<EnumConstantSyntax>? constants = null;
        if (body == BodyKind.Enum)
        {
            constants = [];
            while (IsName(Kind) || Kind == TokenKind.At)
            {
                var before = index;
                constants.Add(ParseEnumConstant());
                if (!Accept(TokenKind.Comma) || index == before)
                    break;
            }

            if (Kind != TokenKind.CloseBrace)
                Expect(TokenKind.Semicolon);
        }

        var members = new List<MemberDeclarationSyntax>();
        while (Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            if (Accept(TokenKind.Semicolon))
                continue;

            var before = index;
            if (ParseMember(body) is { } member)
                members.Add(member);
            if (index == before)
                SkipUnexpected("in a class body");
        }

        var hasCloseBrace = Expect(TokenKind.CloseBrace);
        return new ClassBodySyntax(From(start), List(constants), List(members), hasCloseBrace);
    }

    private EnumConstantSyntax ParseEnumConstant()
    {
        var start = CurrentStart;
        var modifiers = ParseModifiers();
        var name = ParseIdentifier();
        var arguments = Kind == TokenKind.OpenParen ? ParseArguments() : null;
        var body = Kind == TokenKind.OpenBrace ? ParseClassBody(BodyKind.Class) : null;
        return new EnumConstantSyntax(From(start), modifiers, name, arguments, body);
    }

    private TypeParameterListSyntax ParseTypeParameters()
    {
        var start = CurrentStart;
        Advance();
        var parameters = new List<TypeParameterSyntax>();
        do
        {
            var parameterStart = CurrentStart;
            var annotations = ParseAnnotations();
            var name = ParseIdentifier();
            List<TypeSyntax>? bounds = null;
            if (Accept(TokenKind.ExtendsKeyword))
            {
                bounds = [ParseType()];
                while (Accept(TokenKind.Ampersand))
                    bounds.Add(ParseType());
            }

            parameters.Add(new TypeParameterSyntax(From(parameterStart), annotations, name, List(bounds)));
        }
        while (Accept(TokenKind.Comma));
        Expect(TokenKind.GreaterThan);
        return new TypeParameterListSyntax(From(start), List(parameters));
    }

    private ConstructorDeclarationSyntax ParseConstructor(int start, ModifiersSyntax? modifiers, TypeParameterListSyntax? typeParameters)
    {
        var name = ParseIdentifier();
        var parameters = ParseParameters();
        var throws = Accept(TokenKind.ThrowsKeyword) ? ParseTypeList() : null;
        var body = Kind == TokenKind.OpenBrace ? ParseBlock() : MissingBlock();
        return new ConstructorDeclarationSyntax(From(start), modifiers, typeParameters, name, parameters, throws, body);
    }

    private BlockSyntax MissingBlock()
    {
        ErrorExpected("'{'");
        Accept(TokenKind.Semicolon);
        return new BlockSyntax(new SourceSpan(lastEnd, 0), null, hasCloseBrace: false);
    }

    private MethodDeclarationSyntax ParseMethod(int start, ModifiersSyntax? modifiers, TypeParameterListSyntax? typeParameters, TypeSyntax returnType,
        IdentifierSyntax name, BodyKind body)
    {
        var parameters = ParseParameters();
        var dimensions = ParseDimensions();
        var throws = Accept(TokenKind.ThrowsKeyword) ? ParseTypeList() : null;
        SyntaxNode? defaultValue = null;
        if (body == BodyKind.Annotation && Accept(TokenKind.DefaultKeyword))
            defaultValue = ParseElementValue();

        BlockSyntax? block = null;
        if (Kind == TokenKind.OpenBrace)
            block = ParseBlock();
        else
            Expect(TokenKind.Semicolon);

        return new MethodDeclarationSyntax(From(start), modifiers, typeParameters, returnType, name, parameters, dimensions, throws, defaultValue, block);
    }

    private ParameterListSyntax ParseParameters()
    {
        var start = CurrentStart;
        var parameters = new List<ParameterSyntax>();
        if (Expect(TokenKind.OpenParen))
        {
            while (Kind is not (TokenKind.CloseParen or TokenKind.EndOfFile))
            {
                var before = index;
                parameters.Add(ParseParameter());
                if (!Accept(TokenKind.Comma) || index == before)
                    break;
            }

            Expect(TokenKind.CloseParen);
        }

        return new ParameterListSyntax(From(start), List(parameters));
    }

    private ParameterSyntax ParseParameter()
    {
        var start = CurrentStart;
        var modifiers = ParseModifiers();
        var type = ParseType();
        ParseAnnotations();
        var isVarargs = Accept(TokenKind.Ellipsis);
        IdentifierSyntax name;
        if (Kind == TokenKind.ThisKeyword)
        {
            name = KeywordIdentifier();
        }
        else if (IsName(Kind) && PeekKind(1) == TokenKind.Dot && PeekKind(2) == TokenKind.ThisKeyword)
        {
            Advance();
            Advance();
            name = KeywordIdentifier();
        }
        else
        {
            name = ParseIdentifier();
        }

        var dimensions = ParseDimensions();
        return new ParameterSyntax(From(start), modifiers, type, isVarargs, name, dimensions);
    }

    private SyntaxList<VariableDeclaratorSyntax> ParseVariableDeclarators(IdentifierSyntax firstName)
    {
        var variables = new List<VariableDeclaratorSyntax> { ParseVariableDeclaratorRest(firstName) };
        while (Accept(TokenKind.Comma))
            variables.Add(ParseVariableDeclaratorRest(ParseIdentifier()));
        return new SyntaxList<VariableDeclaratorSyntax>(variables);
    }

    private VariableDeclaratorSyntax ParseVariableDeclaratorRest(IdentifierSyntax name)
    {
        var dimensions = ParseDimensions();
        ExpressionSyntax? initializer = null;
        if (Accept(TokenKind.Equals))
            initializer = Kind == TokenKind.OpenBrace ? ParseArrayInitializer() : ParseExpression();
        return new VariableDeclaratorSyntax(From(name.Start), name, dimensions, initializer);
    }
}
