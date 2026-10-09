using System.ComponentModel;
using System.Text;
using Runesmith.Sdk.Build;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using Runesmith.Text;

namespace Runesmith.Languages.Tests.Analyzers;

internal sealed class TestDocument(string? filePath, string name, string text, string languageId = "fake") : IDocument
{
    private string languageId = languageId;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? FilePath { get; set; } = filePath;

    public string Name { get; } = name;

    public TextBuffer Buffer { get; } = new(text);

    public UndoHistory History { get; } = new();

    public string LanguageId
    {
        get => languageId;
        set
        {
            languageId = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LanguageId)));
        }
    }

    public LineEnding LineEnding { get; set; }

    public Encoding Encoding { get; set; } = Encoding.UTF8;

    public bool IsModified => false;

    public bool IsReadOnly => false;
}

internal sealed class TestDocuments : IDocumentService
{
    private readonly List<IDocument> documents = [];

    public event EventHandler<DocumentEventArgs>? Opened;

    public event EventHandler<DocumentEventArgs>? Saved;

    public event EventHandler<DocumentEventArgs>? Closed;

    public event EventHandler<DocumentEventArgs>? ChangedOnDisk;

    public IReadOnlyList<IDocument> Documents => documents;

    public void Add(IDocument document)
    {
        documents.Add(document);
        Opened?.Invoke(this, new DocumentEventArgs(document));
    }

    public void SaveAs(TestDocument document, string filePath)
    {
        document.FilePath = filePath;
        Saved?.Invoke(this, new DocumentEventArgs(document));
    }

    public void Close(IDocument document)
    {
        documents.Remove(document);
        Closed?.Invoke(this, new DocumentEventArgs(document));
    }

    public IDocument? Find(string filePath) => documents.FirstOrDefault(document => document.FilePath == filePath);

    public Task<IDocument> OpenAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IDocument CreateUntitled(string? languageId = null) => throw new NotSupportedException();

    public Task SaveAsync(IDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task SaveAsAsync(IDocument document, string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task ReloadAsync(IDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    internal void RaiseChangedOnDisk(IDocument document) => ChangedOnDisk?.Invoke(this, new DocumentEventArgs(document));
}

internal sealed class TestWorkspace : IWorkspace
{
    public event EventHandler? Changed;

    public string? RootPath { get; private set; }

    public string? Name => RootPath is null ? null : Path.GetFileName(RootPath);

    public IReadOnlyList<string> Solutions => [];

    public Task OpenAsync(string folderPath)
    {
        RootPath = folderPath;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Close() => RootPath = null;

    public bool Contains(string path) => RootPath is not null && path.StartsWith(RootPath, StringComparison.Ordinal);

    public Task<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
}

internal sealed class TestDiagnostics : IDiagnosticService
{
    private readonly Dictionary<string, IReadOnlyList<Diagnostic>> byFile = [];

    public event EventHandler<IReadOnlyCollection<string>>? Changed;

    public IReadOnlyList<Diagnostic> All
    {
        get
        {
            lock (byFile)
                return [.. byFile.Values.SelectMany(list => list)];
        }
    }

    public void Set(string source, string filePath, IReadOnlyList<Diagnostic> diagnostics)
    {
        lock (byFile)
            byFile[filePath] = diagnostics;
        Changed?.Invoke(this, [filePath]);
    }

    public void Clear(string source)
    {
        lock (byFile)
            byFile.Clear();
    }

    public IReadOnlyList<Diagnostic> Get(string filePath)
    {
        lock (byFile)
            return byFile.GetValueOrDefault(filePath) ?? [];
    }
}

internal sealed class TestOutput : IOutputService
{
    public IOutputChannel GetChannel(string name) => new Channel(name);

    private sealed class Channel(string name) : IOutputChannel
    {
        public string Name => name;

        public void Append(string text)
        {
        }

        public void AppendLine(string line)
        {
        }

        public void Clear()
        {
        }

        public void Show()
        {
        }
    }
}

internal sealed class TestSettings : ISettingsService
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
