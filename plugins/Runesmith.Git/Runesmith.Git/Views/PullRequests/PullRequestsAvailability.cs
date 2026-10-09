using System.Composition;
using Runesmith.Sdk.Plugins;
using Runesmith.Sdk.ToolWindows;

namespace Runesmith.Git.Views.PullRequests;

/// <summary>Shows the Pull Requests window only while the open folder's repository has a remote on a host that is signed in; signing in stays one
/// click away on the hosting plugins' own buttons.</summary>
[Export(typeof(IPlugin))]
[method: ImportingConstructor]
internal sealed class PullRequestsAvailability(PullRequestService service, [Import(AllowDefault = true)] IToolWindowManager? toolWindows) : IPlugin
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        service.Hosts.Changed += OnHostsChanged;
        if (service.Repositories is { } repositories)
            repositories.Changed += OnChanged;
        Update();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        service.Hosts.Changed -= OnHostsChanged;
        if (service.Repositories is { } repositories)
            repositories.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Update();

    private void OnHostsChanged(object? sender, EventArgs e) => Avalonia.Threading.Dispatcher.UIThread.Post(Update);

    private void Update() => toolWindows?.SetAvailable(PullRequestsToolWindow.Id, service.Current is { Host.Account: not null });
}
