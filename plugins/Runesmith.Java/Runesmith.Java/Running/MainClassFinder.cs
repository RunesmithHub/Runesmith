using Runesmith.Languages.Java.Projects;
using Runesmith.Languages.Java.Syntax;

namespace Runesmith.Java.Running;

/// <summary>A program a folder's sources can start: a class with a <c>main</c> method, or a compact source file.</summary>
/// <param name="Name">The class's fully qualified name, or the compact file's path relative to the folder, with forward slashes.</param>
/// <param name="SimpleName">The name shown for it: the class's own name, or the file's name.</param>
/// <param name="FilePath">The source file.</param>
/// <param name="IsCompactSourceFile">Whether it is a compact source file, which <c>java</c> runs from its source.</param>
internal sealed record MainEntry(string Name, string SimpleName, string FilePath, bool IsCompactSourceFile);

/// <summary>Finds the classes with a <c>main</c> method, and the compact source files, in a folder's main sources.</summary>
internal static class MainClassFinder
{
    /// <summary>The most files read, so a huge folder cannot make detection run away.</summary>
    public const int MaximumFiles = 5000;

    private const long MaximumFileSize = 1024 * 1024;

    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "target", "build", "out", "bin", "obj", "node_modules",
    };

    /// <summary>Finds the programs of a folder's projects, skipping test sources, build output and hidden folders.</summary>
    public static IReadOnlyList<MainEntry> Find(string rootPath, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(rootPath);
        var found = new List<MainEntry>();
        var read = 0;
        foreach (var file in MainRoots(root).SelectMany(Files))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++read > MaximumFiles)
                break;
            found.AddRange(FindInFile(root, file));
        }

        return found;
    }

    /// <summary>Whether the main sources of a folder's projects declare a module in a <c>module-info.java</c>.</summary>
    public static bool HasModuleDeclaration(string rootPath) =>
        MainRoots(Path.GetFullPath(rootPath)).SelectMany(Files).Take(MaximumFiles).Any(file => Path.GetFileName(file) == "module-info.java");

    /// <summary>Finds the programs one file declares.</summary>
    public static IEnumerable<MainEntry> FindInFile(string rootPath, string file)
    {
        string text;
        try
        {
            if (new FileInfo(file).Length > MaximumFileSize)
                return [];
            text = File.ReadAllText(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return text.Contains("main", StringComparison.Ordinal) ? FindInText(rootPath, file, text) : [];
    }

    private static List<MainEntry> FindInText(string rootPath, string file, string text)
    {
        var unit = JavaSyntaxTree.Parse(text).Root;
        if (unit.IsCompactSourceFile)
        {
            return unit.Members.OfType<MethodDeclarationSyntax>().Any(IsMain)
                ? [new MainEntry(Path.GetRelativePath(rootPath, file).Replace('\\', '/'), Path.GetFileName(file), file, true)]
                : [];
        }

        var package = unit.Package?.Name.ToString();
        return
        [
            .. unit.Members.OfType<TypeDeclarationSyntax>()
                .Where(type => !type.Name.IsMissing && type.Body.Members.OfType<MethodDeclarationSyntax>().Any(IsMain))
                .Select(type => new MainEntry(string.IsNullOrEmpty(package) ? type.Name.Text : $"{package}.{type.Name.Text}", type.Name.Text, file, false)),
        ];
    }

    /// <summary>Whether a method is a launcher's entry point: <c>void main(String[])</c>, static or not, or <c>void main()</c>.</summary>
    public static bool IsMain(MethodDeclarationSyntax method)
    {
        if (method.Name.Text != "main" || method.ReturnType is not PrimitiveTypeSyntax { Kind: PrimitiveTypeKind.Void } || method.Body is null)
            return false;

        var parameters = method.Parameters.Parameters;
        if (parameters.Count == 0)
            return true;
        if (parameters.Count != 1)
            return false;

        var parameter = parameters[0];
        var rank = parameter.Dimensions.Count + (parameter.IsVarargs ? 1 : 0);
        var type = parameter.Type;
        if (type is ArrayTypeSyntax array)
        {
            rank += array.Rank;
            type = array.ElementType;
        }

        return rank == 1 && type is ClassTypeSyntax { QualifiedName: "String" or "java.lang.String" };
    }

    private static IEnumerable<string> MainRoots(string root) =>
        JavaProjectLoader.Load(root).SelectMany(project => project.SourceRoots).Where(source => IsMainRoot(root, source)).Distinct(StringComparer.Ordinal);

    private static bool IsMainRoot(string root, string sourceRoot)
    {
        var parts = Path.GetRelativePath(root, sourceRoot).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !parts.Any(part => part.Equals("test", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> Files(string folder)
    {
        var pending = new Stack<string>([folder]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> files, folders;
            try
            {
                files = Directory.EnumerateFiles(current, "*.java").Order(StringComparer.Ordinal).ToList();
                folders = Directory.EnumerateDirectories(current).ToList();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
                yield return file;
            foreach (var child in folders.OrderDescending(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(child);
                var isTestSources = name.Equals("test", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(current).Equals("src", StringComparison.OrdinalIgnoreCase);
                if (!name.StartsWith('.') && !SkippedFolders.Contains(name) && !isTestSources)
                    pending.Push(child);
            }
        }
    }
}
