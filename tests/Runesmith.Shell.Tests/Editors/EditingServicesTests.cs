using System.Reflection;
using Runesmith.Composition;
using Runesmith.Editor;
using Runesmith.Editor.Input;
using Runesmith.Editor.Tests;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Tests.Running;
using Runesmith.Text;

namespace Runesmith.Shell.Tests.Editors;

public sealed class EditingServicesTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("runesmith-edits-").FullName;
    private readonly FakeDocuments documents = new();
    private readonly FakeEditors editors;
    private readonly FakeNotifications notifications = new();
    private readonly WorkspaceEditService service;

    public EditingServicesTests()
    {
        editors = new FakeEditors(documents);
        service = new WorkspaceEditService(documents, new Lazy<IEditorService>(() => editors), notifications);
    }

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Fact]
    public Task AppliesEachFilesChangesAsOneUndoStepOfThatFileAndOpensTheOthers() => Run(async () =>
    {
        var a = documents.Add(Path.Combine(folder, "A.cs"), "class A { Old x; }");
        var b = documents.Add(Path.Combine(folder, "B.cs"), "Old.Make();");
        var closed = Path.Combine(folder, "C.cs");
        await File.WriteAllTextAsync(closed, "// Old\r\nOld y;\r\n");
        var edit = new WorkspaceEdit(
        [
            new DocumentEdit(a.FilePath!, [new TextChange(new TextSpan(10, 3), "New")]) { Snapshot = a.Buffer.Current },
            new DocumentEdit(b.FilePath!, [new TextChange(new TextSpan(0, 3), "New")]),
            new DocumentEdit(closed, [new TextChange(new TextSpan(7, 3), "New\r\n")]),
        ]);

        Assert.True(await service.ApplyAsync(edit, TestContext.Current.CancellationToken));

        Assert.Equal("class A { New x; }", a.Buffer.Current.GetText());
        Assert.Equal("New.Make();", b.Buffer.Current.GetText());
        var c = Assert.Single(editors.Opened);
        Assert.Equal("// Old\nNew\n y;\n", c.Buffer.Current.GetText());
        a.History.Undo(a.Buffer);
        Assert.Equal("class A { Old x; }", a.Buffer.Current.GetText());
        Assert.Equal("New.Make();", b.Buffer.Current.GetText());
        Assert.False(a.History.CanUndo);
    });

    [Fact]
    public Task ChangesNothingWhenOneFileChangedSinceTheEditWasComputed() => Run(async () =>
    {
        var a = documents.Add(Path.Combine(folder, "A.cs"), "one");
        var b = documents.Add(Path.Combine(folder, "B.cs"), "two");
        var stale = b.Buffer.Current;
        b.Buffer.Insert(0, "x");

        var applied = await service.ApplyAsync(new WorkspaceEdit(
        [
            new DocumentEdit(a.FilePath!, [new TextChange(new TextSpan(0, 3), "ONE")]),
            new DocumentEdit(b.FilePath!, [new TextChange(new TextSpan(0, 3), "TWO")]) { Snapshot = stale },
        ]), TestContext.Current.CancellationToken);

        Assert.False(applied);
        Assert.Equal("one", a.Buffer.Current.GetText());
        Assert.Equal("xtwo", b.Buffer.Current.GetText());
        Assert.Single(notifications.Notified);
    });

    [Fact]
    public Task RefusesOverlappingChangesAndChangesPastTheEnd() => Run(async () =>
    {
        var a = documents.Add(Path.Combine(folder, "A.cs"), "abcdef");

        Assert.False(await service.ApplyAsync(new WorkspaceEdit([new DocumentEdit(a.FilePath!, [new TextChange(new TextSpan(0, 3), "x"), new TextChange(new TextSpan(2, 2), "y")])])));
        Assert.False(await service.ApplyAsync(new WorkspaceEdit([new DocumentEdit(a.FilePath!, [new TextChange(new TextSpan(5, 4), "x")])])));

        Assert.Equal("abcdef", a.Buffer.Current.GetText());
    });

    [Fact]
    public Task AnEditorShowingTheFileAppliesTheChangesAndKeepsItsCaretOnItsText() => Run(async () =>
    {
        var a = documents.Add(Path.Combine(folder, "A.cs"), "int total = Sum();");
        using var editor = new TextEditor(a, new FakeEditorServices().Create());
        editors.Shown.Add(editor);
        editor.CaretOffset = 15;

        Assert.True(await service.ApplyAsync(new WorkspaceEdit([new DocumentEdit(a.FilePath!, [new TextChange(new TextSpan(12, 3), "Add")])])));

        Assert.Equal("int total = Add();", a.Buffer.Current.GetText());
        Assert.Equal(15, editor.CaretOffset);
        editor.Undo();
        Assert.Equal("int total = Sum();", a.Buffer.Current.GetText());
    });

    [Fact]
    public Task FormatOnSaveFormatsOnlyWhenTheSettingIsOn() => Run(async () =>
    {
        var fakes = new FakeEditorServices();
        fakes.Features.Format = _ => [new TextChange(new TextSpan(0, 0), "// formatted\n")];
        var a = documents.Add(Path.Combine(folder, "A.cs"), "code");
        using var editor = new TextEditor(a, fakes.Create());
        var settings = new Settings();

        Assert.False(await FormatOnSave.RunAsync(settings, editor));
        Assert.Equal("code", a.Buffer.Current.GetText());

        settings.Set(SettingKeys.FormatOnSave, true);
        Assert.True(await FormatOnSave.RunAsync(settings, editor));
        Assert.Equal("// formatted\ncode", a.Buffer.Current.GetText());
        Assert.False(await FormatOnSave.RunAsync(settings, null));
    });

    [Theory]
    [InlineData("", new string[0])]
    [InlineData("acme.modal, other.macros;third", new[] { "acme.modal", "other.macros", "third" })]
    public void ReadsThePluginsWhoseKeyboardHooksAreOff(string value, string[] expected) =>
        Assert.Equal(expected.Order(), KeyHookPolicy.ParseDisabled(value).Order());

    [Fact]
    public async Task TheHostExportsTheEditingServicesAndTheProvidersTheyFind()
    {
        string[] hostAssemblies = ["Runesmith.Sdk", "Runesmith.Workspace", "Runesmith.Languages", "Runesmith.Editor", "Runesmith.Shell"];
        using var result = await RunesmithComposition.CreateAsync(new CompositionOptions([.. hostAssemblies.Select(Assembly.Load)], [], null), TestContext.Current.CancellationToken);

        Assert.Empty(result.Errors);
        Assert.IsType<WorkspaceEditService>(result.Exports.GetExportedValue<IWorkspaceEditService>());
        Assert.IsType<Languages.Features.EditorFeatures>(result.Exports.GetExportedValue<IEditorFeatures>());
        Assert.NotNull(result.Exports.GetExportedValue<EditorKeyHooks>());
        Assert.Equal(2, result.Exports.GetExports<ICodeActionProvider, LanguageMetadata>().Count());
        Assert.Equal(2, result.Exports.GetExports<IRenameProvider, LanguageMetadata>().Count());
        Assert.Equal(2, result.Exports.GetExports<IDocumentFormattingProvider, LanguageMetadata>().Count());
        Assert.Equal(2, result.Exports.GetExports<IRangeFormattingProvider, LanguageMetadata>().Count());
        Assert.Equal(2, result.Exports.GetExports<IDecorationProvider, LanguageMetadata>().Count());
        Assert.All(result.Exports.GetExports<ICodeActionProvider, LanguageMetadata>(), export => Assert.Equal([LanguagesAttribute.Any], export.Metadata.LanguageIds));
    }

    private static Task<bool> Run(Func<Task> test) =>
        HeadlessSession.Value.Dispatch(async () =>
        {
            await test();
            return true;
        }, TestContext.Current.CancellationToken);

    private sealed class FakeDocuments : IDocumentService
    {
        private readonly List<IDocument> open = [];

        public event EventHandler<DocumentEventArgs>? Opened;

        public event EventHandler<DocumentEventArgs>? Saved;

        public event EventHandler<DocumentEventArgs>? Closed;

        public event EventHandler<DocumentEventArgs>? ChangedOnDisk;

        public IReadOnlyList<IDocument> Documents => open;

        public FileTestDocument Add(string path, string text)
        {
            var document = new FileTestDocument(path, text);
            open.Add(document);
            Opened?.Invoke(this, new DocumentEventArgs(document));
            return document;
        }

        public IDocument? Find(string filePath) => open.FirstOrDefault(d => d.FilePath == filePath);

        public Task<IDocument> OpenAsync(string filePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IDocument>(Add(filePath, LineEndings.Normalize(File.ReadAllText(filePath))));

        public IDocument CreateUntitled(string? languageId = null) => throw new NotSupportedException();

        public Task SaveAsync(IDocument document, CancellationToken cancellationToken = default)
        {
            Saved?.Invoke(this, new DocumentEventArgs(document));
            return Task.CompletedTask;
        }

        public Task SaveAsAsync(IDocument document, string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task ReloadAsync(IDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Close(IDocument document)
        {
            open.Remove(document);
            Closed?.Invoke(this, new DocumentEventArgs(document));
            ChangedOnDisk?.Invoke(this, new DocumentEventArgs(document));
        }
    }

    private sealed class FakeEditors(FakeDocuments documents) : IEditorService
    {
        public List<TextEditor> Shown { get; } = [];

        public List<IDocument> Opened { get; } = [];

        public IEditorView? ActiveEditor => null;

        public IReadOnlyList<IEditorView> Editors => Shown;

        public event EventHandler? ActiveEditorChanged;

        public async Task<IEditorView?> OpenAsync(string filePath, TextPosition? position = null, bool activate = true)
        {
            Assert.False(activate);
            var document = await documents.OpenAsync(filePath);
            Opened.Add(document);
            ActiveEditorChanged?.Invoke(this, EventArgs.Empty);
            return new View(document);
        }

        public IEditorView Open(IDocument document, bool activate = true) => throw new NotSupportedException();

        public Task<bool> CloseAsync(IEditorView editor) => Task.FromResult(true);

        private sealed class View(IDocument document) : IEditorView
        {
            public IDocument Document => document;

            public int CaretOffset { get; set; }

            public TextSpan Selection => default;

            public EditorCaretStyle CaretStyle { get; set; }

            public event EventHandler? CaretMoved;

            public void Select(TextSpan span) => CaretMoved?.Invoke(this, EventArgs.Empty);

            public void ScrollTo(int offset)
            {
            }

            public void Focus()
            {
            }
        }
    }

    private sealed class Settings : ISettingsService
    {
        private readonly Dictionary<string, object> values = [];

        public IReadOnlyList<SettingDefinition> Definitions => [];

        public event EventHandler<SettingChangedEventArgs>? Changed;

        public T Get<T>(string key) => values.TryGetValue(key, out var value) ? (T)value : default!;

        public object? GetValue(string key, SettingScope scope) => values.GetValueOrDefault(key);

        public SettingScope GetEffectiveScope(string key) => SettingScope.User;

        public void Set(string key, object value, SettingScope scope = SettingScope.User)
        {
            values[key] = value;
            Changed?.Invoke(this, new SettingChangedEventArgs(key));
        }

        public void Reset(string key, SettingScope scope = SettingScope.User) => values.Remove(key);
    }
}
