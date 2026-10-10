using System.Composition;
using System.Reflection;
using Avalonia.Controls;
using Runesmith.Composition;
using Runesmith.Sdk.Editors;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Editors.Custom;
using Runesmith.Shell.Editors.Images;

namespace Runesmith.Shell.Tests.Editors.Custom;

public sealed class CustomEditorServiceTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-custom-editors-").FullName;

    public void Dispose() => TestFolders.Delete(folder);

    [Fact]
    public async Task OpensFilesInTheirEditorSavesTheChangesAndSwitchesWithOpenWith()
    {
        string[] hostAssemblies = ["Runesmith.Sdk", "Runesmith.Workspace", "Runesmith.Languages", "Runesmith.Editor", "Runesmith.Shell"];
        using var result = await RunesmithComposition.CreateAsync(
            new CompositionOptions([.. hostAssemblies.Select(Assembly.Load), typeof(NoteEditorProvider).Assembly], [], null), TestContext.Current.CancellationToken);
        Assert.Empty(result.Errors);

        await HeadlessSession.Value.Dispatch(async () =>
        {
            var custom = result.Exports.GetExportedValue<CustomEditorService>();
            var editors = result.Exports.GetExportedValue<EditorService>();
            var path = Path.Combine(folder, "list.note");
            await File.WriteAllTextAsync(path, "one");

            Assert.Contains(custom.Catalog.Providers, p => p.Definition.Id == NoteEditorProvider.Id);
            Assert.Contains(custom.Catalog.Providers, p => p.Definition.Id == ImageViewerProvider.Id && p.IsBuiltIn);
            Assert.Equal([EditorProviderDefinition.TextEditorId, NoteEditorProvider.Id], custom.GetChoices(path).Select(c => c.Id));
            Assert.Equal(NoteEditorProvider.Id, Assert.Single(custom.GetChoices(path), c => c.IsDefault).Id);

            Assert.Null(await editors.OpenAsync(path));
            var panel = Assert.Single(custom.Panels);
            var note = Assert.IsType<NoteEditor>(panel.Editor);
            Assert.Equal("one", note.Text);
            Assert.False(panel.IsModified);
            var changes = 0;
            panel.PropertyChanged += (_, e) => changes += e.PropertyName == nameof(panel.IsModified) ? 1 : 0;

            note.Type("two");
            Assert.True(panel.IsModified);
            Assert.True(note.CanUndo);
            Assert.True(await custom.SaveAsync(panel));
            Assert.Equal("onetwo", await File.ReadAllTextAsync(path));
            Assert.False(panel.IsModified);
            Assert.Equal(2, changes);

            note.Undo();
            Assert.Equal("one", note.Text);
            Assert.True(panel.IsModified);
            await custom.RevertAsync(panel);
            Assert.Equal("onetwo", note.Text);
            Assert.False(panel.IsModified);

            Assert.True(await custom.OpenWithAsync(path, EditorProviderDefinition.TextEditorId));
            Assert.Empty(custom.Panels);
            Assert.True(note.IsDisposed);
            Assert.Equal("onetwo", Assert.Single(editors.Editors).Document.Buffer.Current.GetText());
            Assert.Equal(EditorProviderDefinition.TextEditorId, custom.OpenEditorOf(path));

            Assert.Same(editors.Editors[0], await editors.OpenAsync(path));

            Assert.True(await custom.OpenWithAsync(path, NoteEditorProvider.Id));
            Assert.Empty(editors.Editors);
            Assert.Equal(NoteEditorProvider.Id, custom.OpenEditorOf(path));

            await Assert.ThrowsAsync<ArgumentException>(() => custom.OpenWithAsync(path, ImageViewerProvider.Id));

            var reopened = Assert.Single(custom.Panels);
            Assert.True(await custom.CloseAsync(reopened.Id, ask: false));
            Assert.Empty(custom.Panels);
            Assert.Null(custom.OpenEditorOf(path));
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task OpenWithTheTextEditorShowsABinaryFileReadOnly()
    {
        string[] hostAssemblies = ["Runesmith.Sdk", "Runesmith.Workspace", "Runesmith.Languages", "Runesmith.Editor", "Runesmith.Shell"];
        using var result = await RunesmithComposition.CreateAsync(new CompositionOptions([.. hostAssemblies.Select(Assembly.Load)], [], null), TestContext.Current.CancellationToken);

        await HeadlessSession.Value.Dispatch(async () =>
        {
            var custom = result.Exports.GetExportedValue<CustomEditorService>();
            var editors = result.Exports.GetExportedValue<EditorService>();
            var path = Path.Combine(folder, "red.png");
            await File.WriteAllBytesAsync(path, Images.SampleImages.Png);

            Assert.Null(await editors.OpenAsync(path));
            Assert.IsType<ImageViewer>(Assert.Single(custom.Panels).Editor);

            Assert.True(await custom.OpenWithAsync(path, EditorProviderDefinition.TextEditorId));
            var document = Assert.Single(editors.Editors).Document;
            Assert.True(document.IsReadOnly);
            Assert.StartsWith("\u0089PNG", document.Buffer.Current.GetText(), StringComparison.Ordinal);

            Assert.True(await custom.OpenWithAsync(path, ImageViewerProvider.Id));
            Assert.Empty(editors.Editors);
            Assert.Single(custom.Panels);
            await custom.CloseAllAsync();
        }, TestContext.Current.CancellationToken);
    }
}

/// <summary>A custom editor for <c>.note</c> files that appends text, with undo, for the tests.</summary>
[Export(typeof(IEditorProvider))]
public sealed class NoteEditorProvider : IEditorProvider
{
    public const string Id = "tests.notes";

    public EditorProviderDefinition Definition { get; } = new(Id, "Notes", ["*.note"]);

    public async Task<CustomEditor> CreateEditorAsync(CustomEditorContext context, CancellationToken cancellationToken) =>
        new NoteEditor(context.FilePath, await File.ReadAllTextAsync(context.FilePath, cancellationToken));
}

internal sealed class NoteEditor(string path, string text) : CustomEditor
{
    private readonly Stack<string> undo = new();
    private string saved = text;

    public string Text { get; private set; } = text;

    public bool IsDisposed { get; private set; }

    public override Control Content { get; } = new TextBlock();

    public override bool CanUndo => undo.Count > 0;

    public void Type(string more)
    {
        undo.Push(Text);
        Text += more;
        IsModified = Text != saved;
    }

    public override void Undo()
    {
        if (undo.TryPop(out var previous))
            Text = previous;
        IsModified = Text != saved;
    }

    public override async Task SaveAsync(CancellationToken cancellationToken)
    {
        await File.WriteAllTextAsync(path, Text, cancellationToken);
        saved = Text;
        IsModified = false;
    }

    public override async Task RevertAsync(CancellationToken cancellationToken)
    {
        Text = saved = await File.ReadAllTextAsync(path, cancellationToken);
        undo.Clear();
        IsModified = false;
    }

    protected override void Dispose(bool disposing)
    {
        IsDisposed = true;
        base.Dispose(disposing);
    }
}
