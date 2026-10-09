using Runesmith.Sdk.Options;

namespace Runesmith.CSharp.Templates;

/// <summary>A parameter of a template that the user can set, as <c>dotnet new</c> takes it.</summary>
/// <param name="Name">The symbol's name in <c>template.json</c>; the option id.</param>
/// <param name="CliName">The option <c>dotnet new</c> takes it as, without the leading dashes.</param>
/// <param name="Kind">The kind of option it becomes.</param>
/// <param name="Label">The label, localized and without accelerator markers.</param>
internal sealed record TemplateParameter(string Name, string CliName, OptionKind Kind, string Label)
{
    public string? Description { get; init; }

    public string? Default { get; init; }

    public bool IsRequired { get; init; }

    public IReadOnlyList<OptionChoice> Choices { get; init; } = [];

    /// <summary>Gets whether the parameter shows with the main options rather than under Advanced.</summary>
    public bool IsMain { get; init; }
}

/// <summary>One language variant of a project template, read from its package's <c>.template.config</c> folder.</summary>
/// <param name="Identity">The template's unique identity.</param>
/// <param name="Name">The display name, localized.</param>
/// <param name="ShortName">The first short name, which <c>dotnet new</c> takes.</param>
/// <param name="Language">The language, such as C# or F#, or empty when the template has none.</param>
internal sealed record TemplateDefinition(string Identity, string Name, string ShortName, string Language)
{
    public string? GroupIdentity { get; init; }

    public string Type { get; init; } = "";

    public string? Author { get; init; }

    public string? Description { get; init; }

    public IReadOnlyList<string> Classifications { get; init; } = [];

    /// <summary>Gets the parameters the user can set, in the order of <c>template.json</c>.</summary>
    public IReadOnlyList<TemplateParameter> Parameters { get; init; } = [];

    /// <summary>Gets the option that skips restoring the project, such as <c>no-restore</c>, when the template has one.</summary>
    public string? SkipRestoreCliName { get; init; }

    public int Precedence { get; init; }

    /// <summary>Gets where the template comes from, such as ".NET SDK 10.0.12" or a package's id and version.</summary>
    public string? Source { get; init; }

    public string PackagePath { get; init; } = "";

    /// <summary>Gets the version of the SDK's templates folder the package is in, or null for an installed package.</summary>
    public Version? SdkTemplatesVersion { get; init; }

    /// <summary>Gets the key templates of one kind in several languages share.</summary>
    public string GroupKey => GroupIdentity ?? ShortName;
}
