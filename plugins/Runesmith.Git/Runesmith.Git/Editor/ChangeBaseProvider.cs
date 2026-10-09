using System.Collections.Concurrent;
using System.Composition;
using Runesmith.Git.Git;
using Runesmith.Git.Repositories;
using Runesmith.Sdk.Documents;

namespace Runesmith.Git.Editor;

/// <summary>Gives the editor's change markers each file as it is in HEAD: empty for a file that is new, and nothing for files outside the
/// repository, ignored files and binary files. Contents are cached by blob id, and the list of HEAD's blobs until HEAD moves.</summary>
[Export(typeof(IChangeBaseProvider))]
[Export]
[Shared]
internal sealed class ChangeBaseProvider : IChangeBaseProvider
{
    private const int BlobCacheLimit = 256;

    private readonly RepositoryService repositories;
    private readonly ConcurrentDictionary<string, Task<string?>> blobs = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private (GitRepository Repository, string? Head, Task<IReadOnlyDictionary<string, string>> Blobs)? tree;

    [ImportingConstructor]
    public ChangeBaseProvider(RepositoryService repositories)
    {
        this.repositories = repositories;
        repositories.HeadMoved += (_, _) =>
        {
            lock (gate)
                tree = null;
            BaseTextChanged?.Invoke(this, new BaseTextChangedEventArgs(null));
        };
    }

    public event EventHandler<BaseTextChangedEventArgs>? BaseTextChanged;

    public async Task<string?> GetBaseTextAsync(string filePath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        await repositories.Opened.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (repositories.Repository is not { } repository)
            return null;

        var full = Path.GetFullPath(filePath);
        var relative = Path.GetRelativePath(repository.Root, full);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) || full.StartsWith(repository.GitDirectory, StringComparison.Ordinal))
            return null;
        relative = relative.Replace('\\', '/');

        try
        {
            var headBlobs = await TreeOf(repository).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (!headBlobs.TryGetValue(relative, out var blob))
                return await IsIgnoredAsync(repository, relative, cancellationToken).ConfigureAwait(false) ? null : "";

            if (blobs.Count > BlobCacheLimit)
                blobs.Clear();
            return await blobs.GetOrAdd(blob, id => ReadBlobAsync(repository, id)).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (GitException)
        {
            return null;
        }
    }

    private Task<IReadOnlyDictionary<string, string>> TreeOf(GitRepository repository)
    {
        var head = repositories.Current?.Head;
        lock (gate)
        {
            if (tree is { } known && ReferenceEquals(known.Repository, repository) && known.Head == head)
                return known.Blobs;

            var blobsOfHead = head is null
                ? Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>())
                : repository.GetTreeAsync(head, CancellationToken.None);
            tree = (repository, head, blobsOfHead);
            return blobsOfHead;
        }
    }

    private static async Task<string?> ReadBlobAsync(GitRepository repository, string blob)
    {
        var text = await repository.GetBlobAsync(blob, CancellationToken.None).ConfigureAwait(false);
        if (text.Contains('\0', StringComparison.Ordinal))
            return null;
        return text.TrimStart('﻿').ReplaceLineEndings("\n");
    }

    private static async Task<bool> IsIgnoredAsync(GitRepository repository, string relative, CancellationToken cancellationToken) =>
        (await GitProcess.RunAsync(repository.Root, ["check-ignore", "--quiet", "--", relative], cancellationToken).ConfigureAwait(false)).ExitCode == 0;
}
