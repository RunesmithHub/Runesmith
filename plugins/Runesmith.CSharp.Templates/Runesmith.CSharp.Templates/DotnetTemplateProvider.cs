using System.Collections.Concurrent;
using System.Composition;
using System.Globalization;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Sdks;
using Runesmith.Sdk.Templates;

namespace Runesmith.CSharp.Templates;

/// <summary>Offers the project templates of the .NET SDK and the template packages the user installed with <c>dotnet new install</c>, and
/// creates projects with <c>dotnet new</c>.</summary>
[Export(typeof(IProjectTemplateProvider))]
[Shared]
public sealed class DotnetTemplateProvider : IProjectTemplateProvider
{
    private readonly ConcurrentDictionary<string, (DateTime Written, IReadOnlyList<TemplateDefinition> Templates)> cache = new(StringComparer.Ordinal);
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> findRoots;
    private readonly string templateEngineFolder;

    /// <summary>Creates the provider, finding .NET through the SDK service when Runesmith has one and through the environment otherwise.</summary>
    [ImportingConstructor]
    public DotnetTemplateProvider([Import(AllowDefault = true)] ISdkService? sdks)
        : this(cancellationToken => FindRootsAsync(sdks, cancellationToken), TemplatePackages.TemplateEngineFolder())
    {
    }

    internal DotnetTemplateProvider(Func<CancellationToken, Task<IReadOnlyList<string>>> findRoots, string templateEngineFolder)
    {
        this.findRoots = findRoots;
        this.templateEngineFolder = templateEngineFolder;
    }

    /// <summary>Gets or sets whether <c>dotnet new</c> skips restoring the new project, for tests that work offline.</summary>
    internal bool SkipRestore { get; set; }

    /// <summary>Gets or sets the <c>dotnet</c> to run when the user picked no SDK; null finds it on the <c>PATH</c>.</summary>
    internal string? DefaultDotnet { get; set; }

    public async Task<IReadOnlyList<ProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken)
    {
        var roots = await findRoots(cancellationToken).ConfigureAwait(false);
        var packages = roots.SelectMany(TemplatePackages.FindSdkPackages).Concat(TemplatePackages.FindInstalledPackages(templateEngineFolder)).ToList();
        var culture = CultureInfo.CurrentUICulture;
        var read = await Task.WhenAll(packages.Select(package => Task.Run(() => Read(package, culture), cancellationToken))).ConfigureAwait(false);
        return TemplateCatalog.Build(read.SelectMany(templates => templates));
    }

    public async Task<ProjectCreationResult> CreateAsync(ProjectTemplate template, ProjectCreationRequest request, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        if (template.Data is not DotnetTemplateData data)
            throw new ArgumentException("The template is not one of the .NET templates.", nameof(template));

        var values = request.Values;
        var folder = Path.GetFullPath(Path.Combine(request.Location, request.Name));
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            throw new InvalidOperationException($"{folder} exists already and is not empty.");
        Directory.CreateDirectory(folder);

        var (dotnet, root) = ChooseDotnet(values.Get(TemplateCatalog.SdkOption));
        if (data.IsBlankSolution)
        {
            progress.Report("Creating the solution");
            var blank = await CreateSolutionAsync(dotnet, root, folder, request.Name, cancellationToken).ConfigureAwait(false);
            return new ProjectCreationResult(folder, [blank]);
        }

        var language = values.Get(TemplateCatalog.LanguageOption) is { } chosen && data.Variants.ContainsKey(chosen) ? chosen : data.PrimaryLanguage;
        var variant = data.Variants[language];
        if (data.IsSolution)
        {
            progress.Report($"Creating the solution with dotnet new {variant.ShortName}");
            await DotnetCli.RunAsync(dotnet, root, folder, NewProjectArguments(variant, request.Name, folder, values), cancellationToken).ConfigureAwait(false);
            var created = Directory.EnumerateFiles(folder, "*.sln*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault();
            return new ProjectCreationResult(folder, created is null ? [] : [created]);
        }

        var sameDirectory = values.GetBool(TemplateCatalog.SameDirectoryOption);
        var projectFolder = sameDirectory ? folder : Path.Combine(folder, request.Name);

        progress.Report($"Creating the project with dotnet new {variant.ShortName}");
        await DotnetCli.RunAsync(dotnet, root, folder, NewProjectArguments(variant, request.Name, projectFolder, values), cancellationToken)
            .ConfigureAwait(false);
        var project = Directory.EnumerateFiles(projectFolder, "*.*proj").Order(StringComparer.Ordinal).FirstOrDefault();

        if (!sameDirectory && project is not null)
        {
            var solutionName = values.Get(TemplateCatalog.SolutionNameOption) is { Length: > 0 } name ? name.Trim() : request.Name;
            progress.Report("Creating the solution");
            var solution = await CreateSolutionAsync(dotnet, root, folder, solutionName, cancellationToken).ConfigureAwait(false);
            progress.Report("Adding the project to the solution");
            await DotnetCli.RunAsync(dotnet, root, folder, ["sln", solution, "add", project], cancellationToken).ConfigureAwait(false);
        }

        return new ProjectCreationResult(folder, MainFile(projectFolder, project) is { } main ? [main] : []);
    }

    /// <summary>Gets the arguments of <c>dotnet new</c> for a template, passing only the parameters that differ from their defaults.</summary>
    internal IReadOnlyList<string> NewProjectArguments(TemplateDefinition variant, string name, string output, OptionValues values)
    {
        var arguments = new List<string> { "new", variant.ShortName };
        if (variant.Language.Length > 0)
            arguments.AddRange(["--language", variant.Language]);
        arguments.AddRange(["--type", variant.Type is { Length: > 0 } type ? type.ToLowerInvariant() : "project", "--name", name, "--output", output, "--no-update-check"]);
        foreach (var parameter in variant.Parameters)
        {
            if (values.Get(parameter.Name) is not { } value || IsDefault(parameter, value))
                continue;
            arguments.AddRange(["--" + parameter.CliName, value]);
        }

        if (SkipRestore && variant.SkipRestoreCliName is { } skipRestore)
            arguments.AddRange(["--" + skipRestore, "true"]);
        return arguments;
    }

    private static bool IsDefault(TemplateParameter parameter, string value) => parameter.Kind switch
    {
        OptionKind.Toggle => string.Equals(value, parameter.Default ?? "false", StringComparison.OrdinalIgnoreCase),
        _ => value.Length == 0 ? string.IsNullOrEmpty(parameter.Default) : value == parameter.Default,
    };

    private static async Task<string> CreateSolutionAsync(string dotnet, string? root, string folder, string name, CancellationToken cancellationToken)
    {
        string[] arguments = ["new", "sln", "--name", name, "--output", folder, "--no-update-check"];
        try
        {
            await DotnetCli.RunAsync(dotnet, root, folder, [.. arguments, "--format", "slnx"], cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException) when (!File.Exists(Path.Combine(folder, name + ".slnx")))
        {
            // SDKs before 9.0.200 have no --format and make a .sln.
            await DotnetCli.RunAsync(dotnet, root, folder, arguments, cancellationToken).ConfigureAwait(false);
        }

        var slnx = Path.Combine(folder, name + ".slnx");
        return File.Exists(slnx) ? slnx : Path.Combine(folder, name + ".sln");
    }

    private (string Dotnet, string? Root) ChooseDotnet(string? sdk)
    {
        if (sdk is { Length: > 0 } && File.Exists(TemplatePackages.DotnetIn(sdk)))
            return (TemplatePackages.DotnetIn(sdk), sdk);
        return (DefaultDotnet ?? TemplatePackages.FindDotnetOnPath() ?? "dotnet", null);
    }

    private static string? MainFile(string projectFolder, string? project)
    {
        foreach (var name in (string[])["Program.cs", "Program.fs", "Program.vb"])
        {
            var path = Path.Combine(projectFolder, name);
            if (File.Exists(path))
                return path;
        }

        if (!Directory.Exists(projectFolder))
            return project;
        return Directory.EnumerateFiles(projectFolder, "*.*", SearchOption.TopDirectoryOnly)
            .Where(f => Path.GetExtension(f) is ".cs" or ".fs" or ".vb")
            .Order(StringComparer.Ordinal)
            .FirstOrDefault() ?? project;
    }

    private IReadOnlyList<TemplateDefinition> Read(TemplatePackage package, CultureInfo culture)
    {
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(package.Path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (cache.TryGetValue(package.Path, out var cached) && cached.Written == written)
            return cached.Templates;

        IReadOnlyList<TemplateDefinition> templates;
        try
        {
            var source = package.SdkTemplatesVersion is not null ? ".NET SDK " + Path.GetFileName(Path.GetDirectoryName(package.Path)) : null;
            templates = TemplatePackageReader.Read(package.Path, source, package.SdkTemplatesVersion, culture);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            templates = [];
        }

        cache[package.Path] = (written, templates);
        return templates;
    }

    private static async Task<IReadOnlyList<string>> FindRootsAsync(ISdkService? sdks, CancellationToken cancellationToken)
    {
        var roots = new List<string>();
        if (sdks is not null)
            roots.AddRange((await sdks.GetInstalledAsync("dotnet", cancellationToken).ConfigureAwait(false)).Select(sdk => sdk.Path));
        roots.AddRange(TemplatePackages.FindRootsFromEnvironment());
        return [.. roots.Where(Directory.Exists).Select(Path.GetFullPath).Distinct(OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase)];
    }
}
