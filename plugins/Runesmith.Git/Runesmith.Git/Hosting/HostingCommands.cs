using System.Composition;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Runesmith.Git.Commands;
using Runesmith.Git.Views;
using Runesmith.Git.Views.Clone;
using Runesmith.Git.Views.PullRequests;
using Runesmith.Plugins.Views;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.VersionControl;

namespace Runesmith.Git.Hosting;

/// <summary>The commands that work with hosting services: cloning, links to a host's pages, and pull requests. Each provider gets its own
/// Open on and Copy Link commands, named after it, which show while the open repository's remote is on one of its hosts.</summary>
[Export(typeof(ICommandContributor))]
[method: ImportingConstructor]
internal sealed class HostingCommands(
    RepositoryHosts hosts,
    HostedRepositories repositories,
    PullRequestActions pullRequests,
    CloneService cloner,
    GitAvatarCache avatars,
    [Import(AllowDefault = true)] IRepositoryService? repository,
    [Import(AllowDefault = true)] IEditorService? editors,
    [Import(AllowDefault = true)] HammerUI.Services.IDialogService? dialogs,
    [Import(AllowDefault = true)] INotificationService? notifications,
    [Import(AllowDefault = true)] Lazy<ICommandService>? commands) : ICommandContributor
{
    public const string CreatePullRequest = "git.createPullRequest";

    private const string Category = "Git";

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        GitIcons.Register();

        registry.Add(new CommandDefinition(CommandIds.CloneRepository, "Clone Repository...", Category)
        {
            Icon = "import",
            Description = "Clone a repository from your accounts on hosting services, or from any Git URL, and open it",
        }, CloneAsync);
        registry.AddMenuItem(new MenuItemDefinition(Menus.File, CommandIds.CloneRepository, "1-new", 10));

        registry.Add(new CommandDefinition(CreatePullRequest, "Create Pull Request...", Category)
        {
            Icon = GitIcons.PullRequest,
            Description = "Propose the current branch's changes in a pull request on the repository's hosting service",
        }, _ => pullRequests.CreateAsync(), _ => Hosted() is not null && repository?.Current?.Branch is not null);
        registry.AddMenuItem(new MenuItemDefinition(GitCommands.Menu, CreatePullRequest, "7-pull-requests", 0));
        registry.AddMenuItem(new MenuItemDefinition(GitCommands.Menu, "view." + PullRequestsToolWindow.Id, "7-pull-requests", 1));

        foreach (var provider in hosts.Providers)
            AddLinks(registry, provider);
    }

    /// <summary>Gets the id part a provider's commands share, such as <c>github</c>.</summary>
    internal static string Key(IRepositoryHostProvider provider) => new([.. provider.Name.ToLowerInvariant().Where(char.IsAsciiLetterOrDigit)]);

    private void AddLinks(ICommandRegistry registry, IRepositoryHostProvider provider)
    {
        var key = Key(provider);
        var menu = $"{Menus.Tools}/{provider.Name}";
        var openOn = $"git.openOn.{key}";
        var copyLink = $"git.copyLink.{key}";
        var openRepository = $"git.openRepositoryOn.{key}";

        bool Owns(object? _) => Hosted() is { } hosted && provider.Hosts.Contains(hosted.Host);
        bool HasLink(object? argument) => Owns(argument) && (argument is not ContextMenuTarget || Link(argument) is not null);

        Add(openOn, $"Open on {provider.Name}", provider.Icon, $"Open the current file, at the selected lines, on {provider.Name}",
            argument => Open(Link(argument) ?? RepositoryLink()), HasLink, 0);
        Add(copyLink, $"Copy {provider.Name} Link", "link", $"Copy the link to the current file and its selected lines on {provider.Name}",
            argument => CopyAsync(Link(argument) ?? RepositoryLink()), HasLink, 1);
        Add(openRepository, $"Open Repository on {provider.Name}", "external-link", $"Open the repository's page on {provider.Name}",
            _ => Open(RepositoryLink()), Owns, 2);

        registry.AddMenuItem(new MenuItemDefinition(ContextMenus.Editor, openOn, GitCommands.ContextGroup, 10));
        registry.AddMenuItem(new MenuItemDefinition(ContextMenus.Editor, copyLink, GitCommands.ContextGroup, 11));
        registry.AddMenuItem(new MenuItemDefinition(ContextMenus.Explorer, openOn, GitCommands.ContextGroup, 10));

        void Add(string id, string title, string icon, string description, Func<object?, Task> execute, Func<object?, bool> canExecute, int order)
        {
            registry.Add(new CommandDefinition(id, title, Category) { Icon = icon, Description = description }, execute, canExecute);
            registry.AddMenuItem(new MenuItemDefinition(menu, id, "2-links", order));
        }
    }

    private HostedRemote? Hosted() => hosts.Find(repository?.Current);

    private async Task CloneAsync(object? argument)
    {
        if (dialogs is null)
        {
            notifications?.Notify(NotificationKind.Error, "Cloning needs a newer Runesmith", "This version of Runesmith does not let plugins show dialogs.");
            return;
        }

        var dialog = new CloneDialog(new CloneModel(hosts, repositories, commands?.Value), avatars, cloner, pullRequests.OpenUrl);
        if (argument is string url && url.Length > 0)
            dialog.UseUrl(url);
        await dialogs.ShowAsync(dialog);
    }

    // From a context menu, the link is to what the menu was opened on; otherwise to the active editor's file at its selected lines.
    private Uri? Link(object? argument)
    {
        if (repository?.Current is not { } info || Hosted() is not { } hosted)
            return null;

        WebTarget? target;
        if (argument is ContextMenuTarget menuTarget)
            target = WebLinks.File(info, menuTarget.Path, menuTarget.Lines, menuTarget.IsDirectory);
        else if (editors?.ActiveEditor is { Document.FilePath: { } path } editor)
            target = WebLinks.File(info, path, WebLinks.Lines(editor.Document.Buffer.Current, editor.Selection));
        else
            target = null;

        return target is null ? null : hosted.Host.GetWebUrl(hosted.Remote.FetchUrl, target);
    }

    private Uri? RepositoryLink() => Hosted() is { } hosted ? hosted.Host.GetWebUrl(hosted.Remote.FetchUrl, new WebTarget(WebTargetKind.Repository)) : null;

    private Task Open(Uri? link)
    {
        if (link is not null)
            pullRequests.OpenUrl(link);
        return Task.CompletedTask;
    }

    private async Task CopyAsync(Uri? link)
    {
        if (link is null || (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.Clipboard is not { } clipboard)
            return;

        await clipboard.SetTextAsync(link.AbsoluteUri);
        notifications?.Notify(NotificationKind.Info, "Link copied", link.AbsoluteUri);
    }
}
