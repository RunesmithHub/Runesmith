using System.Composition;
using Avalonia.Controls;
using Runesmith.Git.Commands;
using Runesmith.Git.Hosting;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.ToolWindows;

namespace Runesmith.Git.Views.History;

/// <summary>The Git tool window in the bottom area: the log with its branch graph, its filters, and the selected commit's details.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
[method: ImportingConstructor]
internal sealed class GitToolWindow(GitOperations operations, HistoryNavigator navigator, [Import(AllowDefault = true)] ILanguageRegistry? languages,
    RepositoryHosts hosts, [Import(AllowDefault = true)] ILauncher? launcher) : IToolWindowProvider
{
    public ToolWindowDefinition Definition { get; } = Create();

    public Control CreateContent() => new LogView(operations, navigator, languages, hosts, launcher);

    private static ToolWindowDefinition Create()
    {
        GitIcons.Register();
        return new ToolWindowDefinition(GitOperations.LogWindowId, "Git", GitIcons.Branch, DockSide.Bottom) { Order = 5 };
    }
}
