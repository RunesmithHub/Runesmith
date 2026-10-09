namespace Runesmith.Languages.Java.Syntax;

/// <summary>A statement.</summary>
public abstract class StatementSyntax : SyntaxNode
{
    private protected StatementSyntax(SourceSpan span, SyntaxNode?[]? slots)
        : base(span, slots)
    {
    }
}

/// <summary>A block: statements between braces.</summary>
public sealed class BlockSyntax : StatementSyntax
{
    internal BlockSyntax(SourceSpan span, SyntaxList<StatementSyntax>? statements, bool hasCloseBrace)
        : base(span, [statements])
    {
        HasCloseBrace = hasCloseBrace;
    }

    public SyntaxList<StatementSyntax> Statements => ListSlot<StatementSyntax>(0);

    public bool HasCloseBrace { get; }
}

/// <summary>A local variable declaration, as a statement or in a <c>for</c> or <c>try</c> header.</summary>
public sealed class LocalVariableDeclarationSyntax : StatementSyntax
{
    internal LocalVariableDeclarationSyntax(SourceSpan span, ModifiersSyntax? modifiers, TypeSyntax type, SyntaxList<VariableDeclaratorSyntax> variables)
        : base(span, [modifiers, type, variables])
    {
    }

    public ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    /// <summary>Gets the type, a <see cref="VarTypeSyntax"/> for <c>var</c>.</summary>
    public TypeSyntax Type => Slot<TypeSyntax>(1);

    public SyntaxList<VariableDeclaratorSyntax> Variables => ListSlot<VariableDeclaratorSyntax>(2);
}

/// <summary>A class, interface, enum or record declared inside a block.</summary>
public sealed class LocalTypeDeclarationStatementSyntax : StatementSyntax
{
    internal LocalTypeDeclarationStatementSyntax(SourceSpan span, TypeDeclarationSyntax declaration)
        : base(span, [declaration])
    {
    }

    public TypeDeclarationSyntax Declaration => Slot<TypeDeclarationSyntax>(0);
}

/// <summary>An expression used as a statement, such as a call or an assignment.</summary>
public sealed class ExpressionStatementSyntax : StatementSyntax
{
    internal ExpressionStatementSyntax(SourceSpan span, ExpressionSyntax expression)
        : base(span, [expression])
    {
    }

    public ExpressionSyntax Expression => Slot<ExpressionSyntax>(0);
}

/// <summary>An empty statement: <c>;</c>.</summary>
public sealed class EmptyStatementSyntax : StatementSyntax
{
    internal EmptyStatementSyntax(SourceSpan span)
        : base(span, null)
    {
    }
}

/// <summary>A statement with a label: <c>outer: for (...)</c>.</summary>
public sealed class LabeledStatementSyntax : StatementSyntax
{
    internal LabeledStatementSyntax(SourceSpan span, IdentifierSyntax label, StatementSyntax statement)
        : base(span, [label, statement])
    {
    }

    public IdentifierSyntax Label => Slot<IdentifierSyntax>(0);

    public StatementSyntax Statement => Slot<StatementSyntax>(1);
}

/// <summary><c>if (condition) then else otherwise</c>.</summary>
public sealed class IfStatementSyntax : StatementSyntax
{
    internal IfStatementSyntax(SourceSpan span, ExpressionSyntax condition, StatementSyntax then, StatementSyntax? @else)
        : base(span, [condition, then, @else])
    {
    }

    public ExpressionSyntax Condition => Slot<ExpressionSyntax>(0);

    public StatementSyntax Then => Slot<StatementSyntax>(1);

    public StatementSyntax? Else => OptionalSlot<StatementSyntax>(2);
}

/// <summary><c>while (condition) body</c>.</summary>
public sealed class WhileStatementSyntax : StatementSyntax
{
    internal WhileStatementSyntax(SourceSpan span, ExpressionSyntax condition, StatementSyntax body)
        : base(span, [condition, body])
    {
    }

    public ExpressionSyntax Condition => Slot<ExpressionSyntax>(0);

    public StatementSyntax Body => Slot<StatementSyntax>(1);
}

/// <summary><c>do body while (condition);</c>.</summary>
public sealed class DoStatementSyntax : StatementSyntax
{
    internal DoStatementSyntax(SourceSpan span, StatementSyntax body, ExpressionSyntax condition)
        : base(span, [body, condition])
    {
    }

    public StatementSyntax Body => Slot<StatementSyntax>(0);

    public ExpressionSyntax Condition => Slot<ExpressionSyntax>(1);
}

/// <summary>A basic <c>for</c> loop; its initializers are a local variable declaration or expression statements.</summary>
public sealed class ForStatementSyntax : StatementSyntax
{
    internal ForStatementSyntax(SourceSpan span, SyntaxList<StatementSyntax>? initializers, ExpressionSyntax? condition,
        SyntaxList<ExpressionSyntax>? updates, StatementSyntax body)
        : base(span, [initializers, condition, updates, body])
    {
    }

    public SyntaxList<StatementSyntax> Initializers => ListSlot<StatementSyntax>(0);

    public ExpressionSyntax? Condition => OptionalSlot<ExpressionSyntax>(1);

    public SyntaxList<ExpressionSyntax> Updates => ListSlot<ExpressionSyntax>(2);

    public StatementSyntax Body => Slot<StatementSyntax>(3);
}

/// <summary>An enhanced <c>for</c> loop: <c>for (String s : list)</c>.</summary>
public sealed class ForEachStatementSyntax : StatementSyntax
{
    internal ForEachStatementSyntax(SourceSpan span, ModifiersSyntax? modifiers, TypeSyntax type, IdentifierSyntax name, ExpressionSyntax expression,
        StatementSyntax body)
        : base(span, [modifiers, type, name, expression, body])
    {
    }

    public ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public TypeSyntax Type => Slot<TypeSyntax>(1);

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(2);

    public ExpressionSyntax Expression => Slot<ExpressionSyntax>(3);

    public StatementSyntax Body => Slot<StatementSyntax>(4);
}

/// <summary><c>return</c>, with or without a value.</summary>
public sealed class ReturnStatementSyntax : StatementSyntax
{
    internal ReturnStatementSyntax(SourceSpan span, ExpressionSyntax? expression)
        : base(span, [expression])
    {
    }

    public ExpressionSyntax? Expression => OptionalSlot<ExpressionSyntax>(0);
}

/// <summary><c>break</c>, with or without a label.</summary>
public sealed class BreakStatementSyntax : StatementSyntax
{
    internal BreakStatementSyntax(SourceSpan span, IdentifierSyntax? label)
        : base(span, [label])
    {
    }

    public IdentifierSyntax? Label => OptionalSlot<IdentifierSyntax>(0);
}

/// <summary><c>continue</c>, with or without a label.</summary>
public sealed class ContinueStatementSyntax : StatementSyntax
{
    internal ContinueStatementSyntax(SourceSpan span, IdentifierSyntax? label)
        : base(span, [label])
    {
    }

    public IdentifierSyntax? Label => OptionalSlot<IdentifierSyntax>(0);
}

/// <summary><c>throw expression;</c>.</summary>
public sealed class ThrowStatementSyntax : StatementSyntax
{
    internal ThrowStatementSyntax(SourceSpan span, ExpressionSyntax expression)
        : base(span, [expression])
    {
    }

    public ExpressionSyntax Expression => Slot<ExpressionSyntax>(0);
}

/// <summary><c>yield value;</c> in a switch expression.</summary>
public sealed class YieldStatementSyntax : StatementSyntax
{
    internal YieldStatementSyntax(SourceSpan span, ExpressionSyntax expression)
        : base(span, [expression])
    {
    }

    public ExpressionSyntax Expression => Slot<ExpressionSyntax>(0);
}

/// <summary><c>synchronized (lock) { ... }</c>.</summary>
public sealed class SynchronizedStatementSyntax : StatementSyntax
{
    internal SynchronizedStatementSyntax(SourceSpan span, ExpressionSyntax @lock, BlockSyntax body)
        : base(span, [@lock, body])
    {
    }

    public ExpressionSyntax Lock => Slot<ExpressionSyntax>(0);

    public BlockSyntax Body => Slot<BlockSyntax>(1);
}

/// <summary><c>assert condition : message;</c>.</summary>
public sealed class AssertStatementSyntax : StatementSyntax
{
    internal AssertStatementSyntax(SourceSpan span, ExpressionSyntax condition, ExpressionSyntax? message)
        : base(span, [condition, message])
    {
    }

    public ExpressionSyntax Condition => Slot<ExpressionSyntax>(0);

    public ExpressionSyntax? Message => OptionalSlot<ExpressionSyntax>(1);
}

/// <summary>A <c>try</c> statement, with resources, catches and a finally block.</summary>
public sealed class TryStatementSyntax : StatementSyntax
{
    internal TryStatementSyntax(SourceSpan span, SyntaxList<SyntaxNode>? resources, bool hasResourceSpecification, BlockSyntax body,
        SyntaxList<CatchClauseSyntax>? catches, BlockSyntax? @finally)
        : base(span, [resources, body, catches, @finally])
    {
        HasResourceSpecification = hasResourceSpecification;
    }

    /// <summary>Gets the resources: <see cref="LocalVariableDeclarationSyntax"/>s, or expressions naming effectively final variables.</summary>
    public SyntaxList<SyntaxNode> Resources => ListSlot<SyntaxNode>(0);

    public bool HasResourceSpecification { get; }

    public BlockSyntax Body => Slot<BlockSyntax>(1);

    public SyntaxList<CatchClauseSyntax> Catches => ListSlot<CatchClauseSyntax>(2);

    public BlockSyntax? Finally => OptionalSlot<BlockSyntax>(3);
}

/// <summary>A <c>catch</c> clause; several exception types form a <see cref="UnionTypeSyntax"/>.</summary>
public sealed class CatchClauseSyntax : SyntaxNode
{
    internal CatchClauseSyntax(SourceSpan span, ModifiersSyntax? modifiers, TypeSyntax type, IdentifierSyntax name, BlockSyntax body)
        : base(span, [modifiers, type, name, body])
    {
    }

    public ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public TypeSyntax Type => Slot<TypeSyntax>(1);

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(2);

    public BlockSyntax Body => Slot<BlockSyntax>(3);
}

/// <summary>A <c>switch</c> statement.</summary>
public sealed class SwitchStatementSyntax : StatementSyntax
{
    internal SwitchStatementSyntax(SourceSpan span, ExpressionSyntax selector, SyntaxList<SwitchCaseSyntax>? cases)
        : base(span, [selector, cases])
    {
    }

    public ExpressionSyntax Selector => Slot<ExpressionSyntax>(0);

    public SyntaxList<SwitchCaseSyntax> Cases => ListSlot<SwitchCaseSyntax>(1);
}

/// <summary>A case of a switch: its labels and guard, and either the statements after a colon or the body after an arrow.</summary>
public sealed class SwitchCaseSyntax : SyntaxNode
{
    internal SwitchCaseSyntax(SourceSpan span, SyntaxList<SyntaxNode>? labels, ExpressionSyntax? guard, bool isArrow,
        SyntaxList<StatementSyntax>? statements, SyntaxNode? arrowBody)
        : base(span, [labels, guard, statements, arrowBody])
    {
        IsArrow = isArrow;
    }

    /// <summary>Gets the labels: constant expressions (and <c>null</c>), patterns, and <see cref="DefaultLabelSyntax"/>.</summary>
    public SyntaxList<SyntaxNode> Labels => ListSlot<SyntaxNode>(0);

    /// <summary>Gets the <c>when</c> guard of a pattern case.</summary>
    public ExpressionSyntax? Guard => OptionalSlot<ExpressionSyntax>(1);

    public bool IsArrow { get; }

    /// <summary>Gets the statements after the colon of an old-style case.</summary>
    public SyntaxList<StatementSyntax> Statements => ListSlot<StatementSyntax>(2);

    /// <summary>Gets the body after the arrow: an expression, a block or a throw statement.</summary>
    public SyntaxNode? ArrowBody => OptionalSlot<SyntaxNode>(3);
}

/// <summary>The <c>default</c> label of a switch.</summary>
public sealed class DefaultLabelSyntax : SyntaxNode
{
    internal DefaultLabelSyntax(SourceSpan span)
        : base(span)
    {
    }
}

/// <summary><c>this(...)</c> or <c>super(...)</c>, possibly qualified (<c>outer.super(...)</c>) or with type arguments, in a constructor.</summary>
public sealed class ExplicitConstructorInvocationSyntax : StatementSyntax
{
    internal ExplicitConstructorInvocationSyntax(SourceSpan span, ExpressionSyntax? qualifier, TypeArgumentListSyntax? typeArguments, bool isSuper,
        ArgumentListSyntax arguments)
        : base(span, [qualifier, typeArguments, arguments])
    {
        IsSuper = isSuper;
    }

    public ExpressionSyntax? Qualifier => OptionalSlot<ExpressionSyntax>(0);

    public TypeArgumentListSyntax? TypeArguments => OptionalSlot<TypeArgumentListSyntax>(1);

    public bool IsSuper { get; }

    public ArgumentListSyntax Arguments => Slot<ArgumentListSyntax>(2);
}

/// <summary>Source that could not be read as a statement, kept so the rest of the block still is.</summary>
public sealed class IncompleteStatementSyntax : StatementSyntax
{
    internal IncompleteStatementSyntax(SourceSpan span)
        : base(span, null)
    {
    }
}
