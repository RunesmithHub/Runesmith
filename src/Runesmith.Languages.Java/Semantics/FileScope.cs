using Runesmith.Languages.Java.ClassFiles;
using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Languages.Java.Semantics;

/// <summary>A problem with an import that the analyzer is sure of.</summary>
internal sealed record ImportProblem(SourceSpan Span, string Message);

/// <summary>The names a file sees from outside its own declarations: its package, its imports, <c>java.lang</c>, and for a compact source
/// file the packages of <c>java.base</c>.</summary>
internal sealed class FileScope
{
    private const string JavaLang = "java.lang";

    private readonly SemanticModel model;
    private readonly Dictionary<string, string> singleTypes = new(StringComparer.Ordinal);
    private readonly HashSet<string> unresolvedNames = new(StringComparer.Ordinal);
    private readonly List<string> typesOnDemand = [];
    private readonly List<string> packagesOnDemand = [JavaLang];
    private readonly List<(string Type, string Member)> staticSingle = [];
    private readonly List<string> staticOnDemand = [];
    private readonly List<ImportProblem> problems = [];

    public FileScope(SemanticModel model)
    {
        this.model = model;
        var root = model.Tree.Root;
        PackageName = root.Package?.Name.ToString() ?? "";
        IsComplete = model.Project.HasJdk;
        if (root.IsCompactSourceFile && JavaFeatures.IsAvailable(JavaFeature.CompactSourceFiles, model.Project.Version))
            ImportModule("java.base", null);

        foreach (var import in root.Imports)
        {
            if (import.Name.Parts.Any(p => p.IsMissing))
            {
                IsComplete = false;
                continue;
            }

            switch (import.Kind)
            {
                case ImportKind.Single:
                    ImportType(import);
                    break;
                case ImportKind.OnDemand:
                    ImportOnDemand(import);
                    break;
                case ImportKind.Static:
                    ImportStatic(import);
                    break;
                case ImportKind.StaticOnDemand:
                    ImportStaticOnDemand(import);
                    break;
                case ImportKind.Module:
                    ImportModule(import.Name.ToString(), import);
                    break;
            }
        }
    }

    public string PackageName { get; }

    /// <summary>Gets whether every import was understood, so a name none of them gives is certainly not imported.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Gets the imports that certainly name nothing.</summary>
    public IReadOnlyList<ImportProblem> Problems => problems;

    /// <summary>Gets the members imported by name with <c>import static</c>: the type and the member's name.</summary>
    public IReadOnlyList<(string Type, string Member)> StaticImports => staticSingle;

    /// <summary>Gets the types whose static members <c>import static ...*</c> imports.</summary>
    public IReadOnlyList<string> StaticOnDemandImports => staticOnDemand;

    /// <summary>Gets whether a simple name was imported by an import that could not be resolved, so it may name a type the analyzer cannot see.</summary>
    public bool IsUnresolvedImport(string simpleName) => unresolvedNames.Contains(simpleName);

    /// <summary>Gets the type a simple name means at file level, or null.</summary>
    public string? LookupType(string simpleName)
    {
        if (singleTypes.TryGetValue(simpleName, out var imported))
            return imported;

        var samePackage = PackageName.Length > 0 ? PackageName + "." + simpleName : simpleName;
        if (model.Project.Contains(samePackage))
            return samePackage;

        foreach (var type in typesOnDemand)
        {
            if (model.FindMemberType(type, simpleName) is { } nested)
                return nested;
        }

        foreach (var package in packagesOnDemand)
        {
            var name = package + "." + simpleName;
            if (model.Project.Contains(name))
                return name;
        }

        foreach (var (type, member) in staticSingle)
        {
            if (member == simpleName && model.FindMemberType(type, simpleName) is { } nested)
                return nested;
        }

        foreach (var type in staticOnDemand)
        {
            if (model.FindMemberType(type, simpleName) is { } nested)
                return nested;
        }

        return null;
    }

    /// <summary>Gets every type the file sees by its simple name without an import being added: imported, in the same package or in
    /// <c>java.lang</c>.</summary>
    public IEnumerable<(string SimpleName, string BinaryName)> TypesInScope()
    {
        foreach (var (simple, binary) in singleTypes)
            yield return (simple, binary);
        foreach (var name in model.Project.ClassPath.GetTypes(PackageName))
        {
            if (!ClassNames.IsAnonymousOrLocal(name) && ClassNames.OuterName(name) is null)
                yield return (ClassNames.SimpleName(name), name);
        }

        foreach (var package in packagesOnDemand.Distinct(StringComparer.Ordinal))
        {
            foreach (var name in model.Project.ClassPath.GetTypes(package))
            {
                if (ClassNames.OuterName(name) is null)
                    yield return (ClassNames.SimpleName(name), name);
            }
        }

        foreach (var type in typesOnDemand)
        {
            foreach (var nested in model.FindClass(type)?.NestedClasses ?? [])
                yield return (ClassNames.SimpleName(nested), nested);
        }
    }

    /// <summary>Finds the type a canonical name such as <c>java.util.Map.Entry</c> names, as its binary name.</summary>
    public string? ResolveCanonical(string name)
    {
        var parts = name.Split('.');
        for (var split = parts.Length - 1; split >= 1; split--)
        {
            var binary = string.Join('.', parts, 0, split) + "." + string.Join('$', parts, split, parts.Length - split);
            if (model.Project.Contains(binary))
                return binary;
        }

        return parts.Length == 1 && model.Project.Contains(name) ? name : null;
    }

    private void ImportType(ImportDeclarationSyntax import)
    {
        var name = import.Name.ToString();
        var simple = import.Name.Parts[^1].Text;
        if (ResolveCanonical(name) is { } binary)
        {
            singleTypes[simple] = binary;
            return;
        }

        unresolvedNames.Add(simple);
        ReportIfCertain(import, name, isPackageImport: false);
    }

    private void ImportOnDemand(ImportDeclarationSyntax import)
    {
        var name = import.Name.ToString();
        if (model.Project.HasTypesIn(name))
        {
            packagesOnDemand.Add(name);
            return;
        }

        if (ResolveCanonical(name) is { } type)
        {
            typesOnDemand.Add(type);
            return;
        }

        if (!model.Project.IsPackage(name))
        {
            IsComplete = false;
            ReportIfCertain(import, name, isPackageImport: true);
        }
    }

    private void ImportStatic(ImportDeclarationSyntax import)
    {
        var parts = import.Name.Parts;
        var member = parts[^1].Text;
        var typeName = string.Join('.', parts.Take(parts.Count - 1).Select(p => p.Text));
        if (ResolveCanonical(typeName) is not { } type)
        {
            unresolvedNames.Add(member);
            ReportIfCertain(import, typeName, isPackageImport: false);
            return;
        }

        staticSingle.Add((type, member));
        if (model.IsFullyKnown(type) && !model.HasStaticMember(type, member))
            problems.Add(new ImportProblem(import.Name.Span, $"{ClassNames.SourceName(type)} has no static member '{member}'"));
    }

    private void ImportStaticOnDemand(ImportDeclarationSyntax import)
    {
        var name = import.Name.ToString();
        if (ResolveCanonical(name) is { } type)
        {
            staticOnDemand.Add(type);
            typesOnDemand.Add(type);
            if (!model.IsFullyKnown(type))
                IsComplete = false;
            return;
        }

        IsComplete = false;
        ReportIfCertain(import, name, isPackageImport: false);
    }

    private void ImportModule(string moduleName, ImportDeclarationSyntax? import)
    {
        if (model.Project.PackagesOfModule(moduleName) is { } packages)
        {
            packagesOnDemand.AddRange(packages);
            return;
        }

        IsComplete = false;
        if (import is not null && model.Project.Model.Jdk is { ModuleNames.Count: > 0 } && (moduleName.StartsWith("java.", StringComparison.Ordinal) || moduleName.StartsWith("jdk.", StringComparison.Ordinal)))
            problems.Add(new ImportProblem(import.Name.Span, $"The module {moduleName} does not exist"));
    }

    // An import is certain to be wrong only in a package nothing else adds to: java.* or one only the project's sources have.
    private void ReportIfCertain(ImportDeclarationSyntax import, string name, bool isPackageImport)
    {
        if (!model.Project.HasJdk)
            return;

        if (isPackageImport)
        {
            if (name.StartsWith("java.", StringComparison.Ordinal) || name == "java")
                problems.Add(new ImportProblem(import.Name.Span, $"The package {name} does not exist"));
            return;
        }

        var parts = name.Split('.');
        for (var split = parts.Length - 1; split >= 1; split--)
        {
            var package = string.Join('.', parts, 0, split);
            if (!model.Project.HasTypesIn(package))
                continue;
            if (!IsClosed(package))
                return;

            var outer = package + "." + parts[split];
            if (split == parts.Length - 1 || (model.Project.Contains(outer) && model.IsFullyKnown(outer)))
                problems.Add(new ImportProblem(import.Name.Span, $"The import {name} cannot be resolved"));
            return;
        }
    }

    private bool IsClosed(string package) =>
        package.StartsWith("java.", StringComparison.Ordinal)
        || (!model.Project.Model.MayMissTypes && !model.Project.Model.LibraryPackages.Has(package) && model.Project.HasTypesIn(package));
}
