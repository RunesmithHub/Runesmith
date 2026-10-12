using Runesmith.Sdk.Templates;
using Runesmith.Shell.Pages;

namespace Runesmith.Shell.Tests.Pages;

public sealed class TemplateCatalogTests
{
    private static readonly IProjectTemplateProvider Provider = new NoProvider();

    private static readonly TemplateEntry[] Entries =
    [
        Entry("console", "Console App", "Sample", TemplateCategories.Console, "minimal"),
        Entry("unit", "Unit Test Project", "Sample", TemplateCategories.Test),
        Entry("webapi", "Web API", "Sample", TemplateCategories.Web, "rest", "api"),
        Entry("other.app", "Application", "Other", TemplateCategories.Console, "make", "ninja"),
        Entry("other.lib", "Library", "Other", TemplateCategories.Library),
        Entry("odd", "Odd Thing", "Sample", "Robots"),
    ];

    [Fact]
    public void GroupsFollowTheUsualOrderWithUnknownKindsLast()
    {
        var groups = TemplateCatalog.Rows(Entries, [], null, null, null).OfType<TemplateGroupRow>().Select(g => (g.Title, g.Count));

        Assert.Equal([("Console", 2), ("Library", 1), ("Web", 1), ("Test", 1), ("Robots", 1)], groups);
    }

    [Fact]
    public void RecentlyUsedTemplatesComeFirstUnlessSearching()
    {
        var rows = TemplateCatalog.Rows(Entries, ["other.lib", "missing"], null, null, null);
        Assert.Equal(new TemplateGroupRow(TemplateCatalog.RecentGroup, 1), rows[0]);
        Assert.Equal("other.lib", ((TemplateRow)rows[1]).Entry.Template.Id);

        var searched = TemplateCatalog.Rows(Entries, ["other.lib"], "lib", null, null);
        Assert.DoesNotContain(searched.OfType<TemplateGroupRow>(), g => g.Title == TemplateCatalog.RecentGroup);
    }

    [Fact]
    public void EveryWordOfASearchMustMatchTheNameTagsLanguageOrKind()
    {
        Assert.Equal(["other.app"], Ids(TemplateCatalog.Rows(Entries, [], "other ninja", null, null)));
        Assert.Equal(["webapi"], Ids(TemplateCatalog.Rows(Entries, [], "REST", null, null)));
        Assert.Equal(["unit"], Ids(TemplateCatalog.Rows(Entries, [], "test sample", null, null)));
        Assert.Empty(TemplateCatalog.Rows(Entries, [], "nothing like this", null, null));
    }

    [Fact]
    public void LanguageAndKindFiltersCombine()
    {
        Assert.Equal(["other.app", "console"], Ids(TemplateCatalog.Rows(Entries, [], null, null, TemplateCategories.Console)));
        Assert.Equal(["other.app"], Ids(TemplateCatalog.Rows(Entries, [], null, "other", TemplateCategories.Console)));
        Assert.Equal(["Sample", "Other"], TemplateCatalog.Languages(Entries));
        Assert.Equal(["Console", "Library", "Web", "Test", "Robots"], TemplateCatalog.Categories(Entries));
    }

    private static string[] Ids(IEnumerable<TemplateListRow> rows) => [.. rows.OfType<TemplateRow>().Select(r => r.Entry.Template.Id)];

    private static TemplateEntry Entry(string id, string name, string language, string category, params string[] tags) =>
        new(new ProjectTemplate(id, name, language, category) { Tags = tags }, Provider);

    private sealed class NoProvider : IProjectTemplateProvider
    {
        public Task<IReadOnlyList<ProjectTemplate>> GetTemplatesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProjectTemplate>>([]);

        public Task<ProjectCreationResult> CreateAsync(ProjectTemplate template, ProjectCreationRequest request, IProgress<string> progress, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
