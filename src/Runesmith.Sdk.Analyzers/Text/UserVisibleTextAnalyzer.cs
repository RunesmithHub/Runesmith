using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Runesmith.Sdk.Analyzers.Text;

/// <summary>Flags text users see that is written in the code, in projects that set <c>RunesmithLocalize</c> to true: string literals
/// given to <c>Text</c>, <c>Content</c>, <c>Title</c>, <c>Header</c>, <c>Watermark</c> and <c>PlaceholderText</c> of controls, to
/// <c>ToolTip.SetTip</c>, to the titles and messages of <c>INotificationService</c>, to the labels of <c>TreeItem</c> and <c>PickItem</c>,
/// and to the titles of <c>CommandDefinition</c>.</summary>
/// <remarks>Text without letters, such as a separator or a number, is left alone.</remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UserVisibleTextAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The MSBuild property that turns the check on.</summary>
    public const string EnabledProperty = "build_property.RunesmithLocalize";

    private static readonly ImmutableHashSet<string> ControlProperties = ImmutableHashSet.Create(StringComparer.Ordinal, "Text", "Content", "Title", "Header", "Watermark", "PlaceholderText");
    private static readonly ImmutableHashSet<string> NotificationParameters = ImmutableHashSet.Create(StringComparer.Ordinal, "title", "message");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = ImmutableArray.Create(Descriptors.NotLocalized);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(start =>
        {
            if (!start.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(EnabledProperty, out var enabled)
                || !string.Equals(enabled.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                return;

            var compilation = start.Compilation;
            var symbols = new Symbols(
                compilation.GetTypeByMetadataName("Avalonia.AvaloniaObject"),
                compilation.GetTypeByMetadataName("Avalonia.Controls.ToolTip"),
                compilation.GetTypeByMetadataName("Runesmith.Sdk.Shell.INotificationService"),
                compilation.GetTypeByMetadataName("Runesmith.Sdk.Commands.CommandDefinition"),
                compilation.GetTypeByMetadataName("Runesmith.Sdk.ToolWindows.TreeItem"),
                compilation.GetTypeByMetadataName("Runesmith.Sdk.Shell.PickItem`1"),
                compilation.GetTypeByMetadataName("Runesmith.Sdk.Shell.PickItem"));
            start.RegisterOperationAction(context => AnalyzeAssignment(context, symbols), OperationKind.SimpleAssignment);
            start.RegisterOperationAction(context => AnalyzeInvocation(context, symbols), OperationKind.Invocation);
            start.RegisterOperationAction(context => AnalyzeCreation(context, symbols), OperationKind.ObjectCreation);
        });
    }

    /// <summary>Gets the literal text in an expression given to a user-visible member, if any: a string literal, or the text of an
    /// interpolated string, also in either branch of a conditional or after <c>??</c>.</summary>
    internal static IEnumerable<IOperation> Literals(IOperation? value)
    {
        switch (value)
        {
            case IConversionOperation conversion:
                return Literals(conversion.Operand);
            case IParenthesizedOperation parenthesized:
                return Literals(parenthesized.Operand);
            case IConditionalOperation conditional:
                return Literals(conditional.WhenTrue).Concat(Literals(conditional.WhenFalse));
            case ICoalesceOperation coalesce:
                return Literals(coalesce.WhenNull);
            case ILiteralOperation { ConstantValue: { HasValue: true, Value: string text } } literal when HasWords(text):
                return [literal];
            case IInterpolatedStringOperation interpolated when interpolated.Parts.OfType<IInterpolatedStringTextOperation>()
                .Any(part => part.Text.ConstantValue.Value is string text && HasWords(text)):
                return [interpolated];
            default:
                return [];
        }
    }

    private static bool HasWords(string text) => text.Any(char.IsLetter);

    private static void Report(OperationAnalysisContext context, IOperation? value)
    {
        foreach (var literal in Literals(value))
        {
            var text = literal.Syntax.ToString();
            if (text.Length > 60)
                text = text.Substring(0, 57) + "...";
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.NotLocalized, literal.Syntax.GetLocation(), text.Trim('"', '$', '@')));
        }
    }

    private static void AnalyzeAssignment(OperationAnalysisContext context, Symbols symbols)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (assignment.Target is IPropertyReferenceOperation { Property: var property }
            && ControlProperties.Contains(property.Name)
            && symbols.AvaloniaObject is { } avalonia
            && DerivesFrom(property.ContainingType, avalonia))
            Report(context, assignment.Value);
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, Symbols symbols)
    {
        var invocation = (IInvocationOperation)context.Operation;
        var method = invocation.TargetMethod;
        if (method.Name == "SetTip" && SymbolEqualityComparer.Default.Equals(method.ContainingType, symbols.ToolTip))
        {
            Report(context, invocation.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 1)?.Value);
            return;
        }

        if (method.Name == "Separator" && SymbolEqualityComparer.Default.Equals(method.ContainingType, symbols.PickItem))
        {
            Report(context, invocation.Arguments.FirstOrDefault(a => a.Parameter?.Name == "label")?.Value);
            return;
        }

        if (symbols.NotificationService is { } notifications && Implements(method, notifications))
        {
            foreach (var argument in invocation.Arguments.Where(a => a.Parameter is { } parameter && NotificationParameters.Contains(parameter.Name)))
                Report(context, argument.Value);
        }
    }

    private static void AnalyzeCreation(OperationAnalysisContext context, Symbols symbols)
    {
        var creation = (IObjectCreationOperation)context.Operation;
        var type = creation.Type?.OriginalDefinition;
        var parameter = SymbolEqualityComparer.Default.Equals(type, symbols.CommandDefinition) ? "Title"
            : SymbolEqualityComparer.Default.Equals(type, symbols.TreeItem) || SymbolEqualityComparer.Default.Equals(type, symbols.GenericPickItem) ? "Label"
            : null;
        if (parameter is not null)
            Report(context, creation.Arguments.FirstOrDefault(a => a.Parameter?.Name == parameter)?.Value);
    }

    private static bool DerivesFrom(ITypeSymbol? type, INamedTypeSymbol baseType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
                return true;
        }

        return false;
    }

    private static bool Implements(IMethodSymbol method, INamedTypeSymbol service)
    {
        if (SymbolEqualityComparer.Default.Equals(method.ContainingType, service))
            return true;

        return service.GetMembers(method.Name).OfType<IMethodSymbol>().Any(member =>
            SymbolEqualityComparer.Default.Equals(method.ContainingType.FindImplementationForInterfaceMember(member), method));
    }

    private sealed class Symbols(
        INamedTypeSymbol? avaloniaObject, INamedTypeSymbol? toolTip, INamedTypeSymbol? notificationService, INamedTypeSymbol? commandDefinition,
        INamedTypeSymbol? treeItem, INamedTypeSymbol? genericPickItem, INamedTypeSymbol? pickItem)
    {
        public INamedTypeSymbol? AvaloniaObject { get; } = avaloniaObject;

        public INamedTypeSymbol? ToolTip { get; } = toolTip;

        public INamedTypeSymbol? NotificationService { get; } = notificationService;

        public INamedTypeSymbol? CommandDefinition { get; } = commandDefinition;

        public INamedTypeSymbol? TreeItem { get; } = treeItem;

        public INamedTypeSymbol? GenericPickItem { get; } = genericPickItem;

        public INamedTypeSymbol? PickItem { get; } = pickItem;
    }
}
