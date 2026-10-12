using System.Composition;
using Runesmith.Sdk.Sdks;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Templates;
using Runesmith.Shell.Commands;
using Runesmith.Shell.Editors;
using Runesmith.Shell.Forms;
using Runesmith.Shell.Pages;
using Runesmith.Shell.Session;

namespace Runesmith.Shell.Services;

/// <summary>Shows the New Project dialog and opens what it creates.</summary>
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class NewProjectService(
    [ImportMany] IEnumerable<Lazy<IProjectTemplateProvider>> providers,
    [ImportMany] IEnumerable<Lazy<IProjectCreationStep>> steps,
    NotificationService notifications,
    IOutputService output,
    Lazy<Workbench> workbench,
    Lazy<EditorService> editors,
    Lazy<CommandService> commands,
    [Import(AllowDefault = true)] Lazy<ISdkService>? sdks)
{
    /// <summary>The name of the output channel that logs templates that fail to load and projects that fail to be created.</summary>
    public const string OutputChannel = "New Project";

    /// <summary>Shows the dialog; once a project is created, opens its folder and its main files.</summary>
    public async Task ShowAsync()
    {
        if (notifications.Host.Dialogs?.IsOpen == true)
            return;

        var log = output.GetChannel(OutputChannel);
        var loaded = new List<IProjectTemplateProvider>();
        foreach (var provider in providers)
        {
            try
            {
                loaded.Add(provider.Value);
            }
            catch (Exception exception)
            {
                log.AppendLine($"A template provider could not start: {exception}");
            }
        }

        var services = new OptionFormServices(sdks?.Value, commands.Value);
        var dialog = new NewProjectDialog(loaded, services, log, AvailableSteps(log), NewProjectSession.Load());
        if (await notifications.Dialogs.ShowAsync(dialog) is not ProjectCreationResult result)
            return;

        if (!await workbench.Value.OpenFolderAsync(result.Folder))
            return;

        foreach (var file in result.FilesToOpen.Where(File.Exists))
            await editors.Value.OpenAsync(file);
    }

    // The steps plugins add that can run on this computer; one that fails to load or to answer is left out.
    private List<IProjectCreationStep> AvailableSteps(IOutputChannel log)
    {
        var available = new List<IProjectCreationStep>();
        foreach (var step in steps)
        {
            try
            {
                if (step.Value.IsAvailable)
                    available.Add(step.Value);
            }
            catch (Exception exception)
            {
                log.AppendLine($"A project creation step could not start: {exception}");
            }
        }

        return available;
    }
}
