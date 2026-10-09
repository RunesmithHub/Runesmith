namespace Runesmith.Languages.Java.Syntax;

/// <summary>Reparses only the method, constructor or initializer body that holds an edit, and splices the result into the old tree.</summary>
internal static class IncrementalParser
{
    /// <summary>Gets the new tree, or null when the edit needs a full parse.</summary>
    public static JavaSyntaxTree? TryReparse(JavaSyntaxTree tree, string newText, IReadOnlyList<TextEdit> edits)
    {
        if (edits.Count == 0 || !tree.CanReparseIncrementally)
            return null;

        var editStart = edits[0].Start;
        var editEnd = edits[^1].End;
        var delta = 0;
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            if (edit.Start < 0 || edit.End > tree.Text.Length || (i > 0 && edit.Start < edits[i - 1].End))
                return null;
            delta += edit.NewText.Length - edit.Length;
        }

        if (newText.Length != tree.Text.Length + delta)
            return null;

        // An edit can make a Unicode escape where there was none; that changes how everything after it reads.
        var checkStart = Math.Max(0, editStart - 1);
        var checkEnd = Math.Min(newText.Length, editEnd + delta + 1);
        if (newText.AsSpan(checkStart, checkEnd - checkStart).Contains("\\u", StringComparison.Ordinal))
            return null;

        if (FindBody(tree.Root, editStart, editEnd) is not var (oldBody, owner))
            return null;

        var bodyStart = oldBody.Start;
        var newBodyEnd = oldBody.End + delta;
        if (Relex(newText, bodyStart, newBodyEnd) is not var (tokens, comments, lexerDiagnostics))
            return null;

        var parserDiagnostics = new List<SyntaxDiagnostic>();
        var parser = new Parser(newText, null, tokens, parserDiagnostics);
        var newBody = parser.ParseBlock();
        if (parser.Index != tokens.Count - 1 || newBody.End != newBodyEnd || !newBody.HasCloseBrace)
            return null;

        var newOwner = (MemberDeclarationSyntax)owner.Replace(oldBody, newBody);
        var root = (CompilationUnitSyntax)tree.Root.Replace(oldBody, newBody);

        var diagnostics = new List<SyntaxDiagnostic>(tree.Diagnostics.Length + 4);
        foreach (var diagnostic in tree.Diagnostics)
        {
            var span = diagnostic.Span;
            if (span.End <= bodyStart)
                diagnostics.Add(diagnostic);
            else if (span.Start >= oldBody.End)
                diagnostics.Add(diagnostic with { Span = new SourceSpan(span.Start + delta, span.Length) });
            else if (span.Start <= bodyStart && span.End >= oldBody.End)
                diagnostics.Add(diagnostic with { Span = new SourceSpan(span.Start, span.Length + delta) });
            else if (span.Start < bodyStart || span.End > oldBody.End)
                return null;
        }

        diagnostics.AddRange(lexerDiagnostics);
        diagnostics.AddRange(parserDiagnostics);
        new VersionChecker(tree.Options.Version, diagnostics).CheckBody(newBody, newOwner);
        var diagnosticArray = diagnostics.ToArray();
        Array.Sort(diagnosticArray, SyntaxDiagnostic.Compare);

        tokens.RemoveAt(tokens.Count - 1);
        var newTokens = Splice(tree.Tokens.AsSpan(), tokens, bodyStart, oldBody.End, delta, static t => t.Span, static (t, s) => t with { Span = s });
        var newComments = Splice(tree.Comments.AsSpan(), comments, bodyStart, oldBody.End, delta, static c => c.Span, static (c, s) => c with { Span = s });
        return JavaSyntaxTree.Create(newText, tree.Options, root, newTokens, newComments, diagnosticArray);
    }

    /// <summary>Finds the deepest member body that holds the edited range strictly between its braces.</summary>
    private static (BlockSyntax Body, MemberDeclarationSyntax Owner)? FindBody(SyntaxNode root, int start, int end)
    {
        (BlockSyntax, MemberDeclarationSyntax)? found = null;
        var node = root;
        while (true)
        {
            var body = node switch
            {
                MethodDeclarationSyntax method => method.Body,
                ConstructorDeclarationSyntax constructor => constructor.Body,
                CompactConstructorDeclarationSyntax compact => compact.Body,
                InitializerDeclarationSyntax initializer => initializer.Body,
                _ => null,
            };
            if (body is { HasCloseBrace: true } && body.Start < start && end < body.End)
                found = (body, (MemberDeclarationSyntax)node);

            SyntaxNode? next = null;
            foreach (var child in node.ChildNodes())
            {
                if (child.Start <= start && end <= child.End)
                {
                    next = child;
                    break;
                }

                if (child.Start > start)
                    break;
            }

            if (next is null)
                return found;
            node = next;
        }
    }

    /// <summary>Lexes the new body, which must be one balanced pair of braces from <paramref name="start"/> to <paramref name="end"/>.</summary>
    private static (List<JavaToken> Tokens, List<JavaComment> Comments, List<SyntaxDiagnostic> Diagnostics)? Relex(string text, int start, int end)
    {
        var comments = new List<JavaComment>();
        var diagnostics = new List<SyntaxDiagnostic>();
        var lexerEnd = text.Length > 0 && text[^1] == '\u001a' ? text.Length - 1 : text.Length;
        var lexer = new Lexer(text, start, lexerEnd, comments, diagnostics);
        var tokens = new List<JavaToken>(Math.Max(16, (end - start) / 4));
        var depth = 0;
        while (true)
        {
            var token = lexer.Next();
            if (token.Kind == TokenKind.EndOfFile || token.Span.End > end || (tokens.Count == 0 && token.Kind != TokenKind.OpenBrace))
                return null;

            tokens.Add(token);
            if (token.Kind == TokenKind.OpenBrace)
            {
                depth++;
            }
            else if (token.Kind == TokenKind.CloseBrace && --depth == 0)
            {
                if (token.Span.End != end)
                    return null;
                break;
            }
        }

        if (comments.Count > 0 && comments[^1].Span.End > end)
            return null;

        tokens.Add(new JavaToken(TokenKind.EndOfFile, new SourceSpan(end, 0)));
        return (tokens, comments, diagnostics);
    }

    /// <summary>Puts new items in place of the old ones inside a range, moving the ones after it.</summary>
    private static T[] Splice<T>(ReadOnlySpan<T> old, List<T> replacement, int start, int end, int delta, Func<T, SourceSpan> spanOf,
        Func<T, SourceSpan, T> withSpan)
    {
        var first = 0;
        while (first < old.Length && spanOf(old[first]).Start < start)
            first++;
        var after = first;
        while (after < old.Length && spanOf(old[after]).Start < end)
            after++;

        var result = new T[first + replacement.Count + (old.Length - after)];
        old[..first].CopyTo(result);
        replacement.CopyTo(result, first);
        var target = first + replacement.Count;
        for (var i = after; i < old.Length; i++)
        {
            var span = spanOf(old[i]);
            result[target++] = withSpan(old[i], new SourceSpan(span.Start + delta, span.Length));
        }

        return result;
    }
}
