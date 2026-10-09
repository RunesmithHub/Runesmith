using Runesmith.Sdk.VersionControl;

namespace Runesmith.Plugins.Gitea;

/// <summary>Shows the add-account UI; the views implement it, so the provider can be tested without them.</summary>
internal interface IAddAccountPrompt
{
    /// <summary>Asks for the address of the server to add an account on, and checks that it is a server the plugin takes; null when cancelled.</summary>
    Task<GiteaServer?> AskServerAsync(CancellationToken cancellationToken);
}

/// <summary>The accounts of a plugin, on any number of servers: one host per account, signed in or waiting to be, kept in a list in the state
/// folder with their tokens in the secret store. Before the first account, it offers one for the default server.</summary>
internal class GiteaHostProvider : IRepositoryHostProvider
{
    private readonly GiteaContext context;
    private readonly AccountStore store;
    private readonly List<GiteaHost> accounts = [];
    private readonly Lock gate = new();
    private GiteaHost? placeholder;

    /// <summary>Creates the provider and reads the list of accounts; their tokens load with <see cref="LoadAsync"/>.</summary>
    public GiteaHostProvider(GiteaContext context, AccountStore store)
    {
        ArgumentNullException.ThrowIfNull(context);
        this.context = context;
        this.store = store;
        foreach (var account in store.Load())
        {
            if (GiteaServer.Parse(account.Server) is { } server && account.Login is { Length: > 0 }
                && !accounts.Any(host => host.Server == server && string.Equals(host.Login, account.Login, StringComparison.OrdinalIgnoreCase)))
                accounts.Add(Track(new GiteaHost(context, server, account with { Server = server.Key }, () => SignInPrompt)));
        }
    }

    public event EventHandler? Changed;

    public string Name => context.Flavor.Name;

    public string Icon => context.Flavor.Icon;

    public GiteaContext Context => context;

    /// <summary>Gets or sets the UI that signs accounts in, once the views exist.</summary>
    public ISignInPrompt? SignInPrompt { get; set; }

    /// <summary>Gets or sets the UI that asks for a server, once the views exist.</summary>
    public IAddAccountPrompt? AddAccountPrompt { get; set; }

    public IReadOnlyList<IRepositoryHost> Hosts => Accounts;

    /// <summary>Gets the accounts, or the default server's account waiting for its first sign-in while there are none.</summary>
    public IReadOnlyList<GiteaHost> Accounts
    {
        get
        {
            lock (gate)
            {
                if (accounts.Count > 0)
                    return [.. accounts];

                placeholder ??= Track(new GiteaHost(context, context.Flavor.DefaultServer, new StoredAccount(context.Flavor.DefaultServer.Key, null, null, null),
                    () => SignInPrompt));
                return [placeholder];
            }
        }
    }

    /// <summary>Reads every account's tokens from the secret store.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        foreach (var host in Accounts)
            await host.LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IRepositoryHost?> AddAccountAsync(CancellationToken cancellationToken)
    {
        if (AddAccountPrompt is not { } ask || await ask.AskServerAsync(cancellationToken).ConfigureAwait(true) is not { } server)
            return null;

        var host = NewAccount(server);
        return await host.SignInAsync(cancellationToken).ConfigureAwait(true) ? Find(host) : null;
    }

    /// <summary>Creates an account on a server that is not in the list until it signs in.</summary>
    public GiteaHost NewAccount(GiteaServer server) =>
        Track(new GiteaHost(context, server, new StoredAccount(server.Key, null, null, null), () => SignInPrompt));

    /// <summary>Signs an account out and takes it off the list.</summary>
    public async Task RemoveAsync(GiteaHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        await host.SignOutAsync().ConfigureAwait(true);
        lock (gate)
        {
            if (!accounts.Remove(host))
                return;
        }

        Save();
        RaiseChanged();
    }

    // A login can sign in again through another host object, such as Add Account for an existing account; the list keeps the first.
    private GiteaHost Find(GiteaHost host)
    {
        lock (gate)
            return accounts.FirstOrDefault(account => account.Server == host.Server && string.Equals(account.Login, host.Login, StringComparison.OrdinalIgnoreCase)) ?? host;
    }

    private GiteaHost Track(GiteaHost host)
    {
        host.ProfileChanged += OnProfileChanged;
        return host;
    }

    private void OnProfileChanged(object? sender, EventArgs e)
    {
        if (sender is not GiteaHost host)
            return;

        var listChanged = false;
        lock (gate)
        {
            var existing = accounts.FirstOrDefault(account => account != host && account.Server == host.Server
                && string.Equals(account.Login, host.Login, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                // The same account signed in through another host object; the one in the list takes the new tokens.
                existing.TakeTokensFrom(host);
                listChanged = accounts.Remove(host);
            }
            else if (!accounts.Contains(host))
            {
                accounts.Add(host);
                if (host == placeholder)
                    placeholder = null;
                listChanged = true;
            }
        }

        Save();
        if (listChanged)
            RaiseChanged();
    }

    private void Save()
    {
        lock (gate)
            store.Save(accounts.Select(host => host.Profile));
    }

    private void RaiseChanged() => GiteaContext.OnUiThread(() => Changed?.Invoke(this, EventArgs.Empty));
}
