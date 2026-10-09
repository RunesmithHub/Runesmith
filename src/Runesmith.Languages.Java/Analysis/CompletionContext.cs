using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>What is being completed.</summary>
internal enum CompletionContextKind
{
    /// <summary>Nothing: a comment, a literal, or the name of a new declaration.</summary>
    None,

    /// <summary>A member after <c>.</c>: of a value, a type or a package.</summary>
    MemberAccess,

    /// <summary>A package, type or static member in an import.</summary>
    Import,

    /// <summary>A module in <c>import module</c>.</summary>
    ModuleImport,

    /// <summary>A package in a package declaration.</summary>
    Package,

    /// <summary>An annotation after <c>@</c>.</summary>
    Annotation,

    /// <summary>The start of a statement.</summary>
    Statement,

    /// <summary>The start of a member of a class body.</summary>
    ClassMember,

    /// <summary>The top level of a file.</summary>
    TopLevel,

    /// <summary>An expression.</summary>
    Expression,

    /// <summary>A type, such as after <c>extends</c> or in type arguments.</summary>
    Type,

    /// <summary>The type after <c>new</c>.</summary>
    NewType,

    /// <summary>After a type declaration's name: <c>extends</c>, <c>implements</c>, <c>permits</c>.</summary>
    TypeHeader,
}

/// <summary>What is being completed at a position, and the syntax it depends on.</summary>
/// <param name="Dot">The <c>.</c> before the word, for member access.</param>
/// <param name="Qualifier">The dotted name before the word in an import or package declaration, without its last dot.</param>
/// <param name="IsStaticImport">Whether an import is <c>import static</c>.</param>
internal sealed record CompletionContext(CompletionContextKind Kind, JavaToken? Dot = null, string Qualifier = "", bool IsStaticImport = false)
{
    public static CompletionContext None { get; } = new(CompletionContextKind.None);

    /// <summary>Finds what is being completed for a word that starts at an offset.</summary>
    public static CompletionContext Find(JavaSyntaxTree tree, int wordStart)
    {
        if (IsInCommentOrLiteral(tree, wordStart))
            return None;

        var previous = tree.FindTokenBefore(wordStart);
        if (previous is { Kind: TokenKind.Dot } dot && ImportOrPackage(tree, wordStart) is null)
            return new CompletionContext(CompletionContextKind.MemberAccess, dot);
        if (ImportOrPackage(tree, wordStart) is { } header)
            return header;
        if (previous is not { } before)
            return new CompletionContext(CompletionContextKind.TopLevel);

        switch (before.Kind)
        {
            case TokenKind.ColonColon:
                return None;
            case TokenKind.At:
                return new CompletionContext(CompletionContextKind.Annotation);
            case TokenKind.NewKeyword:
                return new CompletionContext(CompletionContextKind.NewType);
            case TokenKind.ExtendsKeyword or TokenKind.ImplementsKeyword or TokenKind.ThrowsKeyword or TokenKind.InstanceofKeyword:
                return new CompletionContext(CompletionContextKind.Type);
            case TokenKind.Identifier when Text(tree, before) == "permits":
                return new CompletionContext(CompletionContextKind.Type);
        }

        var path = tree.GetPath(wordStart);
        if (HeaderOf(path, wordStart) is { } header2)
            return before.Kind is TokenKind.Comma or TokenKind.Ampersand ? new CompletionContext(CompletionContextKind.Type) : header2;
        if (IsDeclarationName(path, wordStart))
            return None;
        if (before.Kind is TokenKind.LessThan or TokenKind.Comma or TokenKind.Question && path.Any(n => n is TypeArgumentListSyntax or TypeParameterListSyntax))
            return new CompletionContext(CompletionContextKind.Type);
        if (before.Kind is TokenKind.OpenParen or TokenKind.Comma && path.LastOrDefault(n => n is ParameterListSyntax or ArgumentListSyntax or CatchClauseSyntax) is ParameterListSyntax or CatchClauseSyntax)
            return new CompletionContext(CompletionContextKind.Type);

        if (IsStatementBoundary(tree, before, path, wordStart) || IsModifier(tree, before))
        {
            return Container(path) switch
            {
                BlockSyntax or SwitchCaseSyntax => new CompletionContext(CompletionContextKind.Statement),
                ClassBodySyntax => new CompletionContext(CompletionContextKind.ClassMember),
                CompilationUnitSyntax => new CompletionContext(CompletionContextKind.TopLevel),
                _ => new CompletionContext(CompletionContextKind.Expression),
            };
        }

        return new CompletionContext(CompletionContextKind.Expression);
    }

    private static string Text(JavaSyntaxTree tree, JavaToken token) => tree.Text.Substring(token.Span.Start, token.Span.Length);

    private static bool IsInCommentOrLiteral(JavaSyntaxTree tree, int offset)
    {
        if (offset > 0 && tree.FindComment(offset - 1) is { } comment && comment.Span.Start < offset
            && (offset < comment.Span.End || comment.Kind is CommentKind.Line or CommentKind.MarkdownLine))
        {
            return true;
        }

        var token = tree.FindToken(Math.Max(0, offset - 1));
        return token.Kind is TokenKind.StringLiteral or TokenKind.CharacterLiteral or TokenKind.TextBlock && token.Span.Start < offset
            && (offset < token.Span.End || !IsTerminated(tree, token));
    }

    private static bool IsTerminated(JavaSyntaxTree tree, JavaToken token)
    {
        var text = tree.Text.AsSpan(token.Span.Start, token.Span.Length);
        return token.Kind switch
        {
            TokenKind.TextBlock => text.Length >= 6 && text.EndsWith("\"\"\""),
            TokenKind.CharacterLiteral => text.Length >= 2 && text[^1] == '\'',
            _ => text.Length >= 2 && text[^1] == '"',
        };
    }

    // Reads back over a dotted name, the word excluded, to an import or package keyword: import [static | module] a.b.
    private static CompletionContext? ImportOrPackage(JavaSyntaxTree tree, int wordStart)
    {
        var index = tree.FindTokenIndex(wordStart);
        if (tree.Tokens[index].Span.End > wordStart || tree.Tokens[index].Kind == TokenKind.EndOfFile)
            index--;

        var parts = new List<string>();
        while (index >= 1 && tree.Tokens[index].Kind == TokenKind.Dot && tree.Tokens[index - 1].Kind == TokenKind.Identifier)
        {
            parts.Add(Text(tree, tree.Tokens[index - 1]));
            index -= 2;
        }

        if (index < 0)
            return null;

        parts.Reverse();
        var qualifier = string.Join('.', parts);
        var afterImport = index > 0 && tree.Tokens[index - 1].Kind == TokenKind.ImportKeyword;
        return tree.Tokens[index].Kind switch
        {
            TokenKind.ImportKeyword => new CompletionContext(CompletionContextKind.Import, Qualifier: qualifier),
            TokenKind.PackageKeyword => new CompletionContext(CompletionContextKind.Package, Qualifier: qualifier),
            TokenKind.StaticKeyword when afterImport => new CompletionContext(CompletionContextKind.Import, Qualifier: qualifier, IsStaticImport: true),
            TokenKind.Identifier when afterImport && Text(tree, tree.Tokens[index]) == "module" =>
                new CompletionContext(CompletionContextKind.ModuleImport, Qualifier: qualifier),
            _ => null,
        };
    }

    private static CompletionContext? HeaderOf(IReadOnlyList<SyntaxNode> path, int wordStart)
    {
        for (var i = path.Count - 1; i >= 0; i--)
        {
            if (path[i] is TypeDeclarationSyntax type)
            {
                return wordStart > type.Name.End && wordStart <= type.Body.Start && !type.Name.IsMissing
                    ? new CompletionContext(CompletionContextKind.TypeHeader)
                    : null;
            }

            if (path[i] is ClassBodySyntax or BlockSyntax)
                return null;
        }

        return null;
    }

    // The name of a variable, method or parameter being declared, where nothing exists yet to complete.
    private static bool IsDeclarationName(IReadOnlyList<SyntaxNode> path, int wordStart)
    {
        var name = path.Count > 0 ? path[^1] as IdentifierSyntax : null;
        if (name is null || name.Start != wordStart || path.Count < 2)
            return false;

        return path[^2] switch
        {
            VariableDeclaratorSyntax variable => variable.Name == name,
            MethodDeclarationSyntax method => method.Name == name,
            ParameterSyntax parameter => parameter.Name == name && parameter.Type is not null,
            TypeDeclarationSyntax type => type.Name == name,
            _ => false,
        };
    }

    private static bool IsStatementBoundary(JavaSyntaxTree tree, JavaToken before, IReadOnlyList<SyntaxNode> path, int wordStart)
    {
        switch (before.Kind)
        {
            case TokenKind.Semicolon or TokenKind.OpenBrace or TokenKind.CloseBrace or TokenKind.ElseKeyword or TokenKind.DoKeyword:
                return true;
            case TokenKind.Arrow:
                return path.Any(n => n is SwitchCaseSyntax { IsArrow: true }) && !path.Any(n => n is LambdaExpressionSyntax lambda && lambda.Start < before.Span.Start);
            case TokenKind.Colon:
                return !path.Any(n => n is ConditionalExpressionSyntax conditional && conditional.Span.Contains(wordStart));
            case TokenKind.CloseParen:
                for (var i = path.Count - 1; i >= 0; i--)
                {
                    var body = path[i] switch
                    {
                        IfStatementSyntax ifStatement => ifStatement.Then,
                        WhileStatementSyntax whileStatement => whileStatement.Body,
                        ForStatementSyntax forStatement => forStatement.Body,
                        ForEachStatementSyntax forEach => forEach.Body,
                        _ => null,
                    };
                    if (body is not null)
                        return body.Start >= before.Span.End && tree.Text.AsSpan(before.Span.End, wordStart - before.Span.End).IsWhiteSpace();
                }

                return false;
            default:
                return false;
        }
    }

    private static bool IsModifier(JavaSyntaxTree tree, JavaToken token) => token.Kind switch
    {
        TokenKind.PublicKeyword or TokenKind.ProtectedKeyword or TokenKind.PrivateKeyword or TokenKind.StaticKeyword or TokenKind.FinalKeyword
            or TokenKind.AbstractKeyword or TokenKind.NativeKeyword or TokenKind.TransientKeyword or TokenKind.VolatileKeyword
            or TokenKind.StrictfpKeyword => true,
        TokenKind.Identifier => Text(tree, token) is "sealed",
        _ => false,
    };

    private static SyntaxNode? Container(IReadOnlyList<SyntaxNode> path)
    {
        for (var i = path.Count - 1; i >= 0; i--)
        {
            switch (path[i])
            {
                case BlockSyntax or SwitchCaseSyntax or ClassBodySyntax or CompilationUnitSyntax:
                    return path[i];
                case ExpressionSyntax and not (NameExpressionSyntax or FieldAccessExpressionSyntax or MethodInvocationExpressionSyntax):
                    return path[i];
            }
        }

        return null;
    }
}
