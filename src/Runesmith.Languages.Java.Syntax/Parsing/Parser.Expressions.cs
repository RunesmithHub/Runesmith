namespace Runesmith.Languages.Java.Syntax;

internal sealed partial class Parser
{
    /// <summary>Reads a full expression: a lambda, an assignment, or a conditional expression.</summary>
    private ExpressionSyntax ParseExpression()
    {
        if (!HasStackRoom())
            return new MissingExpressionSyntax(lastEnd);

        if (IsLambdaStart())
            return ParseLambda();

        var start = CurrentStart;
        var left = ParseConditional();
        if (AssignmentOperatorOf(Kind) is not { } assignment)
            return left;

        Advance();
        var right = ParseExpression();
        return new AssignmentExpressionSyntax(From(start), assignment, left, right);
    }

    private static AssignmentOperator? AssignmentOperatorOf(TokenKind kind) => kind switch
    {
        TokenKind.Equals => AssignmentOperator.Assign,
        TokenKind.PlusEquals => AssignmentOperator.Add,
        TokenKind.MinusEquals => AssignmentOperator.Subtract,
        TokenKind.AsteriskEquals => AssignmentOperator.Multiply,
        TokenKind.SlashEquals => AssignmentOperator.Divide,
        TokenKind.PercentEquals => AssignmentOperator.Remainder,
        TokenKind.AmpersandEquals => AssignmentOperator.BitwiseAnd,
        TokenKind.BarEquals => AssignmentOperator.BitwiseOr,
        TokenKind.CaretEquals => AssignmentOperator.ExclusiveOr,
        TokenKind.LessThanLessThanEquals => AssignmentOperator.LeftShift,
        TokenKind.GreaterThanGreaterThanEquals => AssignmentOperator.RightShift,
        TokenKind.GreaterThanGreaterThanGreaterThanEquals => AssignmentOperator.UnsignedRightShift,
        _ => null,
    };

    /// <summary>Reads a conditional expression, or anything of higher precedence; never a lambda or an assignment at its top.</summary>
    private ExpressionSyntax ParseConditional()
    {
        var start = CurrentStart;
        var condition = ParseBinary(0);
        if (!Accept(TokenKind.Question))
            return condition;

        var whenTrue = ParseExpression();
        Expect(TokenKind.Colon);
        var whenFalse = !inCaseLabel && IsLambdaStart() ? ParseLambda() : ParseConditional();
        return new ConditionalExpressionSyntax(From(start), condition, whenTrue, whenFalse);
    }

    private ExpressionSyntax ParseBinary(int minimumPrecedence)
    {
        var start = CurrentStart;
        var left = ParseUnary();
        while (true)
        {
            if (Kind == TokenKind.InstanceofKeyword)
            {
                if (minimumPrecedence > RelationalPrecedence)
                    return left;

                Advance();
                SyntaxNode typeOrPattern = Kind is TokenKind.FinalKeyword or TokenKind.At || IsPatternAfterInstanceof()
                    ? ParsePattern()
                    : ParseType();
                left = new InstanceofExpressionSyntax(From(start), left, typeOrPattern);
                continue;
            }

            var (op, precedence, length) = BinaryOperatorAtCurrent();
            if (length == 0 || precedence < minimumPrecedence)
                return left;

            for (var i = 0; i < length; i++)
                Advance();
            var right = ParseBinary(precedence + 1);
            left = new BinaryExpressionSyntax(From(start), op, left, right);
        }
    }

    private const int RelationalPrecedence = 7;

    private bool IsPatternAfterInstanceof() => LooksLike(() => IsPatternType(ParseType()));

    // A pattern is a type followed by a name, or a class type followed by parentheses (a record pattern).
    private bool IsPatternType(TypeSyntax type) =>
        !type.IsMissing && (IsName(Kind) || (Kind == TokenKind.OpenParen && type is ClassTypeSyntax));

    // Shifts are adjacent '>' tokens, so that type arguments can close one '>' at a time.
    private (BinaryOperator Operator, int Precedence, int Length) BinaryOperatorAtCurrent()
    {
        switch (Kind)
        {
            case TokenKind.BarBar: return (BinaryOperator.LogicalOr, 1, 1);
            case TokenKind.AmpersandAmpersand: return (BinaryOperator.LogicalAnd, 2, 1);
            case TokenKind.Bar: return (BinaryOperator.BitwiseOr, 3, 1);
            case TokenKind.Caret: return (BinaryOperator.ExclusiveOr, 4, 1);
            case TokenKind.Ampersand: return (BinaryOperator.BitwiseAnd, 5, 1);
            case TokenKind.EqualsEquals: return (BinaryOperator.Equals, 6, 1);
            case TokenKind.ExclamationEquals: return (BinaryOperator.NotEquals, 6, 1);
            case TokenKind.LessThan: return (BinaryOperator.LessThan, RelationalPrecedence, 1);
            case TokenKind.LessThanEquals: return (BinaryOperator.LessThanOrEqual, RelationalPrecedence, 1);
            case TokenKind.GreaterThanEquals: return (BinaryOperator.GreaterThanOrEqual, RelationalPrecedence, 1);
            case TokenKind.GreaterThan:
                if (IsAdjacentGreaterThan(1))
                    return IsAdjacentGreaterThan(2) ? (BinaryOperator.UnsignedRightShift, 8, 3) : (BinaryOperator.RightShift, 8, 2);
                return (BinaryOperator.GreaterThan, RelationalPrecedence, 1);
            case TokenKind.LessThanLessThan: return (BinaryOperator.LeftShift, 8, 1);
            case TokenKind.Plus: return (BinaryOperator.Add, 9, 1);
            case TokenKind.Minus: return (BinaryOperator.Subtract, 9, 1);
            case TokenKind.Asterisk: return (BinaryOperator.Multiply, 10, 1);
            case TokenKind.Slash: return (BinaryOperator.Divide, 10, 1);
            case TokenKind.Percent: return (BinaryOperator.Remainder, 10, 1);
            default: return (default, 0, 0);
        }
    }

    private bool IsAdjacentGreaterThan(int ahead) =>
        PeekKind(ahead) == TokenKind.GreaterThan && PeekToken(ahead).Span.Start == PeekToken(ahead - 1).Span.End;

    private ExpressionSyntax ParseUnary()
    {
        if (!HasStackRoom())
            return new MissingExpressionSyntax(lastEnd);

        var start = CurrentStart;
        UnaryOperator? prefix = Kind switch
        {
            TokenKind.Plus => UnaryOperator.Plus,
            TokenKind.Minus => UnaryOperator.Minus,
            TokenKind.PlusPlus => UnaryOperator.PreIncrement,
            TokenKind.MinusMinus => UnaryOperator.PreDecrement,
            TokenKind.Exclamation => UnaryOperator.LogicalNot,
            TokenKind.Tilde => UnaryOperator.BitwiseNot,
            _ => null,
        };
        if (prefix is { } op)
        {
            Advance();
            var operand = ParseUnary();
            return new UnaryExpressionSyntax(From(start), op, operand);
        }

        if (Kind == TokenKind.OpenParen && Speculate(ParseCastType) is { } castType)
        {
            var operand = !inCaseLabel && IsLambdaStart() ? ParseLambda() : ParseUnary();
            return new CastExpressionSyntax(From(start), castType, operand);
        }

        return ParsePostfix(start, ParsePrimary());
    }

    // A cast is (Type) before an operand; a reference type casts only an operand that cannot be read as a binary operation's right side.
    private TypeSyntax? ParseCastType()
    {
        Advance();
        var types = new List<TypeSyntax> { ParseType() };
        while (Accept(TokenKind.Ampersand))
            types.Add(ParseType());
        if (!Accept(TokenKind.CloseParen) || types[0].IsMissing)
            return null;

        var type = types.Count == 1 ? types[0] : new IntersectionTypeSyntax(SourceSpan.FromBounds(types[0].Start, types[^1].End), new SyntaxList<TypeSyntax>(types));
        if (types.Count == 1 && IsPrimitive(type))
            return type;

        return Kind switch
        {
            TokenKind.Identifier or TokenKind.Underscore or TokenKind.OpenParen or TokenKind.Exclamation or TokenKind.Tilde
                or TokenKind.ThisKeyword or TokenKind.SuperKeyword or TokenKind.NewKeyword or TokenKind.SwitchKeyword => type,
            _ when TokenKinds.IsLiteral(Kind) || TokenKinds.IsPrimitiveType(Kind) => type,
            _ => null,
        };
    }

    private static bool IsPrimitive(TypeSyntax type) =>
        type is PrimitiveTypeSyntax || (type is ArrayTypeSyntax { ElementType: PrimitiveTypeSyntax });

    private ExpressionSyntax ParsePrimary()
    {
        var start = CurrentStart;
        switch (Kind)
        {
            case TokenKind.IntegerLiteral: return Literal(LiteralKind.Integer);
            case TokenKind.LongLiteral: return Literal(LiteralKind.Long);
            case TokenKind.FloatLiteral: return Literal(LiteralKind.Float);
            case TokenKind.DoubleLiteral: return Literal(LiteralKind.Double);
            case TokenKind.CharacterLiteral: return Literal(LiteralKind.Character);
            case TokenKind.StringLiteral: return Literal(LiteralKind.String);
            case TokenKind.TextBlock: return Literal(LiteralKind.TextBlock);
            case TokenKind.TrueKeyword: return Literal(LiteralKind.True);
            case TokenKind.FalseKeyword: return Literal(LiteralKind.False);
            case TokenKind.NullKeyword: return Literal(LiteralKind.Null);
            case TokenKind.Identifier or TokenKind.Underscore:
            {
                var name = ParseIdentifier();
                if (Kind == TokenKind.OpenParen)
                {
                    var arguments = ParseArguments();
                    return new MethodInvocationExpressionSyntax(From(start), null, null, name, arguments);
                }

                return new NameExpressionSyntax(From(start), name);
            }

            case TokenKind.ThisKeyword:
                Advance();
                return new ThisExpressionSyntax(From(start), null);
            case TokenKind.SuperKeyword:
                Advance();
                return new SuperExpressionSyntax(From(start), null);
            case TokenKind.NewKeyword:
                return ParseNew(start, null);
            case TokenKind.SwitchKeyword:
            {
                Advance();
                var selector = ParseParenthesizedCondition();
                var cases = ParseSwitchBlock();
                return new SwitchExpressionSyntax(From(start), selector, cases);
            }

            case TokenKind.OpenParen:
            {
                Advance();
                var expression = ParseExpression();
                Expect(TokenKind.CloseParen);
                return new ParenthesizedExpressionSyntax(From(start), expression);
            }

            case TokenKind.At or TokenKind.BooleanKeyword or TokenKind.ByteKeyword or TokenKind.ShortKeyword or TokenKind.CharKeyword
                or TokenKind.IntKeyword or TokenKind.LongKeyword or TokenKind.FloatKeyword or TokenKind.DoubleKeyword or TokenKind.VoidKeyword:
            {
                var type = ParseType();
                return TypeSuffix(start, type);
            }

            default:
                ErrorExpected("an expression");
                return new MissingExpressionSyntax(lastEnd);
        }
    }

    private LiteralExpressionSyntax Literal(LiteralKind kind)
    {
        var token = Advance();
        return new LiteralExpressionSyntax(SourceSpan.FromBounds(StartOf(token), EndOf(token)), kind);
    }

    /// <summary>Reads what may follow a type in an expression: <c>.class</c> or a method reference.</summary>
    private ExpressionSyntax TypeSuffix(int start, TypeSyntax type)
    {
        if (Kind == TokenKind.Dot && PeekKind(1) == TokenKind.ClassKeyword)
        {
            Advance();
            Advance();
            return new ClassLiteralExpressionSyntax(From(start), type);
        }

        if (Kind == TokenKind.ColonColon)
            return ParseMethodReference(start, type);

        ErrorExpected("'.class' or '::'");
        return new ClassLiteralExpressionSyntax(From(start), type);
    }

    private ExpressionSyntax ParsePostfix(int start, ExpressionSyntax expression)
    {
        while (true)
        {
            switch (Kind)
            {
                case TokenKind.Dot:
                    expression = ParseMemberAccess(start, expression);
                    break;
                case TokenKind.OpenBracket when PeekKind(1) == TokenKind.CloseBracket || (PeekKind(1) == TokenKind.At && IsAnnotatedDimension()):
                {
                    var type = WithDimensions(start, ToType(expression));
                    return ParsePostfix(start, TypeSuffix(start, type));
                }

                case TokenKind.OpenBracket:
                {
                    Advance();
                    var indexExpression = ParseExpression();
                    Expect(TokenKind.CloseBracket);
                    expression = new ArrayAccessExpressionSyntax(From(start), expression, indexExpression);
                    break;
                }

                case TokenKind.PlusPlus:
                    Advance();
                    expression = new UnaryExpressionSyntax(From(start), UnaryOperator.PostIncrement, expression);
                    break;
                case TokenKind.MinusMinus:
                    Advance();
                    expression = new UnaryExpressionSyntax(From(start), UnaryOperator.PostDecrement, expression);
                    break;
                case TokenKind.ColonColon:
                    expression = ParseMethodReference(start, expression);
                    break;
                case TokenKind.LessThan when IsNameChain(expression):
                {
                    var type = Speculate(() => GenericTypeForMethodReference(start, expression));
                    if (type is null)
                        return expression;
                    expression = ParseMethodReference(start, type);
                    break;
                }

                default:
                    return expression;
            }
        }
    }

    private bool IsAnnotatedDimension() => tokens[SkipAnnotations(index + 1)].Kind == TokenKind.CloseBracket;

    private ExpressionSyntax ParseMemberAccess(int start, ExpressionSyntax target)
    {
        Advance();
        switch (Kind)
        {
            case TokenKind.Identifier or TokenKind.Underscore:
            {
                var name = ParseIdentifier();
                if (Kind != TokenKind.OpenParen)
                    return new FieldAccessExpressionSyntax(From(start), target, name);

                var arguments = ParseArguments();
                return new MethodInvocationExpressionSyntax(From(start), target, null, name, arguments);
            }

            case TokenKind.LessThan:
            {
                var typeArguments = ParseTypeArguments();
                var name = Kind is TokenKind.SuperKeyword or TokenKind.ThisKeyword ? KeywordIdentifier() : ParseIdentifier();
                var arguments = ParseArguments();
                return new MethodInvocationExpressionSyntax(From(start), target, typeArguments, name, arguments);
            }

            case TokenKind.ThisKeyword:
                Advance();
                return new ThisExpressionSyntax(From(start), target);
            case TokenKind.SuperKeyword when PeekKind(1) == TokenKind.OpenParen:
            {
                var name = KeywordIdentifier();
                var arguments = ParseArguments();
                return new MethodInvocationExpressionSyntax(From(start), target, null, name, arguments);
            }

            case TokenKind.SuperKeyword:
                Advance();
                return new SuperExpressionSyntax(From(start), target);
            case TokenKind.NewKeyword:
                return ParseNew(start, target);
            case TokenKind.ClassKeyword:
                Advance();
                return new ClassLiteralExpressionSyntax(From(start), ToType(target));
            default:
                ErrorExpected("a name");
                return new FieldAccessExpressionSyntax(From(start), target, new IdentifierSyntax(new SourceSpan(lastEnd, 0), "", isMissing: true));
        }
    }

    private static bool IsNameChain(ExpressionSyntax expression) => expression switch
    {
        NameExpressionSyntax => true,
        FieldAccessExpressionSyntax access => IsNameChain(access.Target),
        _ => false,
    };

    // Name<Args>.Name<Args>[]:: is a generic type before a method reference; anything else after '<' is a comparison.
    private TypeSyntax? GenericTypeForMethodReference(int start, ExpressionSyntax names)
    {
        var qualifier = (ClassTypeSyntax)ToType(names);
        var firstArguments = ParseTypeArguments();
        var type = new ClassTypeSyntax(From(start), qualifier.Qualifier, null, qualifier.Name, firstArguments);
        while (Kind == TokenKind.Dot && IsName(PeekKind(1)))
        {
            Advance();
            var name = ParseIdentifier();
            var typeArguments = Kind == TokenKind.LessThan ? ParseTypeArguments() : null;
            type = new ClassTypeSyntax(From(start), type, null, name, typeArguments);
        }

        var result = WithDimensions(start, type);
        return Kind == TokenKind.ColonColon ? result : null;
    }

    /// <summary>Turns a name read as an expression into the type it names, such as before <c>.class</c>.</summary>
    private TypeSyntax ToType(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case NameExpressionSyntax name:
                return new ClassTypeSyntax(name.Span, null, null, name.Name, null);
            case FieldAccessExpressionSyntax { Target: var target } access when IsNameChain(target):
                return new ClassTypeSyntax(access.Span, (ClassTypeSyntax)ToType(target), null, access.Name, null);
            default:
                Error(expression.Span, "Expected a type");
                return new MissingTypeSyntax(expression.Start);
        }
    }

    private MethodReferenceExpressionSyntax ParseMethodReference(int start, SyntaxNode target)
    {
        Advance();
        var typeArguments = Kind == TokenKind.LessThan ? ParseTypeArguments() : null;
        var name = Kind == TokenKind.NewKeyword ? KeywordIdentifier() : ParseIdentifier();
        return new MethodReferenceExpressionSyntax(From(start), target, typeArguments, name);
    }

    private ArgumentListSyntax ParseArguments()
    {
        var start = CurrentStart;
        var arguments = new List<ExpressionSyntax>();
        if (!Expect(TokenKind.OpenParen))
            return new ArgumentListSyntax(From(start), null, hasCloseParenthesis: false);

        if (Kind != TokenKind.CloseParen)
        {
            do
            {
                arguments.Add(ParseExpression());
            }
            while (Accept(TokenKind.Comma));
        }

        var hasClose = Expect(TokenKind.CloseParen);
        return new ArgumentListSyntax(From(start), List(arguments), hasClose);
    }

    private ExpressionSyntax ParseNew(int start, ExpressionSyntax? outer)
    {
        Advance();
        var constructorTypeArguments = Kind == TokenKind.LessThan ? ParseTypeArguments() : null;
        var typeStart = CurrentStart;
        var annotations = ParseAnnotations();
        TypeSyntax type;
        if (TokenKinds.IsPrimitiveType(Kind))
        {
            type = ParsePrimitiveType(typeStart, annotations);
        }
        else if (IsName(Kind))
        {
            type = ParseClassType(typeStart, annotations);
        }
        else
        {
            ErrorExpected("a type");
            type = new MissingTypeSyntax(lastEnd);
        }

        if (Kind == TokenKind.OpenBracket || (Kind == TokenKind.At && tokens[SkipAnnotations(index)].Kind == TokenKind.OpenBracket))
            return ParseArrayCreation(start, type);

        if (Kind == TokenKind.OpenBrace && type is PrimitiveTypeSyntax)
            ErrorExpected("'['");

        var arguments = ParseArguments();
        var body = Kind == TokenKind.OpenBrace ? ParseClassBody(BodyKind.Class) : null;
        return new ObjectCreationExpressionSyntax(From(start), outer, constructorTypeArguments, type, arguments, body);
    }

    private ArrayCreationExpressionSyntax ParseArrayCreation(int start, TypeSyntax elementType)
    {
        var dimensions = new List<DimensionSyntax>();
        while (Kind == TokenKind.OpenBracket || (Kind == TokenKind.At && tokens[SkipAnnotations(index)].Kind == TokenKind.OpenBracket))
        {
            var dimensionStart = CurrentStart;
            var annotations = ParseAnnotations();
            Advance();
            ExpressionSyntax? size = null;
            if (Kind != TokenKind.CloseBracket)
            {
                if (dimensions.Count > 0 && dimensions[^1].Size is null)
                    ErrorAtCurrent("A dimension with a size cannot follow one without");
                size = ParseExpression();
            }

            Expect(TokenKind.CloseBracket);
            dimensions.Add(new DimensionSyntax(From(dimensionStart), annotations, size));
        }

        ArrayInitializerExpressionSyntax? initializer = null;
        if (Kind == TokenKind.OpenBrace)
        {
            if (dimensions.Any(d => d.Size is not null))
                ErrorAtCurrent("An array with sizes cannot have an initializer");
            initializer = ParseArrayInitializer();
        }
        else if (dimensions[0].Size is null)
        {
            ErrorExpected("an array size or an initializer");
        }

        return new ArrayCreationExpressionSyntax(From(start), elementType, new SyntaxList<DimensionSyntax>(dimensions), initializer);
    }

    private ArrayInitializerExpressionSyntax ParseArrayInitializer()
    {
        var start = CurrentStart;
        Advance();
        var elements = new List<ExpressionSyntax>();
        while (Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            var before = index;
            elements.Add(Kind == TokenKind.OpenBrace ? ParseArrayInitializer() : ParseExpression());
            if (!Accept(TokenKind.Comma) || index == before)
                break;
        }

        Expect(TokenKind.CloseBrace);
        return new ArrayInitializerExpressionSyntax(From(start), List(elements));
    }

    private bool IsLambdaStart()
    {
        if (IsName(Kind))
            return PeekKind(1) == TokenKind.Arrow;

        if (Kind != TokenKind.OpenParen)
            return false;

        var depth = 0;
        for (var i = index; i < tokens.Count; i++)
        {
            switch (tokens[i].Kind)
            {
                case TokenKind.OpenParen:
                    depth++;
                    break;
                case TokenKind.CloseParen when --depth == 0:
                    return i + 1 < tokens.Count && tokens[i + 1].Kind == TokenKind.Arrow;
                case TokenKind.OpenBrace or TokenKind.CloseBrace or TokenKind.Semicolon or TokenKind.EndOfFile:
                    return false;
            }
        }

        return false;
    }

    private LambdaExpressionSyntax ParseLambda()
    {
        var start = CurrentStart;
        var parameters = new List<ParameterSyntax>();
        var hasParentheses = Kind == TokenKind.OpenParen;
        if (hasParentheses)
        {
            Advance();
            while (Kind is not (TokenKind.CloseParen or TokenKind.EndOfFile))
            {
                var before = index;
                parameters.Add(ParseLambdaParameter());
                if (!Accept(TokenKind.Comma) || index == before)
                    break;
            }

            Expect(TokenKind.CloseParen);
        }
        else
        {
            var name = ParseIdentifier();
            parameters.Add(new ParameterSyntax(name.Span, null, null, isVarargs: false, name, null));
        }

        Expect(TokenKind.Arrow);
        SyntaxNode body = Kind == TokenKind.OpenBrace ? ParseBlock() : ParseExpression();
        return new LambdaExpressionSyntax(From(start), List(parameters), hasParentheses, body);
    }

    private ParameterSyntax ParseLambdaParameter()
    {
        var start = CurrentStart;
        if (IsName(Kind) && PeekKind(1) is TokenKind.Comma or TokenKind.CloseParen)
        {
            var inferred = ParseIdentifier();
            return new ParameterSyntax(From(start), null, null, isVarargs: false, inferred, null);
        }

        var modifiers = ParseModifiers();
        var type = ParseLocalType();
        ParseAnnotations();
        var isVarargs = Accept(TokenKind.Ellipsis);
        var name = ParseIdentifier();
        var dimensions = ParseDimensions();
        return new ParameterSyntax(From(start), modifiers, type, isVarargs, name, dimensions);
    }
}
