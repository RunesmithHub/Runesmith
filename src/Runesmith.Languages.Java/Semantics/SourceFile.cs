using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>A parsed source file of a project and the types it declares, top-level and nested.</summary>
internal sealed class SourceFile
{
    private readonly Dictionary<SyntaxNode, SourceType> byDeclaration = new(ReferenceEqualityComparer.Instance);

    private SourceFile(string path, JavaSyntaxTree tree)
    {
        Path = path;
        Tree = tree;
        PackageName = tree.Root.Package?.Name.ToString() ?? "";
    }

    public string Path { get; }

    public JavaSyntaxTree Tree { get; }

    /// <summary>Gets the package, empty for the unnamed package.</summary>
    public string PackageName { get; }

    /// <summary>Gets the types declared in the file, outer types before the types nested in them.</summary>
    public IReadOnlyList<SourceType> Types { get; private set; } = [];

    /// <summary>Gets the class a compact source file declares implicitly, or null.</summary>
    public SourceType? ImplicitClass { get; private set; }

    /// <summary>Gets the module a <c>module-info.java</c> declares, or null.</summary>
    public ModuleInfo? Module { get; private set; }

    public static SourceFile Create(string path, JavaSyntaxTree tree)
    {
        var file = new SourceFile(path, tree);
        var types = new List<SourceType>();
        var root = tree.Root;
        if (root.Module is { } module)
        {
            file.Module = new ModuleInfo(module.Name.ToString(),
                [.. module.Directives.Where(d => d.Kind == ModuleDirectiveKind.Exports).Select(d => new ModuleExport(d.Name.ToString(), d.Targets.Count > 0))],
                [.. module.Directives.Where(d => d.Kind == ModuleDirectiveKind.Requires).Select(d => d.Name.ToString())])
            {
                TransitiveRequires = [.. module.Directives.Where(d => d.Kind == ModuleDirectiveKind.Requires && d.Modifiers.HasFlag(RequiresModifiers.Transitive))
                    .Select(d => d.Name.ToString())],
            };
        }

        var prefix = file.PackageName.Length > 0 ? file.PackageName + "." : "";
        if (root.IsCompactSourceFile)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var implicitClass = new SourceType(file, prefix + name, name, null, null);
            file.ImplicitClass = implicitClass;
            types.Add(implicitClass);
            foreach (var member in root.Members.OfType<TypeDeclarationSyntax>())
                file.Add(types, member, implicitClass);
        }
        else
        {
            foreach (var member in root.Members.OfType<TypeDeclarationSyntax>())
                file.Add(types, member, null);
        }

        file.Types = types;
        return file;
    }

    /// <summary>Gets the type a declaration in this file declares, or null for a local class.</summary>
    public SourceType? TypeOf(SyntaxNode declaration) => byDeclaration.GetValueOrDefault(declaration);

    private void Add(List<SourceType> types, TypeDeclarationSyntax declaration, SourceType? outer)
    {
        if (declaration.Name.IsMissing)
            return;

        var prefix = PackageName.Length > 0 ? PackageName + "." : "";
        var binaryName = outer is null ? prefix + declaration.Name.Text : outer.BinaryName + "$" + declaration.Name.Text;
        var type = new SourceType(this, binaryName, declaration.Name.Text, declaration, outer);
        types.Add(type);
        byDeclaration[declaration] = type;
        foreach (var member in declaration.Body.Members.OfType<TypeDeclarationSyntax>())
            Add(types, member, type);
    }
}

/// <summary>A type declared in a project's source, whose symbol is built the first time it is asked for.</summary>
internal sealed class SourceType(SourceFile file, string binaryName, string simpleName, TypeDeclarationSyntax? declaration, SourceType? outer)
{
    [ThreadStatic]
    private static HashSet<SourceType>? building;

    private ClassSymbol? symbol;
    private Dictionary<object, SyntaxNode>? memberDeclarations;

    public SourceFile File => file;

    public string BinaryName => binaryName;

    public string SimpleName => simpleName;

    /// <summary>Gets the declaration, or null for the class a compact source file declares implicitly.</summary>
    public TypeDeclarationSyntax? Declaration => declaration;

    public SourceType? Outer => outer;

    /// <summary>Gets whether an annotation the analyzer cannot resolve, or one known to generate members, is on the type, so it may
    /// have members that are not in its source.</summary>
    public bool MayHaveHiddenMembers { get; internal set; }

    /// <summary>Gets the type's symbol, building it on first use; returns null while it is being built, for types that name themselves
    /// in their own headers.</summary>
    public ClassSymbol? GetSymbol(Func<SourceType, (ClassSymbol Symbol, Dictionary<object, SyntaxNode> Members)> build)
    {
        if (Volatile.Read(ref symbol) is { } built)
            return built;

        building ??= [];
        if (!building.Add(this))
            return null;

        try
        {
            var (created, members) = build(this);
            if (Interlocked.CompareExchange(ref symbol, created, null) is null)
                memberDeclarations = members;
            return symbol;
        }
        finally
        {
            building.Remove(this);
        }
    }

    /// <summary>Gets the declaration of a field or method of the symbol, once its members were read.</summary>
    public SyntaxNode? DeclarationOf(object member) =>
        memberDeclarations is { } declarations && declarations.TryGetValue(member, out var node) ? node : null;
}
