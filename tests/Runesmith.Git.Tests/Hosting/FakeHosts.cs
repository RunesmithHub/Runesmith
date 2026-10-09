using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Tests.Hosting;

/// <summary>A host on a made-up server that owns the remotes on that server and answers from lists the test sets.</summary>
internal sealed class FakeHost(string id, string server, string? account = null) : IRepositoryHost
{
    public string Id => id;

    public string Name => server;

    public string Icon => "globe";

    public string Server => server;

    public string? Account { get; set; } = account;

    public Uri? AccountAvatarUrl => null;

    public bool SupportsDraftPullRequests => true;

    public Uri? ManageAccessUrl => null;

    public List<HostedRepository> Repositories { get; } = [];

    public List<PullRequest> PullRequests { get; } = [];

    public int RepositoryLoads { get; private set; }

    public int PullRequestLoads { get; private set; }

    /// <summary>Gets or sets the failure listing pull requests ends in, if any.</summary>
    public Exception? Failure { get; set; }

    /// <summary>Gets or sets the account a sign-in ends in, or null for a cancelled one.</summary>
    public string? SignsInAs { get; set; }

    public event EventHandler? Changed;

    public Task<bool> SignInAsync(CancellationToken cancellationToken)
    {
        Account = SignsInAs ?? Account;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(Account is not null);
    }

    public Task SignOutAsync()
    {
        Account = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<HostedRepository>> GetRepositoriesAsync(CancellationToken cancellationToken)
    {
        RepositoryLoads++;
        return Task.FromResult<IReadOnlyList<HostedRepository>>([.. Repositories]);
    }

    public bool Owns(string remoteUrl) => Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri) && uri.Host == server;

    public Uri? GetWebUrl(string remoteUrl, WebTarget target) =>
        Owns(remoteUrl) ? new Uri($"https://{server}/{WebName(remoteUrl)}/{target.Kind.ToString().ToLowerInvariant()}/{target.Revision}/{target.Path}") : null;

    public Task<string?> GetDefaultBranchAsync(string remoteUrl, CancellationToken cancellationToken) => Task.FromResult<string?>("main");

    public Task<IReadOnlyList<PullRequest>> GetPullRequestsAsync(string remoteUrl, CancellationToken cancellationToken)
    {
        PullRequestLoads++;
        return Failure is { } failure ? Task.FromException<IReadOnlyList<PullRequest>>(failure) : Task.FromResult<IReadOnlyList<PullRequest>>([.. PullRequests]);
    }

    public Task<PullRequest> CreatePullRequestAsync(string remoteUrl, PullRequestDraft draft, CancellationToken cancellationToken) =>
        Task.FromResult(new PullRequest(PullRequests.Count + 1, draft.Title, Account ?? "", draft.HeadBranch, draft.BaseBranch, new Uri($"https://{server}/pull")));

    private static string WebName(string remoteUrl) => new Uri(remoteUrl).AbsolutePath.Trim('/').Replace(".git", "", StringComparison.Ordinal);
}

/// <summary>A provider of fake hosts; adding an account adds the host the test prepared, or none when it prepared none.</summary>
internal sealed class FakeProvider(string name, params FakeHost[] hosts) : IRepositoryHostProvider
{
    private readonly List<IRepositoryHost> hosts = [.. hosts];

    public string Name => name;

    public string Icon => "globe";

    public IReadOnlyList<IRepositoryHost> Hosts => hosts;

    /// <summary>Gets or sets the host the next Add Account adds.</summary>
    public FakeHost? NextAccount { get; set; }

    public event EventHandler? Changed;

    public Task<IRepositoryHost?> AddAccountAsync(CancellationToken cancellationToken)
    {
        if (NextAccount is not { } added)
            return Task.FromResult<IRepositoryHost?>(null);

        hosts.Add(added);
        NextAccount = null;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.FromResult<IRepositoryHost?>(added);
    }
}

/// <summary>A repository service that reports the repository the test sets.</summary>
internal sealed class FakeRepositoryService(RepositoryInfo? current = null) : IRepositoryService
{
    public RepositoryInfo? Current { get; set; } = current;

    public int Refreshes { get; private set; }

    public event EventHandler? Changed;

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Refreshes++;
        Changed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <summary>Gets a repository at a made-up folder with the given remotes, each fetching and pushing at one URL.</summary>
    public static RepositoryInfo Repository(params (string Name, string Url)[] remotes) =>
        new(OperatingSystem.IsWindows() ? @"C:\src\hello" : "/src/hello", "main", "abc123", "origin/main", 0, 0, [.. remotes.Select(r => new GitRemote(r.Name, r.Url, r.Url))]);
}

/// <summary>A command service that knows one command, records what ran, and runs nothing.</summary>
internal sealed class FakeCommands(string known) : Runesmith.Sdk.Commands.ICommandService
{
    public bool CanRun { get; set; } = true;

    public List<string> Executed { get; } = [];

    public IReadOnlyList<Runesmith.Sdk.Commands.CommandDefinition> Commands => [];

    public Runesmith.Sdk.Commands.CommandDefinition? Find(string commandId) => null;

    public bool CanExecute(string commandId, object? argument = null) => CanRun && commandId == known;

    public Task<bool> ExecuteAsync(string commandId, object? argument = null)
    {
        if (!CanExecute(commandId, argument))
            return Task.FromResult(false);

        Executed.Add(commandId);
        return Task.FromResult(true);
    }

    public string? GetKeyBinding(string commandId) => null;

#pragma warning disable CS0067
    public event EventHandler? Changed;
#pragma warning restore CS0067
}
