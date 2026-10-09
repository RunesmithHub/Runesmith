namespace Runesmith.Languages.Java.Syntax;

internal sealed partial class Parser
{
    public BlockSyntax ParseBlock()
    {
        var start = CurrentStart;
        if (!Expect(TokenKind.OpenBrace))
            return new BlockSyntax(From(start), null, hasCloseBrace: false);

        var statements = new List<StatementSyntax>();
        while (Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            var before = index;
            statements.Add(ParseBlockStatement());
            if (index == before)
            {
                SkipUnexpected("in a block");
                statements.RemoveAt(statements.Count - 1);
            }
        }

        var hasCloseBrace = Expect(TokenKind.CloseBrace);
        return new BlockSyntax(From(start), List(statements), hasCloseBrace);
    }

    private StatementSyntax ParseBlockStatement()
    {
        if (!HasStackRoom())
            return new IncompleteStatementSyntax(From(CurrentStart));

        var start = CurrentStart;
        switch (Kind)
        {
            case TokenKind.ClassKeyword or TokenKind.InterfaceKeyword or TokenKind.EnumKeyword or TokenKind.AbstractKeyword
                or TokenKind.StrictfpKeyword or TokenKind.StaticKeyword or TokenKind.FinalKeyword or TokenKind.At:
            {
                var modifiers = ParseModifiers();
                if (IsTypeDeclarationStart())
                {
                    var declaration = ParseTypeDeclaration(start, modifiers);
                    return new LocalTypeDeclarationStatementSyntax(From(start), declaration);
                }

                return ParseLocalVariableDeclaration(start, modifiers, requireSemicolon: true);
            }

            case TokenKind.Identifier:
                if (IsRecordStart())
                {
                    var declaration = ParseTypeDeclaration(start, null);
                    return new LocalTypeDeclarationStatementSyntax(From(start), declaration);
                }

                if (IsYieldStatement() || PeekKind(1) == TokenKind.Colon)
                    return ParseStatement();
                if (IsLocalVariableDeclarationStart())
                    return ParseLocalVariableDeclaration(start, null, requireSemicolon: true);
                return ParseStatement();

            case TokenKind.Underscore when IsLocalVariableDeclarationStart():
                return ParseLocalVariableDeclaration(start, null, requireSemicolon: true);

            case TokenKind.BooleanKeyword or TokenKind.ByteKeyword or TokenKind.ShortKeyword or TokenKind.CharKeyword or TokenKind.IntKeyword
                or TokenKind.LongKeyword or TokenKind.FloatKeyword or TokenKind.DoubleKeyword when !IsPrimitiveExpressionStart():
                return ParseLocalVariableDeclaration(start, null, requireSemicolon: true);

            default:
                return ParseStatement();
        }
    }

    // A primitive type starts an expression only as int.class, int[].class or int[]::new.
    private bool IsPrimitiveExpressionStart()
    {
        var i = 1;
        while (PeekKind(i) == TokenKind.OpenBracket && PeekKind(i + 1) == TokenKind.CloseBracket)
            i += 2;
        return PeekKind(i) is TokenKind.Dot or TokenKind.ColonColon;
    }

    private bool IsLocalVariableDeclarationStart()
    {
        if (IsContextual("var") && IsName(PeekKind(1)))
            return true;

        return LooksLike(() => !ParseType().IsMissing && IsName(Kind));
    }

    private LocalVariableDeclarationSyntax ParseLocalVariableDeclaration(int start, ModifiersSyntax? modifiers, bool requireSemicolon)
    {
        var type = ParseLocalType();
        var variables = ParseVariableDeclarators(ParseIdentifier());
        if (requireSemicolon)
            Expect(TokenKind.Semicolon);
        return new LocalVariableDeclarationSyntax(From(start), modifiers, type, variables);
    }

    private bool IsYieldStatement() =>
        IsContextual("yield") && PeekKind(1) is not (TokenKind.Equals or TokenKind.PlusEquals or TokenKind.MinusEquals or TokenKind.AsteriskEquals
            or TokenKind.SlashEquals or TokenKind.PercentEquals or TokenKind.AmpersandEquals or TokenKind.BarEquals or TokenKind.CaretEquals
            or TokenKind.LessThanLessThanEquals or TokenKind.GreaterThanGreaterThanEquals or TokenKind.GreaterThanGreaterThanGreaterThanEquals
            or TokenKind.Dot or TokenKind.OpenBracket or TokenKind.ColonColon or TokenKind.Arrow or TokenKind.Semicolon or TokenKind.Colon
            or TokenKind.PlusPlus or TokenKind.MinusMinus or TokenKind.EndOfFile);

    private YieldStatementSyntax ParseYield()
    {
        var start = CurrentStart;
        Advance();
        var expression = ParseExpression();
        Expect(TokenKind.Semicolon);
        return new YieldStatementSyntax(From(start), expression);
    }

    private StatementSyntax ParseStatement()
    {
        if (!HasStackRoom())
            return new IncompleteStatementSyntax(From(CurrentStart));

        var start = CurrentStart;
        switch (Kind)
        {
            case TokenKind.OpenBrace:
                return ParseBlock();
            case TokenKind.Semicolon:
                Advance();
                return new EmptyStatementSyntax(From(start));
            case TokenKind.IfKeyword:
            {
                Advance();
                var condition = ParseParenthesizedCondition();
                var then = ParseStatement();
                var @else = Accept(TokenKind.ElseKeyword) ? ParseStatement() : null;
                return new IfStatementSyntax(From(start), condition, then, @else);
            }

            case TokenKind.WhileKeyword:
            {
                Advance();
                var condition = ParseParenthesizedCondition();
                var body = ParseStatement();
                return new WhileStatementSyntax(From(start), condition, body);
            }

            case TokenKind.DoKeyword:
            {
                Advance();
                var body = ParseStatement();
                Expect(TokenKind.WhileKeyword);
                var condition = ParseParenthesizedCondition();
                Expect(TokenKind.Semicolon);
                return new DoStatementSyntax(From(start), body, condition);
            }

            case TokenKind.ForKeyword:
                return ParseFor();
            case TokenKind.ReturnKeyword:
            {
                Advance();
                var expression = Kind == TokenKind.Semicolon ? null : ParseExpression();
                Expect(TokenKind.Semicolon);
                return new ReturnStatementSyntax(From(start), expression);
            }

            case TokenKind.BreakKeyword:
            {
                Advance();
                var label = IsName(Kind) ? ParseIdentifier() : null;
                Expect(TokenKind.Semicolon);
                return new BreakStatementSyntax(From(start), label);
            }

            case TokenKind.ContinueKeyword:
            {
                Advance();
                var label = IsName(Kind) ? ParseIdentifier() : null;
                Expect(TokenKind.Semicolon);
                return new ContinueStatementSyntax(From(start), label);
            }

            case TokenKind.ThrowKeyword:
            {
                Advance();
                var expression = ParseExpression();
                Expect(TokenKind.Semicolon);
                return new ThrowStatementSyntax(From(start), expression);
            }

            case TokenKind.TryKeyword:
                return ParseTry();
            case TokenKind.SwitchKeyword:
            {
                Advance();
                var selector = ParseParenthesizedCondition();
                var cases = ParseSwitchBlock();
                return new SwitchStatementSyntax(From(start), selector, cases);
            }

            case TokenKind.SynchronizedKeyword:
            {
                Advance();
                var @lock = ParseParenthesizedCondition();
                var body = ParseBlock();
                return new SynchronizedStatementSyntax(From(start), @lock, body);
            }

            case TokenKind.AssertKeyword:
            {
                Advance();
                var condition = ParseExpression();
                var message = Accept(TokenKind.Colon) ? ParseExpression() : null;
                Expect(TokenKind.Semicolon);
                return new AssertStatementSyntax(From(start), condition, message);
            }

            case TokenKind.ThisKeyword or TokenKind.SuperKeyword when PeekKind(1) == TokenKind.OpenParen:
                return ParseExplicitConstructorInvocation(start, null);
            case TokenKind.LessThan:
            {
                var typeArguments = Speculate(() =>
                {
                    var arguments = ParseTypeArguments();
                    return Kind is TokenKind.ThisKeyword or TokenKind.SuperKeyword && PeekKind(1) == TokenKind.OpenParen ? arguments : null;
                });
                if (typeArguments is not null)
                    return ParseExplicitConstructorInvocation(start, typeArguments);
                break;
            }

            case TokenKind.Identifier when IsYieldStatement():
                return ParseYield();
            case TokenKind.Identifier or TokenKind.Underscore when PeekKind(1) == TokenKind.Colon:
            {
                var label = ParseIdentifier();
                Advance();
                var statement = ParseStatement();
                return new LabeledStatementSyntax(From(start), label, statement);
            }

            case TokenKind.ElseKeyword or TokenKind.CaseKeyword or TokenKind.DefaultKeyword or TokenKind.CatchKeyword or TokenKind.FinallyKeyword:
                ErrorAtCurrent($"Unexpected '{TextOf(Current)}'");
                Advance();
                return new IncompleteStatementSyntax(From(start));
        }

        return ParseExpressionStatement(start);
    }

    private StatementSyntax ParseExpressionStatement(int start)
    {
        var expression = ParseExpression();
        if (expression is MethodInvocationExpressionSyntax { Name.Text: "super", Target: { } qualifier } invocation)
        {
            Expect(TokenKind.Semicolon);
            return new ExplicitConstructorInvocationSyntax(From(start), qualifier, invocation.TypeArguments, isSuper: true, invocation.Arguments);
        }

        if (expression.IsMissing)
        {
            if (Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
                Advance();
            return new IncompleteStatementSyntax(From(start));
        }

        Expect(TokenKind.Semicolon);
        return new ExpressionStatementSyntax(From(start), expression);
    }

    private ExplicitConstructorInvocationSyntax ParseExplicitConstructorInvocation(int start, TypeArgumentListSyntax? typeArguments)
    {
        var isSuper = Advance().Kind == TokenKind.SuperKeyword;
        var arguments = ParseArguments();
        Expect(TokenKind.Semicolon);
        return new ExplicitConstructorInvocationSyntax(From(start), null, typeArguments, isSuper, arguments);
    }

    private ExpressionSyntax ParseParenthesizedCondition()
    {
        Expect(TokenKind.OpenParen);
        var expression = ParseExpression();
        Expect(TokenKind.CloseParen);
        return expression;
    }

    private StatementSyntax ParseFor()
    {
        var start = CurrentStart;
        Advance();
        Expect(TokenKind.OpenParen);

        var header = Speculate(() =>
        {
            var modifiers = ParseModifiers();
            var type = ParseLocalType();
            var name = ParseIdentifier();
            return Kind == TokenKind.Colon && !type.IsMissing ? new ForEachHeader(modifiers, type, name) : null;
        });
        if (header is not null)
        {
            Advance();
            var expression = ParseExpression();
            Expect(TokenKind.CloseParen);
            var body = ParseStatement();
            return new ForEachStatementSyntax(From(start), header.Modifiers, header.Type, header.Name, expression, body);
        }

        var initializers = new List<StatementSyntax>();
        if (Kind != TokenKind.Semicolon)
        {
            var initStart = CurrentStart;
            if (Kind is TokenKind.FinalKeyword or TokenKind.At || (TokenKinds.IsPrimitiveType(Kind) && !IsPrimitiveExpressionStart())
                || (IsName(Kind) && IsLocalVariableDeclarationStart()))
            {
                var modifiers = ParseModifiers();
                initializers.Add(ParseLocalVariableDeclaration(initStart, modifiers, requireSemicolon: false));
            }
            else
            {
                do
                {
                    var expressionStart = CurrentStart;
                    var expression = ParseExpression();
                    initializers.Add(new ExpressionStatementSyntax(From(expressionStart), expression));
                }
                while (Accept(TokenKind.Comma));
            }
        }

        Expect(TokenKind.Semicolon);
        var condition = Kind == TokenKind.Semicolon ? null : ParseExpression();
        Expect(TokenKind.Semicolon);
        var updates = new List<ExpressionSyntax>();
        if (Kind != TokenKind.CloseParen)
        {
            do
            {
                updates.Add(ParseExpression());
            }
            while (Accept(TokenKind.Comma));
        }

        Expect(TokenKind.CloseParen);
        var loopBody = ParseStatement();
        return new ForStatementSyntax(From(start), List(initializers), condition, List(updates), loopBody);
    }

    private TryStatementSyntax ParseTry()
    {
        var start = CurrentStart;
        Advance();
        var resources = new List<SyntaxNode>();
        var hasResources = false;
        if (Accept(TokenKind.OpenParen))
        {
            hasResources = true;
            while (Kind is not (TokenKind.CloseParen or TokenKind.EndOfFile))
            {
                var before = index;
                resources.Add(ParseResource());
                if (!Accept(TokenKind.Semicolon) || index == before)
                    break;
            }

            Expect(TokenKind.CloseParen);
        }

        var body = ParseBlock();
        var catches = new List<CatchClauseSyntax>();
        while (Kind == TokenKind.CatchKeyword)
        {
            var catchStart = CurrentStart;
            Advance();
            Expect(TokenKind.OpenParen);
            var modifiers = ParseModifiers();
            var typeStart = CurrentStart;
            var types = new List<TypeSyntax> { ParseType() };
            while (Accept(TokenKind.Bar))
                types.Add(ParseType());
            var type = types.Count == 1 ? types[0] : new UnionTypeSyntax(From(typeStart), new SyntaxList<TypeSyntax>(types));
            var name = ParseIdentifier();
            Expect(TokenKind.CloseParen);
            var block = ParseBlock();
            catches.Add(new CatchClauseSyntax(From(catchStart), modifiers, type, name, block));
        }

        var @finally = Accept(TokenKind.FinallyKeyword) ? ParseBlock() : null;
        if (!hasResources && catches.Count == 0 && @finally is null)
            Error(new SourceSpan(start, 3), "A try statement needs a catch or a finally block");

        return new TryStatementSyntax(From(start), List(resources), hasResources, body, List(catches), @finally);
    }

    private SyntaxNode ParseResource()
    {
        var start = CurrentStart;
        if (Kind is TokenKind.FinalKeyword or TokenKind.At || (IsName(Kind) && IsLocalVariableDeclarationStart()))
        {
            var modifiers = ParseModifiers();
            return ParseLocalVariableDeclaration(start, modifiers, requireSemicolon: false);
        }

        return ParseExpression();
    }

    private SyntaxList<SwitchCaseSyntax>? ParseSwitchBlock()
    {
        var cases = new List<SwitchCaseSyntax>();
        if (!Expect(TokenKind.OpenBrace))
            return null;

        while (Kind is not (TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            if (Kind is TokenKind.CaseKeyword or TokenKind.DefaultKeyword)
                cases.Add(ParseSwitchCase());
            else
                SkipUnexpected("in a switch; expected 'case' or 'default'");
        }

        Expect(TokenKind.CloseBrace);
        return List(cases);
    }

    private SwitchCaseSyntax ParseSwitchCase()
    {
        var start = CurrentStart;
        var labels = new List<SyntaxNode>();
        if (Kind == TokenKind.DefaultKeyword)
        {
            var token = Advance();
            labels.Add(new DefaultLabelSyntax(SourceSpan.FromBounds(StartOf(token), EndOf(token))));
        }
        else
        {
            Advance();
            do
            {
                labels.Add(ParseCaseLabel());
            }
            while (Accept(TokenKind.Comma));
        }

        ExpressionSyntax? guard = null;
        if (IsContextual("when"))
        {
            Advance();
            guard = ParseConditional();
        }

        if (Accept(TokenKind.Arrow))
        {
            SyntaxNode body;
            if (Kind == TokenKind.OpenBrace)
            {
                body = ParseBlock();
            }
            else if (Kind == TokenKind.ThrowKeyword)
            {
                body = ParseStatement();
            }
            else
            {
                body = ParseExpression();
                Expect(TokenKind.Semicolon);
            }

            return new SwitchCaseSyntax(From(start), List(labels), guard, isArrow: true, null, body);
        }

        if (!Accept(TokenKind.Colon))
            ErrorExpected("':' or '->'");

        var statements = new List<StatementSyntax>();
        while (Kind is not (TokenKind.CaseKeyword or TokenKind.DefaultKeyword or TokenKind.CloseBrace or TokenKind.EndOfFile))
        {
            var before = index;
            statements.Add(ParseBlockStatement());
            if (index == before)
            {
                statements.RemoveAt(statements.Count - 1);
                SkipUnexpected("in a switch case");
            }
        }

        return new SwitchCaseSyntax(From(start), List(labels), guard, isArrow: false, List(statements), null);
    }

    private SyntaxNode ParseCaseLabel()
    {
        if (Kind == TokenKind.DefaultKeyword)
        {
            var token = Advance();
            return new DefaultLabelSyntax(SourceSpan.FromBounds(StartOf(token), EndOf(token)));
        }

        if (IsPatternStart())
            return ParsePattern();

        // The arrow after a case constant ends the label; it does not make a lambda.
        var wasInCaseLabel = inCaseLabel;
        inCaseLabel = true;
        var constant = ParseConditional();
        inCaseLabel = wasInCaseLabel;
        return constant;
    }

    // A case label that is not a pattern is a constant.
    private bool IsPatternStart()
    {
        if (Kind == TokenKind.Underscore && PeekKind(1) is TokenKind.Comma or TokenKind.Colon or TokenKind.Arrow or TokenKind.CloseParen)
            return true;

        return LooksLike(() =>
        {
            ParseModifiers();
            return IsPatternType(ParseLocalType());
        });
    }

    private PatternSyntax ParsePattern()
    {
        var start = CurrentStart;
        if (Kind == TokenKind.Underscore && PeekKind(1) is not (TokenKind.Dot or TokenKind.LessThan or TokenKind.OpenParen or TokenKind.OpenBracket))
        {
            var token = Advance();
            return new UnnamedPatternSyntax(SourceSpan.FromBounds(StartOf(token), EndOf(token)));
        }

        var modifiers = ParseModifiers();
        var type = ParseLocalType();
        if (Kind == TokenKind.OpenParen && modifiers is null)
        {
            Advance();
            var subpatterns = new List<PatternSyntax>();
            while (Kind is not (TokenKind.CloseParen or TokenKind.EndOfFile))
            {
                var before = index;
                subpatterns.Add(ParsePattern());
                if (!Accept(TokenKind.Comma) || index == before)
                    break;
            }

            Expect(TokenKind.CloseParen);
            return new RecordPatternSyntax(From(start), type, List(subpatterns));
        }

        var name = ParseIdentifier();
        return new TypePatternSyntax(From(start), modifiers, type, name);
    }

    private sealed record ForEachHeader(ModifiersSyntax? Modifiers, TypeSyntax Type, IdentifierSyntax Name);
}
