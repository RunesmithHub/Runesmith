using System.Text;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Templates;

namespace Runesmith.Shell.Pages;

/// <summary>The New Project dialog's own fields, Name, Location and Create Git repository, put in front of a template's options, and the
/// rules that check them.</summary>
internal static class NewProjectForm
{
    /// <summary>The id of the project's name; the dialog's ids start with <c>@</c> so they never meet a template's.</summary>
    public const string Name = "@name";

    /// <summary>The id of the folder the project's folder is created in.</summary>
    public const string Location = "@location";

    /// <summary>The id of Create Git repository.</summary>
    public const string Git = "@git";

    /// <summary>Gets the dialog's fields followed by the template's options.</summary>
    public static OptionSet Options(ProjectTemplate template, bool offerGit)
    {
        ArgumentNullException.ThrowIfNull(template);
        List<Option> options =
        [
            new(Name, "Name", OptionKind.Text) { IsRequired = true, Placeholder = "MyProject" },
            new(Location, "Location", OptionKind.Path) { IsRequired = true, PathKind = PathKind.Folder },
        ];
        if (offerGit)
            options.Add(new Option(Git, "Create Git repository", OptionKind.Toggle));
        options.AddRange(template.Options.Options);
        return new OptionSet(options);
    }

    /// <summary>Gets the values a form for a template starts with: its defaults, then the values the user changed last time.</summary>
    public static OptionValues InitialValues(ProjectTemplate template, IReadOnlyDictionary<string, string>? remembered)
    {
        ArgumentNullException.ThrowIfNull(template);
        var values = template.Options.Defaults();
        if (remembered is null)
            return values;

        foreach (var option in template.Options.Options)
        {
            if (remembered.TryGetValue(option.Id, out var value))
                values.Set(option.Id, value);
        }

        return values;
    }

    /// <summary>Gets the template's own values, without the dialog's fields.</summary>
    public static OptionValues TemplateValues(OptionValues all)
    {
        ArgumentNullException.ThrowIfNull(all);
        return new OptionValues(all.All.Where(pair => !pair.Key.StartsWith('@')).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    /// <summary>Gets the template's values that differ from its defaults, which the dialog remembers for next time; SDK choices follow the
    /// default SDK instead, so they are not remembered.</summary>
    public static IReadOnlyDictionary<string, string> ChangedValues(ProjectTemplate template, OptionValues all)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(all);
        var defaults = template.Options.Defaults();
        var changed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var option in template.Options.Options)
        {
            if (option.Kind != OptionKind.Sdk && all.Get(option.Id) is { } value && value != (defaults.Get(option.Id) ?? ""))
                changed[option.Id] = value;
        }

        return changed;
    }

    /// <summary>Gets the folder the project is created in, or null while the name or location is missing.</summary>
    public static string? TargetFolder(OptionValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var name = values.Get(Name)?.Trim();
        var location = values.Get(Location)?.Trim();
        return string.IsNullOrEmpty(name) || string.IsNullOrEmpty(location) || !Path.IsPathRooted(location) ? null : Path.Combine(location, name);
    }

    /// <summary>Checks the name and location: a name that can be a folder's, a full location, and no folder with files in the way.</summary>
    public static IReadOnlyDictionary<string, string> Validate(OptionValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var problems = new Dictionary<string, string>(StringComparer.Ordinal);
        var name = values.Get(Name) ?? "";
        var location = values.Get(Location)?.Trim() ?? "";
        if (name.Length > 0 && NameProblem(name) is { } nameProblem)
            problems[Name] = nameProblem;
        if (location.Length > 0 && !Path.IsPathRooted(location))
            problems[Location] = "Type a full path, or choose a folder.";
        else if (!problems.ContainsKey(Name) && TargetFolder(values) is { } folder && Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
            problems[Name] = $"{Path.GetFileName(folder)} exists in this location and is not empty.";
        return problems;
    }

    /// <summary>Says why a name cannot be a project's folder name, or returns null when it can.</summary>
    public static string? NameProblem(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Trim().Length == 0)
            return "Type a name.";
        if (name != name.Trim())
            return "A name cannot start or end with a space.";
        if (name is "." or ".." || name.EndsWith('.'))
            return "A name cannot end with a dot.";
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0)
            return "A name cannot contain / \\ : * ? \" < > |.";
        return null;
    }

    /// <summary>Gets a name for a new project from a template's name, such as ConsoleApp1, that no folder in the location has yet.</summary>
    public static string SuggestName(ProjectTemplate template, string? location)
    {
        ArgumentNullException.ThrowIfNull(template);
        var stem = new StringBuilder();
        foreach (var word in template.Name.Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries))
        {
            var letters = new string([.. word.Where(char.IsLetterOrDigit)]);
            if (letters.Length > 0)
                stem.Append(char.ToUpperInvariant(letters[0])).Append(letters.AsSpan(1));
        }

        var baseName = stem.Length == 0 || char.IsDigit(stem[0]) ? "Project" : stem.ToString();
        for (var number = 1; ; number++)
        {
            var candidate = baseName + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(location) || !Path.Exists(Path.Combine(location, candidate)))
                return candidate;
        }
    }
}
