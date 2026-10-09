using System.Globalization;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Git;

/// <summary>A Git repository and the commands Runesmith runs in it. Commands that change the repository run one at a time, in the order they
/// were asked for; commands that only read run beside them.</summary>
internal sealed class GitRepository
{
    private const string NulPathspec = "--pathspec-file-nul";
    private const string PathspecFromInput = "--pathspec-from-file=-";

    private readonly Lock queueGate = new();
    private readonly GitCredentialResolver? resolver;
    private Task queueTail = Task.CompletedTask;

    /// <summary>Creates the repository of a top folder and its Git folder.</summary>
    public GitRepository(string root, string gitDirectory, GitCredentialResolver? credentials = null)
    {
        Root = root;
        GitDirectory = gitDirectory;
        resolver = credentials;
    }

    /// <summary>Gets the repository's top folder.</summary>
    public string Root { get; }

    /// <summary>Gets the repository's Git folder, usually <c>.git</c> in <see cref="Root"/>.</summary>
    public string GitDirectory { get; }

    /// <summary>Raised before each command that changes the repository runs, with its arguments, for the Git output channel.</summary>
    public event EventHandler<IReadOnlyList<string>>? Running;

    /// <summary>Finds the repository a folder is in, or returns null when it is in none, Git is missing, or the folder is inside the Git folder.</summary>
    public static async Task<GitRepository?> DiscoverAsync(string folder, GitCredentialResolver? credentials, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(folder))
            return null;

        GitResult result;
        try
        {
            result = await GitProcess.RunAsync(folder, ["rev-parse", "--show-toplevel", "--absolute-git-dir"], cancellationToken).ConfigureAwait(false);
        }
        catch (GitException)
        {
            return null;
        }

        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return result.Succeeded && lines.Length == 2 ? new GitRepository(Path.GetFullPath(lines[0]), Path.GetFullPath(lines[1]), credentials) : null;
    }

    /// <summary>Makes a folder a Git repository.</summary>
    public static Task InitAsync(string folder, CancellationToken cancellationToken) => GitProcess.RunCheckedAsync(folder, ["init"], cancellationToken);

    /// <summary>Gets a path relative to the repository with forward slashes, as Git writes it.</summary>
    public string RelativePath(string fullPath) => Path.GetRelativePath(Root, fullPath).Replace('\\', '/');

    /// <summary>Gets the full path of a path relative to the repository.</summary>
    public string FullPath(string relativePath) => Path.GetFullPath(Path.Combine(Root, relativePath));

    // Reading

    /// <summary>Reads the status: the branch, its upstream, and every changed, untracked and conflicted file.</summary>
    public async Task<StatusSnapshot> GetStatusAsync(CancellationToken cancellationToken) =>
        StatusParser.Parse(await ReadAsync(["status", "--porcelain=v2", "-z", "--branch", "--untracked-files=all"], cancellationToken).ConfigureAwait(false));

    /// <summary>Reads the remotes and their addresses.</summary>
    public async Task<IReadOnlyList<GitRemote>> GetRemotesAsync(CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(Root, ["config", "-z", "--get-regexp", @"^remote\..*\.(url|pushurl)$"], cancellationToken).ConfigureAwait(false);
        var fetch = new Dictionary<string, string>(StringComparer.Ordinal);
        var push = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var entry in result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Split('\n', 2);
            if (parts.Length != 2)
                continue;

            var key = parts[0];
            var isPush = key.EndsWith(".pushurl", StringComparison.Ordinal);
            var name = key[7..key.LastIndexOf('.')];
            if (!order.Contains(name))
                order.Add(name);
            (isPush ? push : fetch).TryAdd(name, parts[1]);
        }

        return [.. order.Where(fetch.ContainsKey).Select(name => new GitRemote(name, fetch[name], push.GetValueOrDefault(name, fetch[name])))];
    }

    /// <summary>Reads the local and remote branches and the tags.</summary>
    public async Task<IReadOnlyList<GitRef>> GetRefsAsync(CancellationToken cancellationToken) =>
        RefParser.Parse(await ReadAsync(["for-each-ref", "--format=" + RefParser.Format, "refs/heads", "refs/remotes", "refs/tags"], cancellationToken).ConfigureAwait(false));

    /// <summary>Reads a page of the log; a repository without commits has none.</summary>
    public async Task<IReadOnlyList<GitCommit>> GetLogAsync(LogQuery query, CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(Root, LogParser.Arguments(query), cancellationToken).ConfigureAwait(false);
        if (result.Succeeded)
            return LogParser.Parse(result.Output);
        if (!await HasCommitsAsync(cancellationToken).ConfigureAwait(false))
            return [];
        throw new GitException(result, LogParser.Arguments(query));
    }

    /// <summary>Reads a commit's full message and the files it changed, compared with its first parent.</summary>
    public async Task<CommitDetails> GetCommitAsync(string sha, CancellationToken cancellationToken)
    {
        var header = ReadAsync(["show", "-s", "-z", LogParser.Format + "%x00%B", "--decorate=full", sha], cancellationToken);
        var commit = LogParser.Parse(await header.ConfigureAwait(false)) is [var first, ..] ? first : throw new InvalidOperationException($"No commit {sha}.");
        var output = await header.ConfigureAwait(false);
        var message = output.Split('\0') is { Length: > 10 } fields ? fields[10].TrimEnd() : commit.Subject;
        IReadOnlyList<string> arguments = commit.Parents.Count == 0
            ? ["diff-tree", "--root", "--no-commit-id", "-r", "-z", "-M", "--name-status", sha]
            : ["diff-tree", "--no-commit-id", "-r", "-z", "-M", "--name-status", commit.Parents[0], sha];
        var files = LogParser.ParseNameStatus(await ReadAsync(arguments, cancellationToken).ConfigureAwait(false));
        return new CommitDetails(commit, message, files);
    }

    /// <summary>Reads a file as it is in a revision, such as <c>HEAD</c>, or in the index when <paramref name="revision"/> is empty; returns null
    /// when the file is not there.</summary>
    public async Task<string?> GetFileAsync(string revision, string relativePath, CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(Root, ["show", $"{revision}:{relativePath}"], cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.Output : null;
    }

    /// <summary>Reads the blob ids of every file in a revision's tree, by path relative to the repository.</summary>
    public async Task<IReadOnlyDictionary<string, string>> GetTreeAsync(string revision, CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(Root, ["ls-tree", "-r", "-z", "--full-tree", revision], cancellationToken).ConfigureAwait(false);
        var tree = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!result.Succeeded)
            return tree;

        foreach (var entry in result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            // <mode> SP <type> SP <object> TAB <path>
            var tab = entry.IndexOf('\t', StringComparison.Ordinal);
            var fields = tab > 0 ? entry[..tab].Split(' ') : [];
            if (fields is [_, "blob", var blob])
                tree[entry[(tab + 1)..]] = blob;
        }

        return tree;
    }

    /// <summary>Reads a blob's content.</summary>
    public Task<string> GetBlobAsync(string blobId, CancellationToken cancellationToken) => ReadAsync(["cat-file", "blob", blobId], cancellationToken);

    /// <summary>Reads the last commit's full message, or null when there is no commit.</summary>
    public async Task<string?> GetLastMessageAsync(CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(Root, ["log", "-1", "--format=%B", "HEAD"], cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.Output.TrimEnd() : null;
    }

    /// <summary>Reads the messages of the latest commits, newest first, without repeats.</summary>
    public async Task<IReadOnlyList<string>> GetRecentMessagesAsync(int count, CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(Root, ["log", "-z", $"--max-count={count}", "--format=%B", "HEAD"], cancellationToken).ConfigureAwait(false);
        return result.Succeeded
            ? [.. result.Output.Split('\0').Select(m => m.Trim()).Where(m => m.Length > 0).Distinct(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>Reads the branches checked out most recently, newest first, from HEAD's reflog.</summary>
    public async Task<IReadOnlyList<string>> GetRecentBranchesAsync(int count, CancellationToken cancellationToken)
    {
        const string Moving = "checkout: moving from ";
        var result = await GitProcess.RunAsync(Root, ["reflog", "show", "-z", "--format=%gs", "--max-count=500", "HEAD", "--"], cancellationToken).ConfigureAwait(false);
        var recent = new List<string>();
        foreach (var entry in result.Output.Split('\0'))
        {
            var line = entry.Trim('\n');
            if (!line.StartsWith(Moving, StringComparison.Ordinal) || line[Moving.Length..].Split(" to ") is not [var from, var to])
                continue;
            foreach (var name in new[] { to, from }.Where(n => !recent.Contains(n, StringComparer.Ordinal)))
                recent.Add(name);
            if (recent.Count >= count)
                break;
        }

        return [.. recent.Take(count)];
    }

    /// <summary>Reads the stash, newest first.</summary>
    public async Task<IReadOnlyList<StashEntry>> GetStashesAsync(CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(Root, ["stash", "list", "-z", "--format=%H%x00%ct%x00%gs"], cancellationToken).ConfigureAwait(false);
        var fields = result.Output.Split('\0');
        var stashes = new List<StashEntry>();
        for (var i = 0; i + 3 <= fields.Length && fields[i].Length > 0; i += 3)
        {
            var seconds = long.Parse(fields[i + 1], CultureInfo.InvariantCulture);
            stashes.Add(new StashEntry(stashes.Count, fields[i].Trim('\n'), fields[i + 2], DateTimeOffset.FromUnixTimeSeconds(seconds)));
        }

        return stashes;
    }

    /// <summary>Reads a configuration value, or null when it is not set.</summary>
    public async Task<string?> GetConfigAsync(string key, CancellationToken cancellationToken)
    {
        var result = await GitProcess.RunAsync(Root, ["config", "--get", key], cancellationToken).ConfigureAwait(false);
        return result.Succeeded ? result.Output.TrimEnd('\n') : null;
    }

    /// <summary>Finds the multi-step operation that stopped part way, if any.</summary>
    public OngoingOperation GetOngoingOperation() =>
        Directory.Exists(Path.Combine(GitDirectory, "rebase-merge")) || Directory.Exists(Path.Combine(GitDirectory, "rebase-apply")) ? OngoingOperation.Rebase
        : File.Exists(Path.Combine(GitDirectory, "MERGE_HEAD")) ? OngoingOperation.Merge
        : File.Exists(Path.Combine(GitDirectory, "CHERRY_PICK_HEAD")) ? OngoingOperation.CherryPick
        : File.Exists(Path.Combine(GitDirectory, "REVERT_HEAD")) ? OngoingOperation.Revert
        : OngoingOperation.None;

    /// <summary>Whether a branch name is valid.</summary>
    public async Task<bool> IsValidBranchNameAsync(string name, CancellationToken cancellationToken) =>
        name.Length > 0 && !name.StartsWith('-') && (await GitProcess.RunAsync(Root, ["check-ref-format", "--branch", name], cancellationToken).ConfigureAwait(false)).Succeeded;

    /// <summary>Whether HEAD points at a commit.</summary>
    public async Task<bool> HasCommitsAsync(CancellationToken cancellationToken) =>
        (await GitProcess.RunAsync(Root, ["rev-parse", "--verify", "--quiet", "HEAD"], cancellationToken).ConfigureAwait(false)).Succeeded;

    /// <summary>Finds where the current branch pushes to: its upstream's remote and branch, or the first remote and the same name.</summary>
    public async Task<(string Remote, string Branch, bool HasUpstream)?> GetPushTargetAsync(string branch, CancellationToken cancellationToken)
    {
        var remote = await GetConfigAsync($"branch.{branch}.remote", cancellationToken).ConfigureAwait(false);
        var merge = await GetConfigAsync($"branch.{branch}.merge", cancellationToken).ConfigureAwait(false);
        if (remote is not null && remote != "." && merge is not null)
            return (remote, GitRef.ShortName(merge), true);

        var remotes = await GetRemotesAsync(cancellationToken).ConfigureAwait(false);
        var target = remotes.FirstOrDefault(r => r.Name == "origin") ?? (remotes.Count > 0 ? remotes[0] : null);
        return target is null ? null : (target.Name, branch, false);
    }

    // Changing

    /// <summary>Stages files, including deletions.</summary>
    public Task StageAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken) =>
        WithPathsAsync(["add", "--all", PathspecFromInput, NulPathspec], relativePaths, cancellationToken);

    /// <summary>Unstages files, keeping their changes in the working tree.</summary>
    public async Task UnstageAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken)
    {
        if (await HasCommitsAsync(cancellationToken).ConfigureAwait(false))
            await WithPathsAsync(["restore", "--staged", PathspecFromInput, NulPathspec], relativePaths, cancellationToken).ConfigureAwait(false);
        else
            await WithPathsAsync(["rm", "--cached", "-r", "--quiet", "--force", PathspecFromInput, NulPathspec], relativePaths, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Rolls files back to HEAD: changes are undone, renames are reversed, added files become untracked and untracked files are
    /// deleted.</summary>
    public async Task DiscardAsync(IEnumerable<StatusEntry> entries, CancellationToken cancellationToken)
    {
        var list = entries.ToList();
        var restore = new List<string>();
        var unstage = new List<string>();
        var delete = new List<string>();
        foreach (var entry in list)
        {
            switch (entry)
            {
                case { Kind: StatusKind.Untracked }:
                    delete.Add(entry.Path);
                    break;
                case { Kind: StatusKind.Renamed or StatusKind.Copied }:
                    unstage.Add(entry.Path);
                    delete.Add(entry.Path);
                    if (entry.Kind == StatusKind.Renamed && entry.OriginalPath is { } original)
                        restore.Add(original);
                    break;
                case { Index: 'A' }:
                    unstage.Add(entry.Path);
                    break;
                case { Kind: StatusKind.Ignored }:
                    break;
                default:
                    restore.Add(entry.Path);
                    break;
            }
        }

        if (unstage.Count > 0)
            await UnstageAsync(unstage, cancellationToken).ConfigureAwait(false);
        if (restore.Count > 0)
            await WithPathsAsync(["restore", "--source=HEAD", "--staged", "--worktree", PathspecFromInput, NulPathspec], restore, cancellationToken).ConfigureAwait(false);
        foreach (var path in delete)
        {
            var full = FullPath(path);
            if (File.Exists(full))
                File.Delete(full);
            else if (Directory.Exists(full))
                Directory.Delete(full, recursive: true);
        }
    }

    /// <summary>Commits the staged changes, or amends the last commit; returns the new commit's hash.</summary>
    public async Task<string> CommitAsync(string message, CommitOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var arguments = new List<string> { "commit", "--file=-", "--cleanup=strip" };
        if (options.Amend)
            arguments.Add("--amend");
        if (options.Signoff)
            arguments.Add("--signoff");
        if (options.NoVerify)
            arguments.Add("--no-verify");
        await WriteAsync(arguments, cancellationToken, input: message).ConfigureAwait(false);
        return (await ReadAsync(["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false)).Trim();
    }

    /// <summary>Switches to a local branch, or creates a local branch that tracks a remote one and switches to it.</summary>
    /// <param name="force">Throw away local changes that would be overwritten.</param>
    public Task CheckoutAsync(GitRef branch, bool force, CancellationToken cancellationToken, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(branch);
        var arguments = new List<string> { "switch", "--progress" };
        if (force)
            arguments.Add("--discard-changes");
        if (branch.Kind == RefKind.RemoteBranch)
            arguments.AddRange(["--track", branch.Name]);
        else if (branch.Kind == RefKind.Tag)
            arguments.AddRange(["--detach", branch.FullName]);
        else
            arguments.Add(branch.Name);
        return WriteAsync(arguments, cancellationToken, progress: progress);
    }

    /// <summary>Checks out a commit with a detached HEAD.</summary>
    public Task CheckoutCommitAsync(string sha, bool force, CancellationToken cancellationToken) =>
        WriteAsync(force ? ["switch", "--detach", "--discard-changes", sha] : ["switch", "--detach", sha], cancellationToken);

    /// <summary>Creates a branch at a start point, HEAD when null, and switches to it when asked.</summary>
    public Task CreateBranchAsync(string name, string? startPoint, bool checkout, CancellationToken cancellationToken)
    {
        var arguments = checkout ? new List<string> { "switch", "--no-track", "--create", name } : ["branch", "--no-track", name];
        if (startPoint is not null)
            arguments.Add(startPoint);
        return WriteAsync(arguments, cancellationToken);
    }

    /// <summary>Renames a local branch.</summary>
    public Task RenameBranchAsync(string name, string newName, CancellationToken cancellationToken) =>
        WriteAsync(["branch", "--move", name, newName], cancellationToken);

    /// <summary>Deletes a local branch; <paramref name="force"/> deletes it even when its commits are merged nowhere.</summary>
    public Task DeleteBranchAsync(string name, bool force, CancellationToken cancellationToken) =>
        WriteAsync(["branch", force ? "-D" : "-d", name], cancellationToken);

    /// <summary>Deletes a branch on its remote.</summary>
    public async Task DeleteRemoteBranchAsync(string remote, string branch, CancellationToken cancellationToken, IProgress<string>? progress = null) =>
        await WriteAsync(["push", "--progress", remote, "--delete", branch], cancellationToken, await CredentialsForAsync([remote], cancellationToken).ConfigureAwait(false), progress)
            .ConfigureAwait(false);

    /// <summary>Makes a local branch track a remote one; for a remote branch without a local one, creates the local branch.</summary>
    public Task TrackAsync(string localBranch, string remoteBranch, bool create, CancellationToken cancellationToken) =>
        WriteAsync(create ? ["branch", "--track", localBranch, remoteBranch] : ["branch", $"--set-upstream-to={remoteBranch}", localBranch], cancellationToken);

    /// <summary>Fetches every remote and drops remote branches deleted there.</summary>
    public async Task FetchAsync(CancellationToken cancellationToken, IProgress<string>? progress = null)
    {
        var remotes = await GetRemotesAsync(cancellationToken).ConfigureAwait(false);
        if (remotes.Count == 0)
            return;
        await WriteAsync(["fetch", "--all", "--prune", "--progress"], cancellationToken, await CredentialsForAsync(remotes.Select(r => r.Name), cancellationToken).ConfigureAwait(false), progress)
            .ConfigureAwait(false);
    }

    /// <summary>Pulls the current branch's upstream, stashing local changes around it.</summary>
    public async Task PullAsync(PullStrategy strategy, CancellationToken cancellationToken, IProgress<string>? progress = null)
    {
        var arguments = new List<string> { "pull", "--autostash", "--progress" };
        if (strategy == PullStrategy.Rebase)
            arguments.Add("--rebase");
        else if (strategy == PullStrategy.Merge || await GetConfigAsync("pull.rebase", cancellationToken).ConfigureAwait(false) is null)
            arguments.Add("--no-rebase");

        var remotes = await GetRemotesAsync(cancellationToken).ConfigureAwait(false);
        await WriteAsync(arguments, cancellationToken, await CredentialsForAsync(remotes.Select(r => r.Name), cancellationToken).ConfigureAwait(false), progress)
            .ConfigureAwait(false);
    }

    /// <summary>Pushes a local branch to a branch of a remote, setting it as the upstream when asked.</summary>
    /// <param name="forceWithLease">Overwrite the remote branch, but only when it is where the last fetch saw it.</param>
    public async Task PushAsync(string localBranch, string remote, string remoteBranch, bool setUpstream, bool forceWithLease, CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        var arguments = new List<string> { "push", "--progress" };
        if (setUpstream)
            arguments.Add("--set-upstream");
        if (forceWithLease)
            arguments.Add("--force-with-lease");
        arguments.Add(remote);
        arguments.Add($"refs/heads/{localBranch}:refs/heads/{remoteBranch}");
        await WriteAsync(arguments, cancellationToken, await CredentialsForAsync([remote], cancellationToken).ConfigureAwait(false), progress).ConfigureAwait(false);
    }

    /// <summary>Merges a revision into the current branch.</summary>
    public Task MergeAsync(string revision, CancellationToken cancellationToken) => WriteAsync(["merge", "--no-edit", revision], cancellationToken);

    /// <summary>Applies a commit's change on top of the current branch; for a merge, its change against the first parent.</summary>
    public Task CherryPickAsync(GitCommit commit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return WriteAsync(commit.IsMerge ? ["cherry-pick", "--mainline=1", commit.Sha] : ["cherry-pick", commit.Sha], cancellationToken);
    }

    /// <summary>Adds a commit that undoes a commit's change.</summary>
    public Task RevertAsync(GitCommit commit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return WriteAsync(commit.IsMerge ? ["revert", "--no-edit", "--mainline=1", commit.Sha] : ["revert", "--no-edit", commit.Sha], cancellationToken);
    }

    /// <summary>Moves the current branch to a commit.</summary>
    public Task ResetAsync(string sha, ResetMode mode, CancellationToken cancellationToken) =>
        WriteAsync(["reset", "--" + mode.ToString().ToLowerInvariant(), sha], cancellationToken);

    /// <summary>Stashes every local change, including untracked files.</summary>
    public Task StashAsync(string? message, CancellationToken cancellationToken) =>
        WriteAsync(message is { Length: > 0 } ? ["stash", "push", "--include-untracked", "--message", message] : ["stash", "push", "--include-untracked"], cancellationToken);

    /// <summary>Applies a stash entry and drops it when it applied cleanly.</summary>
    public Task UnstashAsync(int index, CancellationToken cancellationToken) => WriteAsync(["stash", "pop", "--index", $"stash@{{{index}}}"], cancellationToken);

    /// <summary>Stops the operation that stopped part way and puts everything back as it was before it.</summary>
    public Task AbortAsync(OngoingOperation operation, CancellationToken cancellationToken) =>
        WriteAsync([Verb(operation), "--abort"], cancellationToken);

    /// <summary>Goes on with the operation that stopped at conflicts once they are resolved and staged.</summary>
    public Task ContinueAsync(OngoingOperation operation, CancellationToken cancellationToken) =>
        operation == OngoingOperation.Merge ? WriteAsync(["commit", "--no-edit"], cancellationToken) : WriteAsync([Verb(operation), "--continue"], cancellationToken);

    /// <summary>Runs a command that only reads, beside others.</summary>
    public Task<string> ReadAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken) =>
        GitProcess.RunCheckedAsync(Root, arguments, cancellationToken);

    /// <summary>Runs a command that changes the repository, after the ones asked for before it.</summary>
    public async Task<string> WriteAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken, GitCredentials? credentials = null,
        IProgress<string>? progress = null, string? input = null)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task previous;
        lock (queueGate)
        {
            previous = queueTail;
            queueTail = done.Task;
        }

        var started = false;
        try
        {
            await previous.WaitAsync(cancellationToken).ConfigureAwait(false);
            started = true;
            Running?.Invoke(this, arguments);
            return await GitProcess.RunCheckedAsync(Root, arguments, cancellationToken, credentials, progress, input).ConfigureAwait(false);
        }
        finally
        {
            // A command cancelled while it waited lets the next one go only after the one it waited for.
            if (started)
                done.SetResult();
            else
                _ = previous.ContinueWith(_ => done.SetResult(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    /// <summary>Asks the credential sources for credentials for the first remote they have them for; null leaves Git to the user's setup.</summary>
    internal async Task<GitCredentials?> CredentialsForAsync(IEnumerable<string> remoteNames, CancellationToken cancellationToken)
    {
        if (resolver is null)
            return null;

        var remotes = await GetRemotesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var remote in remotes.Where(r => remoteNames.Contains(r.Name, StringComparer.Ordinal)))
        {
            foreach (var url in new[] { remote.FetchUrl, remote.PushUrl }.Distinct(StringComparer.Ordinal))
            {
                if (await resolver.GetAsync(url, cancellationToken).ConfigureAwait(false) is { } found)
                    return found;
            }
        }

        return null;
    }

    private static string Verb(OngoingOperation operation) => operation switch
    {
        OngoingOperation.Merge => "merge",
        OngoingOperation.Rebase => "rebase",
        OngoingOperation.CherryPick => "cherry-pick",
        OngoingOperation.Revert => "revert",
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private Task WithPathsAsync(IReadOnlyList<string> arguments, IEnumerable<string> relativePaths, CancellationToken cancellationToken)
    {
        var input = string.Join('\0', relativePaths);
        return input.Length == 0 ? Task.CompletedTask : WriteAsync(["--literal-pathspecs", .. arguments], cancellationToken, input: input);
    }
}
