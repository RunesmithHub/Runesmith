using System.Composition;
using System.Globalization;
using Avalonia.Controls;
using Runesmith.Git.Commands;
using Runesmith.Git.Repositories;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.ToolWindows;

namespace Runesmith.Git.Views.Changes;

/// <summary>The Commit tool window on the left stripe, under Explorer, with the number of changed files on its button.</summary>
[Export(typeof(IToolWindowProvider))]
[Export]
[Shared]
internal sealed class CommitToolWindow : IToolWindowProvider
{
    private readonly GitOperations operations;
    private readonly ISettingsService settings;
    private readonly ICommandService commands;
    private readonly IEditorService editors;
    private readonly ILanguageRegistry? languages;
    private readonly ILauncher? launcher;
    private readonly Lazy<IToolWindowManager> toolWindows;
    private string? badge;

    [ImportingConstructor]
    public CommitToolWindow(GitOperations operations, RepositoryService repositories, ISettingsService settings, ICommandService commands, IEditorService editors,
        Lazy<IToolWindowManager> toolWindows, [Import(AllowDefault = true)] ILanguageRegistry? languages, [Import(AllowDefault = true)] ILauncher? launcher)
    {
        GitIcons.Register();
        this.operations = operations;
        this.settings = settings;
        this.commands = commands;
        this.editors = editors;
        this.toolWindows = toolWindows;
        this.languages = languages;
        this.launcher = launcher;
        repositories.Changed += (_, _) => UpdateBadge(repositories);
    }

    public ToolWindowDefinition Definition { get; } = new(GitOperations.CommitWindowId, "Commit", GitIcons.Commit, DockSide.Left) { Order = 10 };

    public Control CreateContent() => new CommitView(operations, settings, commands, editors, languages, launcher);

    private void UpdateBadge(RepositoryService repositories)
    {
        var count = repositories.Status?.ChangedCount ?? 0;
        var text = repositories.Current is null || count == 0 ? null : count > 99 ? "99+" : count.ToString(CultureInfo.CurrentCulture);
        if (text == badge)
            return;
        badge = text;
        toolWindows.Value.SetBadge(Definition.Id, text);
    }
}
