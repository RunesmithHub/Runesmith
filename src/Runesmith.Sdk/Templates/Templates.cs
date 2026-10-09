using Runesmith.Sdk.Options;

namespace Runesmith.Sdk.Templates;

/// <summary>A kind of project a template makes; the New Project dialog groups and filters by it.</summary>
public static class TemplateCategories
{
    public const string Console = "Console";
    public const string Library = "Library";
    public const string Web = "Web";
    public const string Desktop = "Desktop";
    public const string Test = "Test";
    public const string Other = "Other";
}

/// <summary>A template a new project can be made from.</summary>
/// <param name="Id">A unique id; the dialog remembers values per template by it.</param>
/// <param name="Name">The name in the list, such as "Console App".</param>
/// <param name="Language">The language, such as "C#" or "Java".</param>
/// <param name="Category">The kind of project, one of <see cref="TemplateCategories"/>.</param>
public sealed record ProjectTemplate(string Id, string Name, string Language, string Category)
{
    /// <summary>Gets the description shown under the name and in About this template.</summary>
    public string? Description { get; init; }

    /// <summary>Gets words search matches besides the name, such as "web" or "minimal api".</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Gets the icon's name.</summary>
    public string Icon { get; init; } = "file-code";

    /// <summary>Gets the template's author, when known.</summary>
    public string? Author { get; init; }

    /// <summary>Gets where the template comes from, such as ".NET SDK 10.0.401" or a package's name.</summary>
    public string? Source { get; init; }

    /// <summary>Gets the template's own options; Name and Location are the dialog's.</summary>
    public OptionSet Options { get; init; } = OptionSet.Empty;

    /// <summary>Gets what the provider needs to create the project, such as the template's package; only the provider reads it.</summary>
    public object? Data { get; init; }
}

/// <summary>What the user asked to create.</summary>
/// <param name="Name">The project's name.</param>
/// <param name="Location">The folder the project's folder is created in.</param>
/// <param name="Values">The template's option values.</param>
public sealed record ProjectCreationRequest(string Name, string Location, OptionValues Values);

/// <summary>What was created.</summary>
/// <param name="Folder">The folder to open.</param>
/// <param name="FilesToOpen">Files to open in editors, such as the program's main file.</param>
public sealed record ProjectCreationResult(string Folder, IReadOnlyList<string> FilesToOpen);

/// <summary>Supplies project templates and creates projects from them. Export it with <c>[Export(typeof(IProjectTemplateProvider))]</c>.</summary>
public interface IProjectTemplateProvider
{
    /// <summary>Gets the templates; called each time the New Project dialog opens, so it should answer from a cache when it can.</summary>
    Task<IReadOnlyList<ProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken);

    /// <summary>Creates a project from one of this provider's templates, reporting each step.</summary>
    Task<ProjectCreationResult> CreateAsync(ProjectTemplate template, ProjectCreationRequest request, IProgress<string> progress,
        CancellationToken cancellationToken);
}
