using Runesmith.Sdk.Options;
using Runesmith.Sdk.Templates;

namespace Runesmith.CSharp.Templates;

/// <summary>What <see cref="DotnetTemplateProvider"/> needs to create a project from one of its templates.</summary>
/// <param name="Variants">The template in each language it comes in, by language.</param>
/// <param name="PrimaryLanguage">The language selected at first.</param>
internal sealed record DotnetTemplateData(IReadOnlyDictionary<string, TemplateDefinition> Variants, string PrimaryLanguage)
{
    /// <summary>Gets whether the template makes an empty solution rather than running a project template.</summary>
    public bool IsBlankSolution { get; init; }

    /// <summary>Gets whether the template makes a whole solution itself, so no solution is created around it.</summary>
    public bool IsSolution { get; init; }
}

/// <summary>Turns the templates read from packages into the New Project dialog's templates: project and solution templates, one per kind
/// with a Language option when it comes in several languages.</summary>
internal static class TemplateCatalog
{
    public const string LanguageOption = "$language";
    public const string SdkOption = "$sdk";
    public const string SolutionNameOption = "$solutionName";
    public const string SameDirectoryOption = "$sameDirectory";
    public const string FrameworkParameter = "Framework";
    public const string BlankSolutionId = "dotnet:blank-solution";

    private static readonly string[] LanguageOrder = ["C#", "F#", "VB"];

    /// <summary>Builds the dialog's templates, sorted by name, with a Blank Solution among them.</summary>
    public static IReadOnlyList<ProjectTemplate> Build(IEnumerable<TemplateDefinition> definitions)
    {
        var chosen = definitions
            .Where(d => IsProject(d) || (IsSolution(d) && !string.Equals(d.ShortName, "sln", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(d => (d.GroupKey, d.Language))
            .Select(g => g.OrderByDescending(d => d.Precedence).ThenByDescending(d => d.SdkTemplatesVersion ?? new Version(int.MaxValue, 0)).First());

        var templates = chosen
            .GroupBy(d => d.GroupKey, StringComparer.Ordinal)
            .Select(g => Merge([.. g.OrderBy(d => LanguageRank(d.Language))]))
            .Append(BlankSolution())
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return templates;
    }

    /// <summary>Gets the category a template's classifications put it in; test templates also say Desktop and Web, so Test comes first.</summary>
    public static string Categorize(IReadOnlyCollection<string> classifications)
    {
        bool Any(params string[] names) => classifications.Any(c => names.Contains(c, StringComparer.OrdinalIgnoreCase));

        if (Any("Test", "xUnit", "MSTest", "NUnit", "Playwright"))
            return TemplateCategories.Test;
        if (Any("Web", "ASP.NET", "Blazor", "API", "Web API", "Service", "gRPC", "Razor", "MVC", "WebAssembly", "Worker", "Cloud"))
            return TemplateCategories.Web;
        if (Any("Desktop", "WinForms", "WPF", "Avalonia", "MAUI", "Xaml", "WinUI", "Mobile"))
            return TemplateCategories.Desktop;
        if (Any("Library"))
            return TemplateCategories.Library;
        if (Any("Console"))
            return TemplateCategories.Console;
        return TemplateCategories.Other;
    }

    private static bool IsProject(TemplateDefinition definition) => string.Equals(definition.Type, "project", StringComparison.OrdinalIgnoreCase);

    private static bool IsSolution(TemplateDefinition definition) => string.Equals(definition.Type, "solution", StringComparison.OrdinalIgnoreCase);

    private static ProjectTemplate Merge(IReadOnlyList<TemplateDefinition> variants)
    {
        var primary = variants[0];
        var isSolution = IsSolution(primary);
        var languages = variants.Select(v => v.Language).ToList();
        var category = Categorize(primary.Classifications);
        var options = new List<Option>();
        if (languages.Count > 1)
        {
            options.Add(new Option(LanguageOption, "Language", OptionKind.Choice)
            {
                Choices = [.. languages.Select(l => new OptionChoice(l, l.Length == 0 ? "Default" : l) { Description = LanguageName(l) })],
                Default = primary.Language,
            });
        }

        var parameters = new List<(TemplateParameter Parameter, List<string> Languages)>();
        foreach (var variant in variants)
        {
            foreach (var parameter in variant.Parameters)
            {
                var index = parameters.FindIndex(p => p.Parameter.Name == parameter.Name);
                if (index < 0)
                    parameters.Add((parameter, [variant.Language]));
                else
                    parameters[index].Languages.Add(variant.Language);
            }
        }

        Option ToOption((TemplateParameter Parameter, List<string> Languages) entry) =>
            new(entry.Parameter.Name, entry.Parameter.Label, entry.Parameter.Kind)
            {
                Description = entry.Parameter.Description,
                Default = entry.Parameter.Default,
                IsRequired = entry.Parameter.IsRequired,
                Choices = entry.Parameter.Choices,
                Group = entry.Parameter.IsMain ? OptionGroup.Main : OptionGroup.Advanced,
                VisibleWhen = entry.Languages.Count < languages.Count ? new OptionCondition(LanguageOption, entry.Languages) : null,
            };

        options.AddRange(parameters.Where(p => p.Parameter.Name == FrameworkParameter).Select(ToOption));
        if (!isSolution)
            options.AddRange(SolutionOptions());
        options.AddRange(parameters.Where(p => p.Parameter.Name != FrameworkParameter && p.Parameter.IsMain).Select(ToOption));
        options.AddRange(parameters.Where(p => p.Parameter.Name != FrameworkParameter && !p.Parameter.IsMain).Select(ToOption));

        var tags = primary.Classifications
            .Concat(variants.Select(v => v.ShortName))
            .Concat(languages.Where(l => l.Length > 0))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ProjectTemplate("dotnet:" + primary.GroupKey, primary.Name, primary.Language.Length == 0 ? "C#" : primary.Language, category)
        {
            Description = primary.Description,
            Tags = tags,
            Icon = Icon(category),
            Author = primary.Author,
            Source = primary.Source,
            Options = new OptionSet(options),
            Data = new DotnetTemplateData(variants.ToDictionary(v => v.Language, StringComparer.Ordinal), primary.Language) { IsSolution = isSolution },
        };
    }

    private static IEnumerable<Option> SolutionOptions()
    {
        yield return SdkOptionDefinition();
        yield return new Option(SolutionNameOption, "Solution name", OptionKind.Text)
        {
            Placeholder = "Same as the project name",
            Description = "The name of the .slnx file.",
            Pattern = """^[^\\/:*?"<>|]*$""",
            PatternMessage = "A solution name cannot contain \\ / : * ? \" < > or |.",
            VisibleWhen = new OptionCondition(SameDirectoryOption, ["false", ""]),
        };
        yield return new Option(SameDirectoryOption, "Put solution and project in the same directory", OptionKind.Toggle)
        {
            Default = "false",
            Description = "Creates only the project, without a solution.",
        };
    }

    private static Option SdkOptionDefinition() => new(SdkOption, ".NET SDK", OptionKind.Sdk)
    {
        SdkKind = "dotnet",
        Description = "The SDK that runs dotnet new.",
    };

    private static ProjectTemplate BlankSolution() =>
        new(BlankSolutionId, "Blank Solution", "C#", TemplateCategories.Other)
        {
            Description = "An empty .slnx solution to add projects to.",
            Tags = ["sln", "slnx", "solution", "empty"],
            Icon = "folder",
            Author = "Microsoft",
            Source = ".NET SDK",
            Options = new OptionSet([SdkOptionDefinition()]),
            Data = new DotnetTemplateData(new Dictionary<string, TemplateDefinition>(), "C#") { IsBlankSolution = true },
        };

    private static string Icon(string category) => category switch
    {
        TemplateCategories.Console => "terminal",
        TemplateCategories.Library => "package",
        TemplateCategories.Web => "compass",
        TemplateCategories.Desktop => "monitor",
        TemplateCategories.Test => "check-circle",
        _ => "box",
    };

    private static int LanguageRank(string language) => Array.IndexOf(LanguageOrder, language) is var index and >= 0 ? index : LanguageOrder.Length;

    private static string LanguageName(string language) => language switch
    {
        "VB" => "Visual Basic",
        _ => language,
    };
}
