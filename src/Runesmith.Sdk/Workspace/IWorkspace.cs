namespace Runesmith.Sdk.Workspace;

/// <summary>The folder open in Runesmith and the files in it.</summary>
public interface IWorkspace
{
    /// <summary>Gets the full path of the open folder, or null when none is open.</summary>
    string? RootPath { get; }

    /// <summary>Gets the folder's name, or null when none is open.</summary>
    string? Name { get; }

    /// <summary>Gets the solution or project files found at the top of the folder, such as <c>App.sln</c>.</summary>
    IReadOnlyList<string> Solutions { get; }

    /// <summary>Opens a folder, closing the open one.</summary>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    Task OpenAsync(string folderPath);

    /// <summary>Closes the open folder.</summary>
    void Close();

    /// <summary>Whether a path is inside the open folder and not excluded by <c>files.exclude</c>.</summary>
    bool Contains(string path);

    /// <summary>Gets the files of the folder that are not excluded, as full paths; it is fast after the first call because the list is kept up to
    /// date as files change.</summary>
    Task<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised, on the UI thread, after a folder opens or closes.</summary>
    event EventHandler? Changed;
}
