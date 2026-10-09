namespace Runesmith.Languages.Java.Syntax;

internal sealed partial class Parser
{
    /// <summary>Reads a type with its annotations and brackets, or reports a missing one.</summary>
    private TypeSyntax ParseType()
    {
        var start = CurrentStart;
        var annotations = ParseAnnotations();
        TypeSyntax type;
        if (TokenKinds.IsPrimitiveType(Kind))
        {
            type = ParsePrimitiveType(start, annotations);
        }
        else if (IsName(Kind))
        {
            type = ParseClassType(start, annotations);
        }
        else
        {
            ErrorExpected("a type");
            return new MissingTypeSyntax(lastEnd);
        }

        return WithDimensions(start, type);
    }

    private PrimitiveTypeSyntax ParsePrimitiveType(int start, SyntaxList<AnnotationSyntax>? annotations)
    {
        var kind = Advance().Kind switch
        {
            TokenKind.BooleanKeyword => PrimitiveTypeKind.Boolean,
            TokenKind.ByteKeyword => PrimitiveTypeKind.Byte,
            TokenKind.ShortKeyword => PrimitiveTypeKind.Short,
            TokenKind.CharKeyword => PrimitiveTypeKind.Char,
            TokenKind.IntKeyword => PrimitiveTypeKind.Int,
            TokenKind.LongKeyword => PrimitiveTypeKind.Long,
            TokenKind.FloatKeyword => PrimitiveTypeKind.Float,
            TokenKind.DoubleKeyword => PrimitiveTypeKind.Double,
            _ => PrimitiveTypeKind.Void,
        };
        return new PrimitiveTypeSyntax(From(start), kind, annotations);
    }

    private ClassTypeSyntax ParseClassType(int start, SyntaxList<AnnotationSyntax>? annotations)
    {
        ClassTypeSyntax? type = null;
        while (true)
        {
            var name = ParseIdentifier();
            var typeArguments = Kind == TokenKind.LessThan ? ParseTypeArguments() : null;
            type = new ClassTypeSyntax(From(start), type, annotations, name, typeArguments);
            if (Kind != TokenKind.Dot || !(IsName(PeekKind(1)) || PeekKind(1) == TokenKind.At))
                return type;

            Advance();
            annotations = ParseAnnotations();
            if (!IsName(Kind))
                return type;
        }
    }

    private TypeArgumentListSyntax ParseTypeArguments()
    {
        var start = CurrentStart;
        Advance();
        var arguments = new List<TypeSyntax>();
        if (Kind != TokenKind.GreaterThan)
        {
            do
            {
                arguments.Add(ParseTypeArgument());
            }
            while (Accept(TokenKind.Comma));
        }

        Expect(TokenKind.GreaterThan);
        return new TypeArgumentListSyntax(From(start), List(arguments));
    }

    private TypeSyntax ParseTypeArgument()
    {
        var start = CurrentStart;
        var annotations = ParseAnnotations();
        if (!Accept(TokenKind.Question))
        {
            if (annotations is null)
                return ParseType();

            var type = TokenKinds.IsPrimitiveType(Kind) ? (TypeSyntax)ParsePrimitiveType(start, annotations) : ParseClassType(start, annotations);
            return WithDimensions(start, type);
        }

        var boundKind = WildcardBoundKind.None;
        TypeSyntax? bound = null;
        if (Accept(TokenKind.ExtendsKeyword))
        {
            boundKind = WildcardBoundKind.Extends;
            bound = ParseType();
        }
        else if (Accept(TokenKind.SuperKeyword))
        {
            boundKind = WildcardBoundKind.Super;
            bound = ParseType();
        }

        return new WildcardTypeSyntax(From(start), annotations, boundKind, bound);
    }

    private TypeSyntax WithDimensions(int start, TypeSyntax type)
    {
        var dimensions = ParseDimensions();
        return dimensions is null ? type : new ArrayTypeSyntax(From(start), type, dimensions);
    }

    /// <summary>Reads empty brackets, each with its annotations: <c>@A [] []</c>.</summary>
    private SyntaxList<DimensionSyntax>? ParseDimensions()
    {
        List<DimensionSyntax>? dimensions = null;
        while (true)
        {
            if (Kind == TokenKind.OpenBracket && PeekKind(1) == TokenKind.CloseBracket)
            {
                var start = CurrentStart;
                Advance();
                Advance();
                (dimensions ??= []).Add(new DimensionSyntax(From(start), null, null));
            }
            else if (Kind == TokenKind.At && tokens[SkipAnnotations(index)].Kind == TokenKind.OpenBracket)
            {
                var start = CurrentStart;
                var annotations = ParseAnnotations();
                Expect(TokenKind.OpenBracket);
                Expect(TokenKind.CloseBracket);
                (dimensions ??= []).Add(new DimensionSyntax(From(start), annotations, null));
            }
            else
            {
                return List(dimensions);
            }
        }
    }

    /// <summary>Reads the type of a local variable, pattern or lambda parameter, where <c>var</c> means an inferred type.</summary>
    private TypeSyntax ParseLocalType()
    {
        if (IsContextual("var") && IsName(PeekKind(1)))
        {
            var token = Advance();
            return new VarTypeSyntax(SourceSpan.FromBounds(StartOf(token), EndOf(token)));
        }

        return ParseType();
    }
}
