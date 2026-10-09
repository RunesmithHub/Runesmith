namespace Runesmith.Languages.Java.Syntax;

/// <summary>An identifier, such as a name in a declaration; missing when the source has none where one belongs.</summary>
public sealed class IdentifierSyntax : SyntaxNode
{
    private readonly bool isMissing;

    internal IdentifierSyntax(SourceSpan span, string text, bool isMissing = false)
        : base(span)
    {
        Text = text;
        this.isMissing = isMissing;
    }

    /// <summary>Gets the identifier, with Unicode escapes decoded; empty when it is missing.</summary>
    public string Text { get; }

    public override bool IsMissing => isMissing;

    public override string ToString() => Text;
}

/// <summary>A dotted name, such as a package or module name: <c>java.util.concurrent</c>.</summary>
public sealed class NameSyntax : SyntaxNode
{
    internal NameSyntax(SourceSpan span, SyntaxList<IdentifierSyntax>? parts)
        : base(span, [parts])
    {
    }

    public SyntaxList<IdentifierSyntax> Parts => ListSlot<IdentifierSyntax>(0);

    /// <summary>Gets the name with dots between its parts.</summary>
    public override string ToString() => string.Join('.', Parts.Select(p => p.Text));
}

/// <summary>An annotation: <c>@Name</c>, <c>@Name(value)</c> or <c>@Name(a = 1, b = {2, 3})</c>.</summary>
public sealed class AnnotationSyntax : SyntaxNode
{
    internal AnnotationSyntax(SourceSpan span, NameSyntax name, SyntaxList<AnnotationArgumentSyntax>? arguments, bool hasParentheses)
        : base(span, [name, arguments])
    {
        HasParentheses = hasParentheses;
    }

    public NameSyntax Name => Slot<NameSyntax>(0);

    public SyntaxList<AnnotationArgumentSyntax> Arguments => ListSlot<AnnotationArgumentSyntax>(1);

    public bool HasParentheses { get; }
}

/// <summary>An argument of an annotation: a value, or a name and a value.</summary>
public sealed class AnnotationArgumentSyntax : SyntaxNode
{
    internal AnnotationArgumentSyntax(SourceSpan span, IdentifierSyntax? name, SyntaxNode value)
        : base(span, [name, value])
    {
    }

    /// <summary>Gets the element's name, or null for the single <c>value</c> argument written without one.</summary>
    public IdentifierSyntax? Name => OptionalSlot<IdentifierSyntax>(0);

    /// <summary>Gets the value: an expression, an annotation, or an <see cref="ElementValueArrayInitializerSyntax"/>.</summary>
    public SyntaxNode Value => Slot<SyntaxNode>(1);
}

/// <summary>An array of annotation element values: <c>{1, 2}</c>.</summary>
public sealed class ElementValueArrayInitializerSyntax : SyntaxNode
{
    internal ElementValueArrayInitializerSyntax(SourceSpan span, SyntaxList<SyntaxNode>? values)
        : base(span, [values])
    {
    }

    public SyntaxList<SyntaxNode> Values => ListSlot<SyntaxNode>(0);
}

/// <summary>The modifiers and annotations before a declaration.</summary>
public sealed class ModifiersSyntax : SyntaxNode
{
    internal ModifiersSyntax(SourceSpan span, JavaModifiers flags, SyntaxList<AnnotationSyntax>? annotations)
        : base(span, [annotations])
    {
        Flags = flags;
    }

    /// <summary>Gets no modifiers.</summary>
    public static ModifiersSyntax None { get; } = new(default, JavaModifiers.None, null);

    public JavaModifiers Flags { get; }

    public SyntaxList<AnnotationSyntax> Annotations => ListSlot<AnnotationSyntax>(0);

    public bool Has(JavaModifiers modifier) => (Flags & modifier) == modifier;
}
