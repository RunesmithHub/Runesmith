using Runesmith.Sdk.Templates;

namespace Runesmith.Shell.Pages;

/// <summary>A template and the provider that creates projects from it.</summary>
internal sealed record TemplateEntry(ProjectTemplate Template, IProjectTemplateProvider Provider);

/// <summary>A row of the New Project dialog's template list: a group's header or a template.</summary>
internal abstract record TemplateListRow;

/// <summary>The header of a group of templates, such as Console or Recent.</summary>
internal sealed record TemplateGroupRow(string Title, int Count) : TemplateListRow;

/// <summary>A template in the list.</summary>
internal sealed record TemplateRow(TemplateEntry Entry) : TemplateListRow;

/// <summary>The templates of every provider, filtered and grouped for the New Project dialog's list.</summary>
internal static class TemplateCatalog
{
    /// <summary>The title of the group of recently used templates.</summary>
    public const string RecentGroup = "Recent";

    private static readonly string[] CategoryOrder =
    [
        TemplateCategories.Console, TemplateCategories.Library, TemplateCategories.Web, TemplateCategories.Desktop, TemplateCategories.Test,
        TemplateCategories.Other,
    ];

    /// <summary>Gets the languages of the templates, most templates first, for the language filter.</summary>
    public static IReadOnlyList<string> Languages(IEnumerable<TemplateEntry> entries) =>
    [
        .. entries.GroupBy(e => e.Template.Language, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Key),
    ];

    /// <summary>Gets the categories of the templates in their usual order, for the category filter.</summary>
    public static IReadOnlyList<string> Categories(IEnumerable<TemplateEntry> entries) =>
        [.. entries.Select(e => e.Template.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(CategoryRank).ThenBy(c => c, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Gets the rows of the list: the recently used templates first, then every group of templates that match.</summary>
    /// <param name="language">The language to show, or null for all.</param>
    /// <param name="category">The category to show, or null for all.</param>
    public static IReadOnlyList<TemplateListRow> Rows(IReadOnlyList<TemplateEntry> entries, IReadOnlyList<string> recent, string? query, string? language,
        string? category)
    {
        var terms = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matching = entries
            .Where(e => language is null || string.Equals(e.Template.Language, language, StringComparison.OrdinalIgnoreCase))
            .Where(e => category is null || string.Equals(e.Template.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(e => terms.All(term => Matches(e.Template, term)))
            .ToList();

        var rows = new List<TemplateListRow>();
        var recentEntries = recent.Select(id => matching.Find(e => e.Template.Id == id)).OfType<TemplateEntry>().ToList();
        if (recentEntries.Count > 0 && terms.Length == 0)
        {
            rows.Add(new TemplateGroupRow(RecentGroup, recentEntries.Count));
            rows.AddRange(recentEntries.Select(e => new TemplateRow(e)));
        }

        foreach (var group in matching.GroupBy(e => e.Template.Category, StringComparer.OrdinalIgnoreCase).OrderBy(g => CategoryRank(g.Key)).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new TemplateGroupRow(group.Key, group.Count()));
            rows.AddRange(group
                .OrderBy(e => terms.Length > 0 && e.Template.Name.StartsWith(terms[0], StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(e => e.Template.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.Template.Language, StringComparer.OrdinalIgnoreCase)
                .Select(e => new TemplateRow(e)));
        }

        return rows;
    }

    /// <summary>Whether a template matches one word of a search: in its name, language, category, description, tags or source.</summary>
    public static bool Matches(ProjectTemplate template, string term) =>
        Contains(template.Name, term)
        || Contains(template.Language, term)
        || Contains(template.Category, term)
        || Contains(template.Description, term)
        || Contains(template.Source, term)
        || template.Tags.Any(tag => Contains(tag, term));

    private static bool Contains(string? text, string term) => text?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;

    private static int CategoryRank(string category)
    {
        var index = Array.FindIndex(CategoryOrder, c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? CategoryOrder.Length - 1 : index;
    }
}
