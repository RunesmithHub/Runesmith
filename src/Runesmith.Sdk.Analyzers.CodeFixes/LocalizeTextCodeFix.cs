using System.Collections.Immutable;
using System.Composition;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Simplification;
using Microsoft.CodeAnalysis.Text;
using Runesmith.Sdk.Analyzers.Fluent;
using Runesmith.Sdk.Analyzers.Strings;

namespace Runesmith.Sdk.Analyzers.CodeFixes;

/// <summary>Moves text users see into the project's English Fluent file as a new message, and puts the generated <c>Strings</c> member in
/// its place: <c>Text = "Save all"</c> becomes <c>Text = Strings.SaveAll</c>, and <c>$"Opened {path}"</c> becomes
/// <c>Strings.OpenedPath(path)</c> with the message <c>opened-path = Opened { $path }</c>.</summary>
/// <remarks>Offered when the project has a Fluent file in <c>locales/en/</c>. An interpolated string is moved when each of its holes is
/// text, a number or a date, without an alignment or a format.</remarks>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LocalizeTextCodeFix))]
[Shared]
public sealed class LocalizeTextCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ImmutableArray.Create(Descriptors.NotLocalized.Id);

    // Each fix adds its own message to the same file, so the fixes can't be applied all at once.
    public override FixAllProvider? GetFixAllProvider() => null;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var source = EnglishSource(context.Document.Project);
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        var model = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);
        if (source is null || root is null || model is null)
            return;

        var node = root.FindNode(context.Span, getInnermostNodeForTie: true);
        var text = node.DescendantNodesAndSelf().FirstOrDefault(n => n is InterpolatedStringExpressionSyntax || n.IsKind(SyntaxKind.StringLiteralExpression));
        if (text is not ExpressionSyntax expression || Describe(expression, model, context.CancellationToken) is not { } message)
            return;

        context.RegisterCodeFix(
            CodeAction.Create(
                $"Move the text into {Path.GetFileName(source.FilePath)}",
                cancellationToken => MoveAsync(context.Document, source, expression, message, cancellationToken),
                nameof(LocalizeTextCodeFix)),
            context.Diagnostics);
    }

    /// <summary>Gets the English Fluent file new messages go into: the plugin's own file in <c>locales/en/</c>, the first by name.</summary>
    private static TextDocument? EnglishSource(Project project) =>
        project.AdditionalDocuments
            .Where(d => d.FilePath is { } path && path.EndsWith(".ftl", StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "en", StringComparison.Ordinal)
                && string.Equals(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))), "locales", StringComparison.Ordinal))
            .OrderBy(d => d.FilePath, StringComparer.Ordinal)
            .FirstOrDefault();

    private static Message? Describe(ExpressionSyntax expression, SemanticModel model, CancellationToken cancellationToken)
    {
        if (expression is LiteralExpressionSyntax literal)
            return new Message(literal.Token.ValueText, FluentWriter.Escape(literal.Token.ValueText), []);

        var interpolated = (InterpolatedStringExpressionSyntax)expression;
        var words = new StringBuilder();
        var pattern = new StringBuilder();
        var arguments = new List<(string Variable, ExpressionSyntax Value)>();
        for (var i = 0; i < interpolated.Contents.Count; i++)
        {
            switch (interpolated.Contents[i])
            {
                case InterpolatedStringTextSyntax part:
                    var value = part.TextToken.ValueText;
                    words.Append(value);
                    pattern.Append(FluentWriter.Escape(value, atStart: i == 0, atEnd: i == interpolated.Contents.Count - 1));
                    break;
                case InterpolationSyntax { AlignmentClause: null, FormatClause: null } hole:
                    var type = model.GetTypeInfo(hole.Expression, cancellationToken).Type;
                    var function = type?.SpecialType switch
                    {
                        SpecialType.System_String => "",
                        SpecialType.System_Byte or SpecialType.System_SByte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32
                            or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single
                            or SpecialType.System_Double => "NUMBER",
                        SpecialType.System_DateTime => "DATETIME",
                        _ when type?.ToDisplayString() == "System.DateTimeOffset" => "DATETIME",
                        _ => null,
                    };
                    if (function is null)
                        return null;

                    var variable = VariableName(hole.Expression, arguments.Select(a => a.Variable));
                    words.Append(' ').Append(variable).Append(' ');
                    pattern.Append(function.Length == 0 ? $"{{ ${variable} }}" : $"{{ {function}(${variable}) }}");
                    arguments.Add((variable, hole.Expression));
                    break;
                default:
                    return null;
            }
        }

        return new Message(words.ToString(), pattern.ToString(), arguments);
    }

    private static string VariableName(ExpressionSyntax expression, IEnumerable<string> taken)
    {
        var name = expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => "value",
        };
        if (name.Length == 0 || !FluentParser.IsIdentifier(name))
            name = "value";
        name = char.ToLowerInvariant(name[0]) + name.Substring(1);
        var candidate = name;
        for (var number = 2; taken.Contains(candidate); number++)
            candidate = name + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return candidate;
    }

    private static async Task<Solution> MoveAsync(Document document, TextDocument source, ExpressionSyntax expression, Message message, CancellationToken cancellationToken)
    {
        var fluent = (await source.GetTextAsync(cancellationToken).ConfigureAwait(false)).ToString();
        var existing = FluentParser.Parse(fluent).Messages.ToList();
        var ids = new HashSet<string>(existing.Select(m => m.Id), StringComparer.Ordinal);
        var members = new HashSet<string>(existing.Select(m => Naming.Member(m.Id)), StringComparer.Ordinal) { StringsGenerator.ClassName };
        var id = FluentWriter.Id(message.Words, candidate => ids.Contains(candidate) || members.Contains(Naming.Member(candidate)));

        var separator = fluent.Length == 0 || fluent.EndsWith("\n", StringComparison.Ordinal) ? "" : "\n";
        var updated = fluent + separator + FluentWriter.Message(id, message.Pattern) + "\n";
        var solution = document.Project.Solution.WithAdditionalDocumentText(source.Id, SourceText.From(updated, Encoding.UTF8));

        var options = document.Project.AnalyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions;
        var space = options.TryGetValue(StringsGenerator.NamespaceProperty, out var configured) && configured.Trim().Length > 0
            ? configured.Trim()
            : document.Project.DefaultNamespace ?? document.Project.AssemblyName;
        var member = $"{(space.Length > 0 ? "global::" + space + "." : "")}{StringsGenerator.ClassName}.{Naming.Member(id)}";
        var target = SyntaxFactory.ParseExpression(member).WithAdditionalAnnotations(Simplifier.Annotation);
        ExpressionSyntax replacement = message.Arguments.Count == 0
            ? target
            : SyntaxFactory.InvocationExpression(target, SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(message.Arguments.Select(a => SyntaxFactory.Argument(a.Value.WithoutTrivia())))));
        replacement = replacement.WithTriviaFrom(expression).WithAdditionalAnnotations(Formatter.Annotation);

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        return root is null ? solution : solution.WithDocumentSyntaxRoot(document.Id, root.ReplaceNode(expression, replacement));
    }

    private sealed class Message(string words, string pattern, IReadOnlyList<(string Variable, ExpressionSyntax Value)> arguments)
    {
        /// <summary>Gets the text the id is made from.</summary>
        public string Words { get; } = words;

        public string Pattern { get; } = pattern;

        public IReadOnlyList<(string Variable, ExpressionSyntax Value)> Arguments { get; } = arguments;
    }
}
