using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Workspace.Documents;

/// <summary>An open text document.</summary>
public sealed class Document : IDocument
{
    private string? _filePath;
    private string _name;
    private string _languageId;
    private LineEnding _lineEnding;
    private Encoding _encoding;
    private bool _isReadOnly;

    internal Document(string? filePath, string name, TextBuffer buffer, string languageId, LineEnding lineEnding, Encoding encoding)
    {
        _filePath = filePath;
        _name = name;
        Buffer = buffer;
        _languageId = languageId;
        _lineEnding = lineEnding;
        _encoding = encoding;
        History.MarkSavePoint();
        History.Changed += (_, _) => OnPropertyChanged(nameof(IsModified));
    }

    public string? FilePath
    {
        get => _filePath;
        internal set => Set(ref _filePath, value);
    }

    public string Name
    {
        get => _name;
        internal set => Set(ref _name, value);
    }

    public TextBuffer Buffer { get; }

    public UndoHistory History { get; } = new();

    public string LanguageId
    {
        get => _languageId;
        set => Set(ref _languageId, value);
    }

    public LineEnding LineEnding
    {
        get => _lineEnding;
        set => Set(ref _lineEnding, value);
    }

    public Encoding Encoding
    {
        get => _encoding;
        set => Set(ref _encoding, value);
    }

    public bool IsModified => !History.IsAtSavePoint;

    public bool IsReadOnly
    {
        get => _isReadOnly;
        internal set => Set(ref _isReadOnly, value);
    }

    /// <summary>Gets the size and time of the file as it was last read or written, to tell its own writes from changes made by others.</summary>
    internal (long Length, DateTime LastWriteUtc)? DiskStamp { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Replaces the text as one step that can be undone.</summary>
    internal void ReplaceText(string text)
    {
        var changeSet = Buffer.SetText(text);
        History.Push(changeSet, stateBefore: null, stateAfter: null);
    }

    /// <summary>Makes edits as one step that can be undone.</summary>
    internal void Edit(IReadOnlyList<TextChange> changes)
    {
        if (changes.Count > 0)
            History.Push(Buffer.Apply(changes), stateBefore: null, stateAfter: null);
    }

    public override string ToString() => FilePath ?? Name;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged(string? propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
