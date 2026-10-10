using Avalonia.Controls;

namespace Runesmith.Sdk.Editors;

/// <summary>The editor of one file in a tab, made by an <see cref="IEditorProvider"/>. Override what the editor supports: a viewer needs only
/// <see cref="Content"/>.</summary>
/// <remarks>Runesmith calls every member on the UI thread. It shows the dot of unsaved changes while <see cref="IsModified"/> is set, asks about
/// them before the tab closes, and routes Save, Undo and Redo to the editor while its tab is active. It disposes the editor when the tab
/// closes.</remarks>
public abstract class CustomEditor : IDisposable
{
    private bool isModified;

    /// <summary>Gets the control the tab shows; it is kept while the tab is moved, floated or hidden.</summary>
    public abstract Control Content { get; }

    /// <summary>Gets whether the editor has changes that are not saved to the file.</summary>
    public bool IsModified
    {
        get => isModified;
        protected set
        {
            if (isModified == value)
                return;
            isModified = value;
            OnStateChanged();
        }
    }

    /// <summary>Gets whether <see cref="Undo"/> has a change to undo.</summary>
    public virtual bool CanUndo => false;

    /// <summary>Gets whether <see cref="Redo"/> has a change to redo.</summary>
    public virtual bool CanRedo => false;

    /// <summary>Raised when <see cref="IsModified"/>, <see cref="CanUndo"/> or <see cref="CanRedo"/> change.</summary>
    public event EventHandler? StateChanged;

    /// <summary>Writes the changes to the file and clears <see cref="IsModified"/>.</summary>
    /// <exception cref="IOException">The file cannot be written; Runesmith tells the user and the changes stay.</exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be written.</exception>
    public virtual Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Reads the file again, dropping unsaved changes; Runesmith also calls it when the file changes on disk while the editor has
    /// none.</summary>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="InvalidDataException">The file is not in a format the editor reads.</exception>
    public virtual Task RevertAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual void Undo()
    {
    }

    public virtual void Redo()
    {
    }

    /// <summary>Gives the editor the keyboard focus when its tab is activated.</summary>
    public virtual void Focus() => Content.Focus();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases what the editor holds, such as images and file handles.</summary>
    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>Raises <see cref="StateChanged"/>, such as after an edit changes <see cref="CanUndo"/>.</summary>
    protected void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
