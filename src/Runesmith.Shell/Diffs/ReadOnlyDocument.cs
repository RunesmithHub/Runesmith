using System.ComponentModel;
using System.Text;
using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Shell.Diffs;

/// <summary>A text that belongs to no file and is only read, such as the older side of a diff.</summary>
internal sealed class ReadOnlyDocument(string name, string text, string languageId) : IDocument
{
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    {
        add { }
        remove { }
    }

    public string? FilePath => null;

    public string Name => name;

    public TextBuffer Buffer { get; } = new(text);

    public UndoHistory History { get; } = new();

    public string LanguageId { get; set; } = languageId;

    public LineEnding LineEnding { get; set; } = LineEnding.Lf;

    public Encoding Encoding { get; set; } = Encoding.UTF8;

    public bool IsModified => false;

    public bool IsReadOnly => true;
}
