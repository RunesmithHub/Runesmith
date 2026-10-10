using System.ComponentModel;
using System.Text;
using Runesmith.Composition;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Messaging;
using Runesmith.Sdk.Settings;
using Runesmith.Text;
using Runesmith.Workspace.Files;
using Runesmith.Workspace.Settings;
using RunesmithHub.Protocol.Index;

namespace Runesmith.Workspace.Tests;

/// <summary>A folder on disk opened as the workspace, with what it needs around it.</summary>
internal sealed class TestWorkspace : IDisposable
{
    private readonly string state = Directory.CreateTempSubdirectory("runesmith-state-").FullName;

    public TestWorkspace()
    {
        Root = Path.TrimEndingDirectorySeparator(Directory.CreateTempSubdirectory("runesmith-workspace-").FullName);
        Workspace = new Runesmith.Workspace.Files.Workspace(Settings, Messages, new RecentFolders(Path.Combine(state, "recent.json"), TimeProvider.System));
    }

    public string Root { get; }

    public TestSettings Settings { get; } = new();

    public MessageBus Messages { get; } = new();

    public Runesmith.Workspace.Files.Workspace Workspace { get; }

    public string Write(string relative, string text = "")
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose()
    {
        Workspace.Dispose();
        Directory.Delete(Root, recursive: true);
        Directory.Delete(state, recursive: true);
    }
}

internal sealed class TestSettings : ISettingsService
{
    public Dictionary<string, object> Values { get; } = new(StringComparer.Ordinal) { [SettingKeys.Exclude] = CoreSettings.DefaultExclude };

    public IReadOnlyList<SettingDefinition> Definitions => [];

    public event EventHandler<SettingChangedEventArgs>? Changed;

    public T Get<T>(string key) => Values.TryGetValue(key, out var value) ? (T)value : default!;

    public object? GetValue(string key, SettingScope scope) => Values.GetValueOrDefault(key);

    public SettingScope GetEffectiveScope(string key) => SettingScope.User;

    public void Set(string key, object value, SettingScope scope = SettingScope.User)
    {
        Values[key] = value;
        Changed?.Invoke(this, new SettingChangedEventArgs(key));
    }

    public void Reset(string key, SettingScope scope = SettingScope.User) => Values.Remove(key);
}

internal sealed class TestDocuments : IDocumentService
{
    private readonly List<IDocument> open = [];

    public event EventHandler<DocumentEventArgs>? Opened;

    public event EventHandler<DocumentEventArgs>? Saved;

    public event EventHandler<DocumentEventArgs>? Closed;

    public event EventHandler<DocumentEventArgs>? ChangedOnDisk;

    public IReadOnlyList<IDocument> Documents => open;

    public IDocument Add(string path, string text)
    {
        var document = new TestDocument(path, text);
        open.Add(document);
        return document;
    }

    public IDocument? Find(string filePath) => open.FirstOrDefault(d => d.FilePath == filePath);

    public Task<IDocument> OpenAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public IDocument CreateUntitled(string? languageId = null) => throw new NotSupportedException();

    public Task SaveAsync(IDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task SaveAsAsync(IDocument document, string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task ReloadAsync(IDocument document, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public void Close(IDocument document)
    {
        open.Remove(document);
        Opened?.Invoke(this, new DocumentEventArgs(document));
        Saved?.Invoke(this, new DocumentEventArgs(document));
        Closed?.Invoke(this, new DocumentEventArgs(document));
        ChangedOnDisk?.Invoke(this, new DocumentEventArgs(document));
    }

    private sealed class TestDocument(string path, string text) : IDocument
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string? FilePath => path;

        public string Name => Path.GetFileName(path);

        public TextBuffer Buffer { get; } = new(text);

        public UndoHistory History { get; } = new();

        public string LanguageId { get; set; } = "plaintext";

        public LineEnding LineEnding { get; set; }

        public Encoding Encoding { get; set; } = Encoding.UTF8;

        public bool IsModified => false;

        public bool IsReadOnly => false;

        public void Changed() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilePath)));
    }
}

internal static class TestPlugins
{
    public static PluginInfo Plugin(string id, params string[] capabilities) =>
        new(new PluginManifest(id, id, "1.0.0")
        {
            SchemaVersion = 1,
            Capabilities = [.. capabilities.Select(capability => new DeclaredCapability { Id = capability, Reason = "For the tests." })],
        }, Path.Combine(Path.GetTempPath(), id), PluginSource.Local);
}
