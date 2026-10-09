namespace Runesmith.Sdk.Documents;

/// <summary>Raised when the base version of files changed, such as after a commit or a checkout.</summary>
/// <param name="filePaths">The files whose base changed, or null for every file.</param>
public sealed class BaseTextChangedEventArgs(IReadOnlyList<string>? filePaths) : EventArgs
{
    /// <summary>Gets the files whose base changed, or null for every file.</summary>
    public IReadOnlyList<string>? FilePaths { get; } = filePaths;
}

/// <summary>Supplies the version of a file that the editor's change markers compare with, such as the file as it is in the last commit.
/// Export it with <c>[Export(typeof(IChangeBaseProvider))]</c>.</summary>
/// <remarks>The editor compares the document with its base as the user types, marks added, changed and deleted lines in the gutter, and
/// offers to roll a change back to the base.</remarks>
public interface IChangeBaseProvider
{
    /// <summary>Gets the base text of a file, or null when the provider has none, such as for a file outside a repository; a file that is
    /// new has an empty base.</summary>
    Task<string?> GetBaseTextAsync(string filePath, CancellationToken cancellationToken);

    /// <summary>Raised when base texts changed; any thread may raise it.</summary>
    event EventHandler<BaseTextChangedEventArgs>? BaseTextChanged;
}

/// <summary>One side of a diff.</summary>
/// <param name="Title">What the side shows, such as "HEAD" or "Working tree".</param>
/// <param name="Text">The text.</param>
public sealed record DiffSide(string Title, string Text)
{
    /// <summary>Gets the file the side's text is from, used for highlighting and, when <see cref="IsEditable"/>, for saving edits.</summary>
    public string? FilePath { get; init; }

    /// <summary>Gets whether the side is the editable file on disk, such as the working copy.</summary>
    public bool IsEditable { get; init; }
}

/// <summary>Opens diffs: two versions of a text side by side, with the changed lines and characters highlighted.</summary>
public interface IDiffService
{
    /// <summary>Opens a diff in an editor tab, or switches to one with the same title.</summary>
    /// <param name="title">The tab's title, such as "Program.cs (HEAD and working tree)".</param>
    /// <param name="left">The older side.</param>
    /// <param name="right">The newer side.</param>
    void Open(string title, DiffSide left, DiffSide right);
}
