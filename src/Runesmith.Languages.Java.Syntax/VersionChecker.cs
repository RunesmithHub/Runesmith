using System.Runtime.CompilerServices;

namespace Runesmith.Languages.Java.Syntax;

/// <summary>Reports syntax that the chosen Java release does not have, or has only as a preview that is not enabled.</summary>
/// <remarks>Each report covers the construct's own node, never a node around it, so that a reparsed body only changes the reports inside it.</remarks>
internal sealed class VersionChecker(JavaVersion version, List<SyntaxDiagnostic> diagnostics)
{
    public void CheckCompilationUnit(CompilationUnitSyntax unit)
    {
        if (unit.Members.FirstOrDefault(m => m is not TypeDeclarationSyntax) is { } first)
            Report(JavaFeature.CompactSourceFiles, (SyntaxNode?)NameOf(first) ?? first);

        Visit(unit);
    }

    /// <summary>Checks a reparsed body, given the member it belongs to.</summary>
    public void CheckBody(BlockSyntax body, MemberDeclarationSyntax owner)
    {
        Visit(body);
        if (owner is ConstructorDeclarationSyntax)
            CheckConstructorBody(body);
    }

    private static IdentifierSyntax? NameOf(MemberDeclarationSyntax member) => member switch
    {
        MethodDeclarationSyntax method => method.Name,
        FieldDeclarationSyntax field => field.Variables[0].Name,
        _ => null,
    };

    private void Visit(SyntaxNode node)
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            return;

        Check(node);
        for (var i = 0; i < node.SlotCount; i++)
        {
            switch (node.GetSlot(i))
            {
                case null:
                    break;
                case ISyntaxList list:
                    var items = (SyntaxNode)list;
                    for (var j = 0; j < items.SlotCount; j++)
                        Visit(items.GetSlot(j)!);
                    break;
                case var child:
                    Visit(child);
                    break;
            }
        }
    }

    private void Check(SyntaxNode node)
    {
        switch (node)
        {
            case LambdaExpressionSyntax lambda:
                Report(JavaFeature.Lambdas, lambda);
                foreach (var parameter in lambda.Parameters)
                {
                    if (parameter.Type is VarTypeSyntax var)
                        Report(JavaFeature.VarInLambdaParameters, var);
                }

                break;
            case MethodReferenceExpressionSyntax reference:
                Report(JavaFeature.MethodReferences, reference);
                break;
            case ModifiersSyntax modifiers:
                CheckModifiers(modifiers);
                break;
            case ModuleDeclarationSyntax module:
                Report(JavaFeature.Modules, module.Name);
                break;
            case LocalVariableDeclarationSyntax { Type: VarTypeSyntax var }:
                Report(JavaFeature.LocalVariableTypeInference, var);
                break;
            case SwitchExpressionSyntax switchExpression:
                Report(JavaFeature.SwitchExpressions, switchExpression);
                break;
            case SwitchCaseSyntax switchCase:
                CheckSwitchCase(switchCase);
                break;
            case YieldStatementSyntax yield:
                Report(JavaFeature.SwitchExpressions, yield);
                break;
            case LiteralExpressionSyntax { Kind: LiteralKind.TextBlock } textBlock:
                Report(JavaFeature.TextBlocks, textBlock);
                break;
            case RecordDeclarationSyntax record:
                Report(JavaFeature.Records, record.Name);
                break;
            case InstanceofExpressionSyntax instanceof:
                CheckInstanceof(instanceof);
                break;
            case ClassDeclarationSyntax { Permits.Count: > 0 } sealedClass:
                Report(JavaFeature.SealedClasses, sealedClass.Permits);
                break;
            case InterfaceDeclarationSyntax { Permits.Count: > 0 } sealedInterface:
                Report(JavaFeature.SealedClasses, sealedInterface.Permits);
                break;
            case RecordPatternSyntax recordPattern:
                Report(JavaFeature.RecordPatterns, recordPattern);
                break;
            case UnnamedPatternSyntax unnamed:
                Report(JavaFeature.UnnamedVariables, unnamed);
                break;
            case ImportDeclarationSyntax { Kind: ImportKind.Module } import:
                Report(JavaFeature.ModuleImports, import);
                break;
            case ConstructorDeclarationSyntax constructor:
                CheckConstructorBody(constructor.Body);
                break;
            case VariableDeclaratorSyntax declarator:
                CheckUnnamed(declarator.Name);
                break;
            case ParameterSyntax parameter:
                CheckUnnamed(parameter.Name);
                break;
            case CatchClauseSyntax catchClause:
                CheckUnnamed(catchClause.Name);
                break;
            case ForEachStatementSyntax forEach:
                if (forEach.Type is VarTypeSyntax forEachVar)
                    Report(JavaFeature.LocalVariableTypeInference, forEachVar);
                CheckUnnamed(forEach.Name);
                break;
            case TypePatternSyntax typePattern:
                CheckUnnamed(typePattern.Name);
                break;
        }
    }

    private void CheckModifiers(ModifiersSyntax modifiers)
    {
        if (modifiers.Has(JavaModifiers.Default))
            Report(JavaFeature.DefaultMethods, modifiers);
        if (modifiers.Has(JavaModifiers.Sealed) || modifiers.Has(JavaModifiers.NonSealed))
            Report(JavaFeature.SealedClasses, modifiers);
    }

    private void CheckSwitchCase(SwitchCaseSyntax switchCase)
    {
        if (switchCase.IsArrow)
            Report(JavaFeature.SwitchExpressions, switchCase.Labels.Count > 0 ? switchCase.Labels[0] : switchCase);

        else if (switchCase.Labels.Count > 1 && switchCase.Labels[1] is not DefaultLabelSyntax)
            Report(JavaFeature.SwitchExpressions, switchCase.Labels[1]);

        if (switchCase.Guard is { } guard)
            Report(JavaFeature.SwitchPatterns, guard);

        foreach (var label in switchCase.Labels)
        {
            if (label is TypePatternSyntax typePattern)
            {
                Report(JavaFeature.SwitchPatterns, typePattern);
                CheckPrimitivePattern(typePattern);
            }
            else if (label is LiteralExpressionSyntax { Kind: LiteralKind.Null } nullLabel)
            {
                Report(JavaFeature.SwitchPatterns, nullLabel);
            }
        }
    }

    private void CheckInstanceof(InstanceofExpressionSyntax instanceof)
    {
        switch (instanceof.TypeOrPattern)
        {
            case TypePatternSyntax typePattern:
                Report(JavaFeature.InstanceofPatterns, typePattern);
                CheckPrimitivePattern(typePattern);
                break;
            case PrimitiveTypeSyntax primitive:
                Report(JavaFeature.PrimitiveTypesInPatterns, primitive);
                break;
        }
    }

    // A primitive type is new only at the top of a pattern; record components of primitive types match since record patterns came.
    private void CheckPrimitivePattern(TypePatternSyntax pattern)
    {
        if (pattern.Type is PrimitiveTypeSyntax primitive)
            Report(JavaFeature.PrimitiveTypesInPatterns, primitive);
    }

    private void CheckUnnamed(IdentifierSyntax name)
    {
        // Release 8 still allows _ as a name.
        if (name.Text == "_" && version.Release > 8)
            Report(JavaFeature.UnnamedVariables, name);
    }

    private void CheckConstructorBody(BlockSyntax body)
    {
        var statements = body.Statements;
        for (var i = 1; i < statements.Count; i++)
        {
            if (statements[i] is ExplicitConstructorInvocationSyntax invocation)
                Report(JavaFeature.FlexibleConstructorBodies, invocation);
        }
    }

    private void Report(JavaFeature feature, SyntaxNode node)
    {
        var support = JavaFeatures.GetSupport(feature, version);
        if (support == JavaFeatureSupport.Supported)
            return;

        var code = support == JavaFeatureSupport.NeedsPreview ? SyntaxDiagnostic.PreviewError : SyntaxDiagnostic.ReleaseError;
        diagnostics.Add(new SyntaxDiagnostic(node.Span, code, JavaFeatures.GetMessage(feature, version)!));
    }
}
