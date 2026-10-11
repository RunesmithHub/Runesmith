using System.Collections.Concurrent;
using System.Threading.Channels;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Text;

namespace Runesmith.Shell.Tests.Editors;

/// <summary>A file operation participant that answers with what the test gives and keeps what it was told.</summary>
internal sealed class RecordingParticipant : IFileOperationParticipant
{
    private readonly Channel<IReadOnlyList<FileOperation>> did = Channel.CreateUnbounded<IReadOnlyList<FileOperation>>();

    public Func<IReadOnlyList<FileOperation>, WorkspaceEdit?> Will { get; init; } = _ => null;

    public TimeSpan Delay { get; init; }

    /// <summary>Gets the operations of each call before the files changed, in order.</summary>
    public ConcurrentQueue<IReadOnlyList<FileOperation>> Asked { get; } = new();

    public async Task<WorkspaceEdit?> WillApplyAsync(IReadOnlyList<FileOperation> operations, CancellationToken cancellationToken)
    {
        Asked.Enqueue(operations);
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, CancellationToken.None);
        return Will(operations);
    }

    public Task DidApplyAsync(IReadOnlyList<FileOperation> operations, CancellationToken cancellationToken)
    {
        did.Writer.TryWrite(operations);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<FileOperation>> NextDidAsync() =>
        await did.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
}

/// <summary>Opens and closes documents as the editor service does, without views, and keeps the ones it closed.</summary>
internal sealed class OpeningEditors(IDocumentService documents) : IEditorService
{
    public List<IDocument> Closed { get; } = [];

    public IEditorView? ActiveEditor => null;

    public IReadOnlyList<IEditorView> Editors => [.. documents.Documents.Select(d => new View(d))];

    public event EventHandler? ActiveEditorChanged
    {
        add { }
        remove { }
    }

    public async Task<IEditorView?> OpenAsync(string filePath, TextPosition? position = null, bool activate = true) => new View(await documents.OpenAsync(filePath));

    public IEditorView Open(IDocument document, bool activate = true) => new View(document);

    public Task<bool> CloseAsync(IEditorView editor)
    {
        Closed.Add(editor.Document);
        documents.Close(editor.Document);
        return Task.FromResult(true);
    }

    private sealed class View(IDocument document) : IEditorView
    {
        public IDocument Document => document;

        public int CaretOffset { get; set; }

        public TextSpan Selection => default;

        public EditorCaretStyle CaretStyle { get; set; }

        public event EventHandler? CaretMoved
        {
            add { }
            remove { }
        }

        public void Select(TextSpan span)
        {
        }

        public void ScrollTo(int offset)
        {
        }

        public void Focus()
        {
        }
    }
}

/// <summary>Settings at their defaults, except that new files end their lines with <c>\n</c>.</summary>
internal sealed class LineFeedSettings : ISettingsService
{
    public IReadOnlyList<SettingDefinition> Definitions => [];

    public event EventHandler<SettingChangedEventArgs>? Changed
    {
        add { }
        remove { }
    }

    public T Get<T>(string key) => key == SettingKeys.DefaultLineEnding ? (T)(object)"lf" : default!;

    public object? GetValue(string key, SettingScope scope) => null;

    public SettingScope GetEffectiveScope(string key) => SettingScope.Default;

    public void Set(string key, object value, SettingScope scope = SettingScope.User)
    {
    }

    public void Reset(string key, SettingScope scope = SettingScope.User)
    {
    }
}

/// <summary>Languages that are all plain text.</summary>
internal sealed class PlainTextLanguages : ILanguageRegistry
{
    private static readonly LanguageDefinition Plain = new(ILanguageRegistry.PlainText, "Plain Text");

    public IReadOnlyList<LanguageDefinition> Languages => [Plain];

    public LanguageDefinition? Find(string languageId) => languageId == Plain.Id ? Plain : null;

    public LanguageDefinition GetLanguageForFile(string filePath) => Plain;
}
