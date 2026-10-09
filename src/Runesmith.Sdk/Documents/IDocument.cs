using System.ComponentModel;
using System.Text;
using Runesmith.Text;

namespace Runesmith.Sdk.Documents;

/// <summary>An open text document: its text, the file it belongs to and how it is saved.</summary>
/// <remarks>Properties raise <see cref="INotifyPropertyChanged.PropertyChanged"/>. Edit the text through <see cref="Buffer"/> on the UI thread;
/// snapshots of it can be read from any thread.</remarks>
public interface IDocument : INotifyPropertyChanged
{
    /// <summary>Gets the full path of the file, or null for a new document that was never saved.</summary>
    string? FilePath { get; }

    /// <summary>Gets the name shown on the document's tab, such as <c>Program.cs</c> or <c>Untitled-1</c>.</summary>
    string Name { get; }

    /// <summary>Gets the text. Its line breaks are always <c>\n</c>; <see cref="LineEnding"/> is what the file is saved with.</summary>
    TextBuffer Buffer { get; }

    /// <summary>Gets the undo history of the text.</summary>
    UndoHistory History { get; }

    /// <summary>Gets or sets the language, such as <c>csharp</c>, which picks the highlighting and the language features.</summary>
    string LanguageId { get; set; }

    /// <summary>Gets or sets the line ending the file is saved with.</summary>
    LineEnding LineEnding { get; set; }

    /// <summary>Gets or sets the encoding the file is saved with.</summary>
    Encoding Encoding { get; set; }

    /// <summary>Gets whether the text differs from the file.</summary>
    bool IsModified { get; }

    /// <summary>Gets whether the file is read-only, so edits cannot be saved to it.</summary>
    bool IsReadOnly { get; }
}
