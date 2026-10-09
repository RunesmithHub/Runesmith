using Runesmith.Sdk.Workspace;

namespace Runesmith.Git.Tests.Repositories;

/// <summary>A workspace whose folder the test opens and closes.</summary>
internal sealed class FakeWorkspace : IWorkspace
{
    public string? RootPath { get; private set; }

    public string? Name => RootPath is null ? null : Path.GetFileName(RootPath);

    public IReadOnlyList<string> Solutions => [];

    public event EventHandler? Changed;

    public void Open(string? folder)
    {
        RootPath = folder;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task OpenAsync(string folderPath)
    {
        Open(folderPath);
        return Task.CompletedTask;
    }

    public void Close() => Open(null);

    public bool Contains(string path) => RootPath is not null && path.StartsWith(RootPath, StringComparison.Ordinal);

    public Task<IReadOnlyList<string>> GetFilesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
}
