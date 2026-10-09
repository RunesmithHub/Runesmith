using System.Text;
using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Semantics;
using Runesmith.Languages.Java.Syntax;
using Runesmith.LanguageServices;
using Runesmith.Text;

namespace Runesmith.Languages.Java.Analysis;

/// <summary>Signature help: the overloads of the method or constructor whose arguments the caret is in, and the argument it is at.</summary>
internal static class JavaSignatureHelp
{
    public static SignatureHelpResult? Get(SemanticModel model, int offset)
    {
        var typer = new ExpressionTyper(model);
        var scope = new ScopeBuilder(typer).ScopeAt(offset, out _);
        var path = model.Tree.GetPath(offset, includeEnd: true);
        for (var i = path.Count - 1; i >= 1; i--)
        {
            if (path[i] is not ArgumentListSyntax arguments || offset <= arguments.Start || (arguments.HasCloseParenthesis && offset >= arguments.End))
                continue;

            var resolution = path[i - 1] switch
            {
                MethodInvocationExpressionSyntax call when call.Arguments == arguments => typer.ResolveCall(call, scope),
                ObjectCreationExpressionSyntax creation when creation.Arguments == arguments => typer.ResolveCreation(creation, scope),
                ExplicitConstructorInvocationSyntax invocation => Constructors(model, scope, invocation.IsSuper),
                _ => null,
            };
            if (resolution is null || resolution.Candidates.Count == 0)
                return null;

            var active = ActiveParameter(model.Tree, arguments.Start, offset);
            var signatures = resolution.Candidates.Select(c => Entry(model, c)).ToList();
            var chosen = resolution.Chosen is { } best ? IndexOf(resolution.Candidates, best) : -1;
            if (chosen < 0 || !Fits(resolution.Candidates[chosen].Member, active))
                chosen = Math.Max(0, IndexWhere(resolution.Candidates, c => Fits(c.Member, active)));
            return new SignatureHelpResult(signatures, chosen, active);
        }

        return null;
    }

    private static CallResolution? Constructors(SemanticModel model, Scope scope, bool isSuper)
    {
        if (scope.EnclosingType()?.Type is not { } type)
            return null;
        var target = isSuper ? type.SuperClass : SemanticModel.SelfType(type);
        return target is null ? null : new CallResolution([.. model.Constructors(target)], null, false, true);
    }

    private static bool Fits(MethodSymbol method, int active) => active < method.ParameterTypes.Count || (method.IsVarargs && method.ParameterTypes.Count > 0);

    private static int IndexOf(IReadOnlyList<MemberRef<MethodSymbol>> list, MemberRef<MethodSymbol> item)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i].Member, item.Member))
                return i;
        }

        return -1;
    }

    private static int IndexWhere(IReadOnlyList<MemberRef<MethodSymbol>> list, Func<MemberRef<MethodSymbol>, bool> predicate)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (predicate(list[i]))
                return i;
        }

        return -1;
    }

    // Counts the commas between the opening parenthesis and the caret that are not nested in other brackets.
    private static int ActiveParameter(JavaSyntaxTree tree, int open, int offset)
    {
        var depth = 0;
        var commas = 0;
        for (var i = tree.FindTokenIndex(open) + 1; i < tree.Tokens.Length && tree.Tokens[i].Span.Start < offset; i++)
        {
            switch (tree.Tokens[i].Kind)
            {
                case TokenKind.OpenParen or TokenKind.OpenBracket or TokenKind.OpenBrace:
                    depth++;
                    break;
                case TokenKind.CloseParen or TokenKind.CloseBracket or TokenKind.CloseBrace:
                    depth--;
                    break;
                case TokenKind.Comma when depth == 0:
                    commas++;
                    break;
            }
        }

        return commas;
    }

    private static SignatureEntry Entry(SemanticModel model, MemberRef<MethodSymbol> method)
    {
        var member = method.Member;
        var label = new StringBuilder();
        if (!member.IsConstructor)
            label.Append(JavaTypes.Display(method.Substitute(member.ReturnType))).Append(' ');
        label.Append(member.IsConstructor ? method.Owner.SimpleName : member.Name).Append('(');
        var spans = new List<TextSpan>();
        var first = true;
        foreach (var parameter in JavaSymbols.Parameters(method))
        {
            if (!first)
                label.Append(", ");
            first = false;
            spans.Add(new TextSpan(label.Length, parameter.Length));
            label.Append(parameter);
        }

        label.Append(')');
        return new SignatureEntry(label.ToString(), spans, JavaSymbols.Documentation(model, method));
    }
}
