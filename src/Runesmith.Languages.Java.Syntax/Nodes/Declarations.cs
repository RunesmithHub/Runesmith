namespace Runesmith.Languages.Java.Syntax;

/// <summary>A whole source file: its package, imports, and either a module declaration or its top-level declarations.</summary>
public sealed class CompilationUnitSyntax : SyntaxNode
{
    internal CompilationUnitSyntax(SourceSpan span, PackageDeclarationSyntax? package, SyntaxList<ImportDeclarationSyntax>? imports,
        ModuleDeclarationSyntax? module, SyntaxList<MemberDeclarationSyntax>? members)
        : base(span, [package, imports, module, members])
    {
    }

    public PackageDeclarationSyntax? Package => OptionalSlot<PackageDeclarationSyntax>(0);

    public SyntaxList<ImportDeclarationSyntax> Imports => ListSlot<ImportDeclarationSyntax>(1);

    /// <summary>Gets the module declaration of a <c>module-info.java</c>, or null.</summary>
    public ModuleDeclarationSyntax? Module => OptionalSlot<ModuleDeclarationSyntax>(2);

    /// <summary>Gets the top-level declarations: types, and in a compact source file also methods and fields.</summary>
    public SyntaxList<MemberDeclarationSyntax> Members => ListSlot<MemberDeclarationSyntax>(3);

    /// <summary>Gets whether the file declares methods or fields outside a class, which makes it a compact source file (Java 25).</summary>
    public bool IsCompactSourceFile => Members.Any(m => m is not TypeDeclarationSyntax);
}

/// <summary><c>package com.example;</c>, with the package's annotations in a <c>package-info.java</c>.</summary>
public sealed class PackageDeclarationSyntax : SyntaxNode
{
    internal PackageDeclarationSyntax(SourceSpan span, SyntaxList<AnnotationSyntax>? annotations, NameSyntax name)
        : base(span, [annotations, name])
    {
    }

    public SyntaxList<AnnotationSyntax> Annotations => ListSlot<AnnotationSyntax>(0);

    public NameSyntax Name => Slot<NameSyntax>(1);
}

/// <summary>An import declaration; the name of an on-demand import leaves out the <c>.*</c>.</summary>
public sealed class ImportDeclarationSyntax : SyntaxNode
{
    internal ImportDeclarationSyntax(SourceSpan span, ImportKind kind, NameSyntax name)
        : base(span, [name])
    {
        Kind = kind;
    }

    public ImportKind Kind { get; }

    public NameSyntax Name => Slot<NameSyntax>(0);
}

/// <summary>A module declaration: <c>open module com.example { requires java.sql; }</c>.</summary>
public sealed class ModuleDeclarationSyntax : SyntaxNode
{
    internal ModuleDeclarationSyntax(SourceSpan span, SyntaxList<AnnotationSyntax>? annotations, bool isOpen, NameSyntax name,
        SyntaxList<ModuleDirectiveSyntax>? directives)
        : base(span, [annotations, name, directives])
    {
        IsOpen = isOpen;
    }

    public SyntaxList<AnnotationSyntax> Annotations => ListSlot<AnnotationSyntax>(0);

    public bool IsOpen { get; }

    public NameSyntax Name => Slot<NameSyntax>(1);

    public SyntaxList<ModuleDirectiveSyntax> Directives => ListSlot<ModuleDirectiveSyntax>(2);
}

/// <summary>A directive of a module: <c>requires</c>, <c>exports ... to</c>, <c>opens ... to</c>, <c>uses</c> or <c>provides ... with</c>.</summary>
public sealed class ModuleDirectiveSyntax : SyntaxNode
{
    internal ModuleDirectiveSyntax(SourceSpan span, ModuleDirectiveKind kind, RequiresModifiers modifiers, NameSyntax name,
        SyntaxList<NameSyntax>? targets)
        : base(span, [name, targets])
    {
        Kind = kind;
        Modifiers = modifiers;
    }

    public ModuleDirectiveKind Kind { get; }

    /// <summary>Gets <c>transitive</c> and <c>static</c> of a <c>requires</c>.</summary>
    public RequiresModifiers Modifiers { get; }

    /// <summary>Gets the module, package or service the directive is about.</summary>
    public NameSyntax Name => Slot<NameSyntax>(0);

    /// <summary>Gets the modules after <c>to</c>, or the implementations after <c>with</c>.</summary>
    public SyntaxList<NameSyntax> Targets => ListSlot<NameSyntax>(1);
}

/// <summary>A member of a class or interface, or a top-level declaration.</summary>
public abstract class MemberDeclarationSyntax : SyntaxNode
{
    private protected MemberDeclarationSyntax(SourceSpan span, SyntaxNode?[] slots)
        : base(span, slots)
    {
    }

    /// <summary>Gets the member's modifiers and annotations.</summary>
    public abstract ModifiersSyntax Modifiers { get; }
}

/// <summary>A class, interface, enum, record or annotation type declaration.</summary>
public abstract class TypeDeclarationSyntax : MemberDeclarationSyntax
{
    private protected TypeDeclarationSyntax(SourceSpan span, SyntaxNode?[] slots)
        : base(span, slots)
    {
    }

    public override ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(1);

    public TypeParameterListSyntax? TypeParameters => OptionalSlot<TypeParameterListSyntax>(2);

    /// <summary>Gets the body between the braces.</summary>
    public ClassBodySyntax Body => Slot<ClassBodySyntax>(SlotCount - 1);
}

/// <summary>The body of a class, interface, enum, record or anonymous class: the enum's constants, then the members.</summary>
public sealed class ClassBodySyntax : SyntaxNode
{
    internal ClassBodySyntax(SourceSpan span, SyntaxList<EnumConstantSyntax>? constants, SyntaxList<MemberDeclarationSyntax>? members,
        bool hasCloseBrace)
        : base(span, [constants, members])
    {
        HasCloseBrace = hasCloseBrace;
    }

    public SyntaxList<EnumConstantSyntax> EnumConstants => ListSlot<EnumConstantSyntax>(0);

    public SyntaxList<MemberDeclarationSyntax> Members => ListSlot<MemberDeclarationSyntax>(1);

    public bool HasCloseBrace { get; }
}

/// <summary>A class declaration.</summary>
public sealed class ClassDeclarationSyntax : TypeDeclarationSyntax
{
    internal ClassDeclarationSyntax(SourceSpan span, ModifiersSyntax? modifiers, IdentifierSyntax name, TypeParameterListSyntax? typeParameters,
        TypeSyntax? extends, SyntaxList<TypeSyntax>? implements, SyntaxList<TypeSyntax>? permits, ClassBodySyntax body)
        : base(span, [modifiers, name, typeParameters, extends, implements, permits, body])
    {
    }

    public TypeSyntax? Extends => OptionalSlot<TypeSyntax>(3);

    public SyntaxList<TypeSyntax> Implements => ListSlot<TypeSyntax>(4);

    public SyntaxList<TypeSyntax> Permits => ListSlot<TypeSyntax>(5);
}

/// <summary>An interface declaration.</summary>
public sealed class InterfaceDeclarationSyntax : TypeDeclarationSyntax
{
    internal InterfaceDeclarationSyntax(SourceSpan span, ModifiersSyntax? modifiers, IdentifierSyntax name,
        TypeParameterListSyntax? typeParameters, SyntaxList<TypeSyntax>? extends, SyntaxList<TypeSyntax>? permits, ClassBodySyntax body)
        : base(span, [modifiers, name, typeParameters, extends, permits, body])
    {
    }

    public SyntaxList<TypeSyntax> Extends => ListSlot<TypeSyntax>(3);

    public SyntaxList<TypeSyntax> Permits => ListSlot<TypeSyntax>(4);
}

/// <summary>An enum declaration; its constants are in <see cref="ClassBodySyntax.EnumConstants"/>.</summary>
public sealed class EnumDeclarationSyntax : TypeDeclarationSyntax
{
    internal EnumDeclarationSyntax(SourceSpan span, ModifiersSyntax? modifiers, IdentifierSyntax name, SyntaxList<TypeSyntax>? implements,
        ClassBodySyntax body)
        : base(span, [modifiers, name, null, implements, body])
    {
    }

    public SyntaxList<TypeSyntax> Implements => ListSlot<TypeSyntax>(3);
}

/// <summary>A record declaration: <c>record Point(int x, int y) { }</c>.</summary>
public sealed class RecordDeclarationSyntax : TypeDeclarationSyntax
{
    internal RecordDeclarationSyntax(SourceSpan span, ModifiersSyntax? modifiers, IdentifierSyntax name, TypeParameterListSyntax? typeParameters,
        RecordHeaderSyntax header, SyntaxList<TypeSyntax>? implements, ClassBodySyntax body)
        : base(span, [modifiers, name, typeParameters, header, implements, body])
    {
    }

    public RecordHeaderSyntax Header => Slot<RecordHeaderSyntax>(3);

    public SyntaxList<TypeSyntax> Implements => ListSlot<TypeSyntax>(4);
}

/// <summary>The components of a record, between its parentheses.</summary>
public sealed class RecordHeaderSyntax : SyntaxNode
{
    internal RecordHeaderSyntax(SourceSpan span, SyntaxList<RecordComponentSyntax>? components)
        : base(span, [components])
    {
    }

    public SyntaxList<RecordComponentSyntax> Components => ListSlot<RecordComponentSyntax>(0);
}

/// <summary>A component of a record: <c>int x</c>, or <c>String... names</c> as the last one.</summary>
public sealed class RecordComponentSyntax : SyntaxNode
{
    internal RecordComponentSyntax(SourceSpan span, ModifiersSyntax? modifiers, TypeSyntax type, bool isVarargs, IdentifierSyntax name)
        : base(span, [modifiers, type, name])
    {
        IsVarargs = isVarargs;
    }

    public ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public TypeSyntax Type => Slot<TypeSyntax>(1);

    public bool IsVarargs { get; }

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(2);
}

/// <summary>An annotation type declaration: <c>@interface Retention { }</c>.</summary>
public sealed class AnnotationTypeDeclarationSyntax : TypeDeclarationSyntax
{
    internal AnnotationTypeDeclarationSyntax(SourceSpan span, ModifiersSyntax? modifiers, IdentifierSyntax name, ClassBodySyntax body)
        : base(span, [modifiers, name, null, body])
    {
    }
}

/// <summary>A constant of an enum, with its arguments and body when it has them.</summary>
public sealed class EnumConstantSyntax : SyntaxNode
{
    internal EnumConstantSyntax(SourceSpan span, ModifiersSyntax? modifiers, IdentifierSyntax name, ArgumentListSyntax? arguments,
        ClassBodySyntax? body)
        : base(span, [modifiers, name, arguments, body])
    {
    }

    public ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(1);

    public ArgumentListSyntax? Arguments => OptionalSlot<ArgumentListSyntax>(2);

    public ClassBodySyntax? Body => OptionalSlot<ClassBodySyntax>(3);
}

/// <summary>A field declaration, with one or more variables.</summary>
public sealed class FieldDeclarationSyntax : MemberDeclarationSyntax
{
    internal FieldDeclarationSyntax(SourceSpan span, ModifiersSyntax? modifiers, TypeSyntax type, SyntaxList<VariableDeclaratorSyntax> variables)
        : base(span, [modifiers, type, variables])
    {
    }

    public override ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public TypeSyntax Type => Slot<TypeSyntax>(1);

    public SyntaxList<VariableDeclaratorSyntax> Variables => ListSlot<VariableDeclaratorSyntax>(2);
}

/// <summary>One variable of a field or local variable declaration: its name, extra brackets and initializer.</summary>
public sealed class VariableDeclaratorSyntax : SyntaxNode
{
    internal VariableDeclaratorSyntax(SourceSpan span, IdentifierSyntax name, SyntaxList<DimensionSyntax>? dimensions, ExpressionSyntax? initializer)
        : base(span, [name, dimensions, initializer])
    {
    }

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(0);

    /// <summary>Gets brackets after the name, as in <c>int a[]</c>.</summary>
    public SyntaxList<DimensionSyntax> Dimensions => ListSlot<DimensionSyntax>(1);

    /// <summary>Gets the initializer: an expression or an <see cref="ArrayInitializerExpressionSyntax"/>.</summary>
    public ExpressionSyntax? Initializer => OptionalSlot<ExpressionSyntax>(2);
}

/// <summary>A method declaration, also an annotation type element with its default value.</summary>
public sealed class MethodDeclarationSyntax : MemberDeclarationSyntax
{
    internal MethodDeclarationSyntax(SourceSpan span, ModifiersSyntax? modifiers, TypeParameterListSyntax? typeParameters, TypeSyntax returnType,
        IdentifierSyntax name, ParameterListSyntax parameters, SyntaxList<DimensionSyntax>? dimensions, SyntaxList<TypeSyntax>? throws,
        SyntaxNode? defaultValue, BlockSyntax? body)
        : base(span, [modifiers, typeParameters, returnType, name, parameters, dimensions, throws, defaultValue, body])
    {
    }

    public override ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public TypeParameterListSyntax? TypeParameters => OptionalSlot<TypeParameterListSyntax>(1);

    public TypeSyntax ReturnType => Slot<TypeSyntax>(2);

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(3);

    public ParameterListSyntax Parameters => Slot<ParameterListSyntax>(4);

    /// <summary>Gets brackets after the parameters, as in the old form <c>int m()[]</c>.</summary>
    public SyntaxList<DimensionSyntax> Dimensions => ListSlot<DimensionSyntax>(5);

    public SyntaxList<TypeSyntax> Throws => ListSlot<TypeSyntax>(6);

    /// <summary>Gets an annotation type element's default value.</summary>
    public SyntaxNode? DefaultValue => OptionalSlot<SyntaxNode>(7);

    /// <summary>Gets the body, or null for an abstract or native method.</summary>
    public BlockSyntax? Body => OptionalSlot<BlockSyntax>(8);
}

/// <summary>A constructor declaration.</summary>
public sealed class ConstructorDeclarationSyntax : MemberDeclarationSyntax
{
    internal ConstructorDeclarationSyntax(SourceSpan span, ModifiersSyntax? modifiers, TypeParameterListSyntax? typeParameters,
        IdentifierSyntax name, ParameterListSyntax parameters, SyntaxList<TypeSyntax>? throws, BlockSyntax body)
        : base(span, [modifiers, typeParameters, name, parameters, throws, body])
    {
    }

    public override ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public TypeParameterListSyntax? TypeParameters => OptionalSlot<TypeParameterListSyntax>(1);

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(2);

    public ParameterListSyntax Parameters => Slot<ParameterListSyntax>(3);

    public SyntaxList<TypeSyntax> Throws => ListSlot<TypeSyntax>(4);

    public BlockSyntax Body => Slot<BlockSyntax>(5);
}

/// <summary>The compact canonical constructor of a record: <c>Point { if (x &lt; 0) throw ...; }</c>.</summary>
public sealed class CompactConstructorDeclarationSyntax : MemberDeclarationSyntax
{
    internal CompactConstructorDeclarationSyntax(SourceSpan span, ModifiersSyntax? modifiers, IdentifierSyntax name, BlockSyntax body)
        : base(span, [modifiers, name, body])
    {
    }

    public override ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(1);

    public BlockSyntax Body => Slot<BlockSyntax>(2);
}

/// <summary>An instance or static initializer block.</summary>
public sealed class InitializerDeclarationSyntax : MemberDeclarationSyntax
{
    internal InitializerDeclarationSyntax(SourceSpan span, ModifiersSyntax? modifiers, BlockSyntax body)
        : base(span, [modifiers, body])
    {
    }

    public override ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public bool IsStatic => Modifiers.Has(JavaModifiers.Static);

    public BlockSyntax Body => Slot<BlockSyntax>(1);
}

/// <summary>The parameters of a method, constructor or lambda, between parentheses.</summary>
public sealed class ParameterListSyntax : SyntaxNode
{
    internal ParameterListSyntax(SourceSpan span, SyntaxList<ParameterSyntax>? parameters)
        : base(span, [parameters])
    {
    }

    public SyntaxList<ParameterSyntax> Parameters => ListSlot<ParameterSyntax>(0);
}

/// <summary>A formal parameter, or a receiver parameter (<c>Foo this</c>), whose name is <c>this</c>.</summary>
public sealed class ParameterSyntax : SyntaxNode
{
    internal ParameterSyntax(SourceSpan span, ModifiersSyntax? modifiers, TypeSyntax? type, bool isVarargs, IdentifierSyntax name,
        SyntaxList<DimensionSyntax>? dimensions)
        : base(span, [modifiers, type, name, dimensions])
    {
        IsVarargs = isVarargs;
    }

    public ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    /// <summary>Gets the type, or null for a lambda parameter whose type is inferred.</summary>
    public TypeSyntax? Type => OptionalSlot<TypeSyntax>(1);

    public bool IsVarargs { get; }

    public IdentifierSyntax Name => Slot<IdentifierSyntax>(2);

    public SyntaxList<DimensionSyntax> Dimensions => ListSlot<DimensionSyntax>(3);

    public bool IsReceiver => Name.Text == "this";
}

/// <summary>Source that could not be read as a member, kept so the rest of the file still is.</summary>
public sealed class IncompleteMemberSyntax : MemberDeclarationSyntax
{
    internal IncompleteMemberSyntax(SourceSpan span, ModifiersSyntax? modifiers, TypeSyntax? type)
        : base(span, [modifiers, type])
    {
    }

    public override ModifiersSyntax Modifiers => OptionalSlot<ModifiersSyntax>(0) ?? ModifiersSyntax.None;

    public TypeSyntax? Type => OptionalSlot<TypeSyntax>(1);
}
