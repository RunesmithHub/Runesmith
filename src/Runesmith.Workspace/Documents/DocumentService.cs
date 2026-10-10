using System.Collections.Concurrent;
using System.Composition;
using System.Text;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Text;
using Runesmith.Workspace.Files;

namespace Runesmith.Workspace.Documents;

/// <summary>Opens, saves and closes documents, and follows changes other programs make to their files.</summary>
/// <remarks>Use it on the UI thread. Edits it makes itself, such as reloading a file or trimming whitespace on save, are recorded in the
/// document's undo history; edits made through the buffer directly are recorded by whoever makes them, such as the editor.</remarks>
[Export(typeof(IDocumentService))]
[Shared]
public sealed class DocumentService : IDocumentService, IDisposable
{
    /// <summary>The largest file that opens, in bytes.</summary>
    public const long MaxFileSize = 50 * 1024 * 1024;

    private readonly ISettingsService _settings;
    private readonly ILanguageRegistry _languages;
    private readonly List<Document> _documents = [];
    private readonly Dictionary<string, Task<IDocument>> _opening = new(PathComparison.Comparer);
    private readonly Dictionary<string, FileWatcher> _watchers = new(PathComparison.Comparer);
    private readonly ConcurrentDictionary<string, bool> _anyContent = new(PathComparison.Comparer);

    [ImportingConstructor]
    public DocumentService(ISettingsService settings, ILanguageRegistry languages)
    {
        _settings = settings;
        _languages = languages;
    }

    public IReadOnlyList<IDocument> Documents => _documents;

    public event EventHandler<DocumentEventArgs>? Opened;

    public event EventHandler<DocumentEventArgs>? Saved;

    public event EventHandler<DocumentEventArgs>? Closed;

    public event EventHandler<DocumentEventArgs>? ChangedOnDisk;

    public IDocument? Find(string filePath)
    {
        var full = PathComparison.Normalize(filePath);
        return _documents.Find(d => d.FilePath is { } path && PathComparison.Comparer.Equals(path, full));
    }

    public Task<IDocument> OpenAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var full = PathComparison.Normalize(filePath);
        if (Find(full) is { } open)
            return Task.FromResult(open);
        if (_opening.TryGetValue(full, out var opening))
            return opening;

        var task = OpenNewAsync(full, cancellationToken);
        _opening[full] = task;
        return task;
    }

    /// <summary>Opens a file as text even when it looks binary, or returns its document when it is open already. A binary file opens
    /// read-only, each byte shown as one Latin-1 character.</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read.</exception>
    public Task<IDocument> OpenAsTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        _anyContent[PathComparison.Normalize(filePath)] = true;
        return OpenAsync(filePath, cancellationToken);
    }

    public IDocument CreateUntitled(string? languageId = null)
    {
        var used = _documents.Where(d => d.FilePath is null).Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        var number = 1;
        while (used.Contains($"Untitled-{number}"))
            number++;

        var document = new Document(null, $"Untitled-{number}", new TextBuffer(""), languageId ?? ILanguageRegistry.PlainText, DefaultLineEnding(), TextFileReader.Utf8);
        Add(document);
        return document;
    }

    public async Task SaveAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        var doc = Own(document);
        if (doc.FilePath is not { } path)
            throw new InvalidOperationException($"{doc.Name} has no file yet; save it with Save As.");
        if (doc.IsReadOnly)
            throw new UnauthorizedAccessException($"{path} is read-only.");

        await WriteAsync(doc, path, cancellationToken);
    }

    public async Task SaveAsAsync(IDocument document, string filePath, CancellationToken cancellationToken = default)
    {
        var doc = Own(document);
        var full = PathComparison.Normalize(filePath);
        if (Find(full) is { } other && other != doc)
            throw new IOException($"{full} is open in another editor; close it first.");

        var previous = doc.FilePath;
        await WriteAsync(doc, full, cancellationToken);
        doc.FilePath = full;
        doc.Name = Path.GetFileName(full);
        doc.IsReadOnly = false;
        if (previous is null || doc.LanguageId == _languages.GetLanguageForFile(previous).Id || doc.LanguageId == ILanguageRegistry.PlainText)
            doc.LanguageId = _languages.GetLanguageForFile(full).Id;
        if (previous is not null)
            Unwatch(previous);
        Watch(full);
    }

    public async Task ReloadAsync(IDocument document, CancellationToken cancellationToken = default)
    {
        var doc = Own(document);
        if (doc.FilePath is not { } path)
            return;

        var file = await Task.Run(() => Read(path), cancellationToken);
        Apply(doc, file);
    }

    public void Close(IDocument document)
    {
        var doc = Own(document);
        _documents.Remove(doc);
        if (doc.FilePath is { } path)
        {
            Unwatch(path);
            _anyContent.TryRemove(path, out _);
        }
        Closed?.Invoke(this, new DocumentEventArgs(doc));
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers.Values)
            watcher.Dispose();
        _watchers.Clear();
    }

    private async Task<IDocument> OpenNewAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var file = await Task.Run(() => Read(path), cancellationToken);
            if (Find(path) is { } open)
                return open;

            var document = new Document(path, Path.GetFileName(path), new TextBuffer(file.Text), _languages.GetLanguageForFile(path).Id, file.LineEnding, file.Encoding)
            {
                IsReadOnly = file.IsReadOnly,
                DiskStamp = file.Stamp,
            };
            Add(document);
            Watch(path);
            return document;
        }
        finally
        {
            _opening.Remove(path);
        }
    }

    private void Add(Document document)
    {
        _documents.Add(document);
        Opened?.Invoke(this, new DocumentEventArgs(document));
    }

    private Document Own(IDocument document) =>
        document as Document is { } doc && _documents.Contains(doc) ? doc : throw new ArgumentException("The document is not open.", nameof(document));

    private DiskFile Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"{path} does not exist.", path);
        if (info.Length > MaxFileSize)
            throw new IOException($"{info.Name} is {info.Length / (1024 * 1024)} MB; Runesmith opens files up to {MaxFileSize / (1024 * 1024)} MB.");

        var bytes = File.ReadAllBytes(path);
        var decoded = TextFileReader.Decode(bytes);
        var isBinary = decoded is null;
        if (isBinary && !_anyContent.ContainsKey(path))
            throw new IOException($"{info.Name} looks like a binary file, so it is not opened as text.");

        var (text, encoding) = decoded ?? (Encoding.Latin1.GetString(bytes), Encoding.Latin1);
        var lineEnding = LineEndings.Detect(text, DefaultLineEnding());
        return new DiskFile(LineEndings.Normalize(text), encoding, lineEnding, isBinary || IsReadOnly(info), (info.Length, info.LastWriteTimeUtc));
    }

    private static bool IsReadOnly(FileInfo info)
    {
        if (info.IsReadOnly)
            return true;

        try
        {
            using var stream = new FileStream(info.FullName, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            return false;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            return exception is UnauthorizedAccessException;
        }
    }

    private static void Apply(Document document, DiskFile file)
    {
        if (document.Buffer.Current.GetText() != file.Text)
            document.ReplaceText(file.Text);
        document.Encoding = file.Encoding;
        document.LineEnding = file.LineEnding;
        document.IsReadOnly = file.IsReadOnly;
        document.DiskStamp = file.Stamp;
        document.History.MarkSavePoint();
    }

    private async Task WriteAsync(Document document, string path, CancellationToken cancellationToken)
    {
        document.Edit(SaveEdits(document.Buffer.Current));
        var text = LineEndings.Apply(document.Buffer.Current.GetText(), document.LineEnding);
        var encoding = document.Encoding;
        var stamp = await Task.Run(() =>
        {
            var file = new FileInfo(path);
            var target = file.Exists && file.LinkTarget is not null ? file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path : path;
            AtomicFile.WriteAllBytes(target, TextFileReader.Encode(text, encoding));
            var info = new FileInfo(target);
            return (info.Length, info.LastWriteTimeUtc);
        }, cancellationToken);

        document.DiskStamp = stamp;
        document.History.MarkSavePoint();
        Saved?.Invoke(this, new DocumentEventArgs(document));
    }

    private List<TextChange> SaveEdits(TextSnapshot snapshot)
    {
        var changes = new List<TextChange>();
        if (_settings.Get<bool>(SettingKeys.TrimTrailingWhitespace))
        {
            for (var i = 0; i < snapshot.LineCount; i++)
            {
                var line = snapshot.GetLine(i);
                var end = line.End;
                while (end > line.Start && snapshot[end - 1] is ' ' or '\t')
                    end--;
                if (end < line.End)
                    changes.Add(new TextChange(TextSpan.FromBounds(end, line.End), ""));
            }
        }

        if (_settings.Get<bool>(SettingKeys.InsertFinalNewline) && snapshot.Length > 0 && snapshot[snapshot.Length - 1] != '\n')
            changes.Add(new TextChange(new TextSpan(snapshot.Length, 0), "\n"));
        return changes;
    }

    private LineEnding DefaultLineEnding() => _settings.Get<string>(SettingKeys.DefaultLineEnding) switch
    {
        "lf" => LineEnding.Lf,
        "crlf" => LineEnding.CrLf,
        _ => OperatingSystem.IsWindows() ? LineEnding.CrLf : LineEnding.Lf,
    };

    private void Watch(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        if (!_watchers.ContainsKey(folder) && Directory.Exists(folder))
            _watchers[folder] = new FileWatcher(folder, recursive: false, (changes, overflowed) => OnFilesChanged(folder, changes, overflowed));
    }

    private void Unwatch(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        if (_documents.Any(d => d.FilePath is { } other && PathComparison.Comparer.Equals(Path.GetDirectoryName(other), folder)))
            return;
        if (_watchers.Remove(folder, out var watcher))
            watcher.Dispose();
    }

    private void OnFilesChanged(string folder, IReadOnlyList<FileChange> changes, bool overflowed)
    {
        var paths = changes.SelectMany(c => c.OldPath is null ? [c.Path] : new[] { c.OldPath, c.Path }).ToHashSet(PathComparison.Comparer);
        UiThread.Run(() =>
        {
            foreach (var document in _documents.ToList())
            {
                if (document.FilePath is { } path && (paths.Contains(path) || (overflowed && PathComparison.Comparer.Equals(Path.GetDirectoryName(path), folder))))
                    _ = CheckDiskAsync(document, path);
            }
        });
    }

    private async Task CheckDiskAsync(Document document, string path)
    {
        var info = new FileInfo(path);
        if (info.Exists && document.DiskStamp == (info.Length, info.LastWriteTimeUtc))
            return;

        if (info.Exists && !document.IsModified)
        {
            try
            {
                var file = await Task.Run(() => Read(path));
                if (_documents.Contains(document) && document.FilePath == path && !document.IsModified)
                    Apply(document, file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A file another program is still writing is read on its next change.
                return;
            }
        }
        else if (info.Exists)
        {
            document.DiskStamp = (info.Length, info.LastWriteTimeUtc);
        }

        if (_documents.Contains(document))
            ChangedOnDisk?.Invoke(this, new DocumentEventArgs(document));
    }

    private sealed record DiskFile(string Text, Encoding Encoding, LineEnding LineEnding, bool IsReadOnly, (long, DateTime) Stamp);
}
