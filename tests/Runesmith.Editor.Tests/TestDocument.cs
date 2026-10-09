using System.ComponentModel;
using System.Text;
using Runesmith.Sdk.Documents;
using Runesmith.Text;

namespace Runesmith.Editor.Tests;

internal sealed class TestDocument(string text) : IDocument
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public string? FilePath => null;

    public string Name => "test.txt";

    public TextBuffer Buffer { get; } = new(text);

    public UndoHistory History { get; } = new();

    public string LanguageId { get; set; } = "plaintext";

    public LineEnding LineEnding { get; set; }

    public Encoding Encoding { get; set; } = Encoding.UTF8;

    public bool IsModified => false;

    public bool IsReadOnly => false;

    public void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
