using Avalonia.Controls;
using Runesmith.Sdk.Editors;
using Runesmith.Shell.Editors.Custom;

namespace Runesmith.Shell.Tests.Editors.Custom;

public sealed class EditorCatalogTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-editor-catalog-").FullName;

    private string AssociationsPath => Path.Combine(folder, "editor-associations.json");

    public void Dispose() => TestFolders.Delete(folder);

    [Theory]
    [InlineData("*.png", "logo.png", true)]
    [InlineData("*.png", "LOGO.PNG", true)]
    [InlineData("*.png", "logo.png.txt", false)]
    [InlineData("Dockerfile", "Dockerfile", true)]
    [InlineData("Dockerfile", "Dockerfile.dev", false)]
    [InlineData("*.drawio*", "plan.drawio.svg", true)]
    [InlineData("icon-?.ico", "icon-a.ico", true)]
    [InlineData("icon-?.ico", "icon-ab.ico", false)]
    public void MatchesFileNamesAgainstPatterns(string pattern, string name, bool matches) => Assert.Equal(matches, FilePatterns.Matches(pattern, name));

    [Theory]
    [InlineData("/work/Logo.PNG", "*.png")]
    [InlineData("/work/Dockerfile", "Dockerfile")]
    [InlineData("/work/.gitignore", ".gitignore")]
    public void KeepsTheDefaultUnderTheFilesExtension(string path, string key) => Assert.Equal(key, FilePatterns.KeyOf(path));

    [Fact]
    public void OffersTheTextEditorFirstAndTheMatchingEditorsAfterIt()
    {
        var catalog = Catalog(Entry("acme.diagrams", "Diagrams", ["*.drawio"]), Entry("runesmith.imageViewer", "Image Viewer", ["*.png"], builtIn: true), Entry("acme.pixels", "Pixel Editor", ["*.png"], EditorPriority.Optional));

        var choices = catalog.GetChoices("/work/logo.png");

        Assert.Equal([EditorProviderDefinition.TextEditorId, "acme.pixels", "runesmith.imageViewer"], choices.Select(c => c.Id));
        Assert.Equal("runesmith.imageViewer", Assert.Single(choices, c => c.IsDefault).Id);
        Assert.Equal([EditorProviderDefinition.TextEditorId], catalog.GetChoices("/work/notes.txt").Select(c => c.Id));
        Assert.True(Assert.Single(catalog.GetChoices("/work/notes.txt")).IsDefault);
    }

    [Fact]
    public void APluginsDefaultEditorComesBeforeRunesmithsOwn()
    {
        var catalog = Catalog(Entry("runesmith.imageViewer", "Image Viewer", ["*.png"], builtIn: true), Entry("acme.pixels", "Pixel Editor", ["*.png"]));

        Assert.Equal("acme.pixels", catalog.DefaultFor("/work/logo.png"));
    }

    [Fact]
    public void AnOptionalEditorIsOnlyOffered()
    {
        var catalog = Catalog(Entry("acme.hex", "Hex Editor", ["*"], EditorPriority.Optional));

        Assert.Equal(EditorProviderDefinition.TextEditorId, catalog.DefaultFor("/work/data.bin"));
        Assert.Contains(catalog.GetChoices("/work/data.bin"), c => c.Id == "acme.hex");
    }

    [Fact]
    public void TheUsersChoiceIsKeptPerPatternAndReadAgain()
    {
        var catalog = Catalog(Entry("runesmith.imageViewer", "Image Viewer", ["*.png", "*.gif"], builtIn: true));

        catalog.Associations.Set(FilePatterns.KeyOf("/work/logo.png"), EditorProviderDefinition.TextEditorId);

        Assert.Equal(EditorProviderDefinition.TextEditorId, catalog.DefaultFor("/work/other.PNG"));
        Assert.Equal("runesmith.imageViewer", catalog.DefaultFor("/work/anim.gif"));
        var reread = new EditorCatalog(catalog.Providers, new EditorAssociations(AssociationsPath));
        Assert.Equal(EditorProviderDefinition.TextEditorId, reread.DefaultFor("/work/logo.png"));
        Assert.Contains("\"*.png\": \"runesmith.text\"", File.ReadAllText(AssociationsPath), StringComparison.Ordinal);

        reread.Associations.Set("*.png", null);
        Assert.Equal("runesmith.imageViewer", new EditorCatalog(catalog.Providers, new EditorAssociations(AssociationsPath)).DefaultFor("/work/logo.png"));
    }

    [Fact]
    public void AChoiceOfAnEditorThatIsGoneFallsBackToTheDefault()
    {
        new EditorAssociations(AssociationsPath).Set("*.png", "acme.removed");

        Assert.Equal("runesmith.imageViewer", Catalog(Entry("runesmith.imageViewer", "Image Viewer", ["*.png"], builtIn: true)).DefaultFor("/work/logo.png"));
    }

    [Fact]
    public void ADamagedAssociationsFileReadsAsNoChoices()
    {
        File.WriteAllText(AssociationsPath, "{ not json");

        Assert.Null(new EditorAssociations(AssociationsPath).Get("*.png"));
    }

    private EditorCatalog Catalog(params EditorEntry[] entries) => new(entries, new EditorAssociations(AssociationsPath));

    private static EditorEntry Entry(string id, string name, string[] patterns, EditorPriority priority = EditorPriority.Default, bool builtIn = false) =>
        new(new EditorProviderDefinition(id, name, patterns) { Priority = priority }, builtIn, new NoProvider());

    private sealed class NoProvider : IEditorProvider
    {
        public EditorProviderDefinition Definition => throw new NotSupportedException();

        public Task<CustomEditor> CreateEditorAsync(CustomEditorContext context, CancellationToken cancellationToken) => Task.FromResult<CustomEditor>(new Blank());
    }

    private sealed class Blank : CustomEditor
    {
        public override Control Content { get; } = new Border();
    }
}
