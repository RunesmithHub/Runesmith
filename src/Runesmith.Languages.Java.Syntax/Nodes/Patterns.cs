namespace Runesmith.Languages.Java.Syntax;

/// <summary>A pattern, in <c>instanceof</c> or a switch case.</summary>
public abstract class PatternSyntax : SyntaxNode
{
    private protected PatternSyntax(SourceSpan span, SyntaxNode?[]? slots)
        : base(span, slots)
    {
    }
}

/// <summary>A type pattern: <c>String s</c>, <c>var x</c>, <c>int i</c>, or <c>Point _</c>.</summary>
public sealed class TypePatternSyntax : PatternSyntax
{
    internal TypePatternSyntax(SourceSpan span, ModifiersSyntax? modifiers, TypeSyntax type, IdentifierSyntax name)
        : base(span, [modifiers, type, name])
    {
    }

    public ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public TypeSyntax Type => Slot<TypeSyntax>(1);

    /// <summary>Gets the binding's name, <c>_</c> for an unnamed one.</summary>
    public IdentifierSyntax Name => Slot<IdentifierSyntax>(2);
}

/// <summary>A record pattern: <c>Point(int x, var y)</c>.</summary>
public sealed class RecordPatternSyntax : PatternSyntax
{
    internal RecordPatternSyntax(SourceSpan span, TypeSyntax type, SyntaxList<PatternSyntax>? subpatterns)
        : base(span, [type, subpatterns])
    {
    }

    public TypeSyntax Type => Slot<TypeSyntax>(0);

    public SyntaxList<PatternSyntax> Subpatterns => ListSlot<PatternSyntax>(1);
}

/// <summary>The unnamed pattern <c>_</c> inside a record pattern.</summary>
public sealed class UnnamedPatternSyntax : PatternSyntax
{
    internal UnnamedPatternSyntax(SourceSpan span)
        : base(span, null)
    {
    }
}
