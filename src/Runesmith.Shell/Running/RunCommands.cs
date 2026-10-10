using System.Composition;
using Runesmith.Sdk.Commands;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Sdks;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Forms;
using Runesmith.Shell.Services;

namespace Runesmith.Shell.Running;

/// <summary>Run, Debug, Stop and Edit Configurations, in the Run menu.</summary>
[Export(typeof(ICommandContributor))]
[Export]
[Shared]
[method: ImportingConstructor]
public sealed class RunCommands(
    IRunConfigurationService configurations,
    RunService runs,
    IWorkspace workspace,
    NotificationService notifications,
    Lazy<ICommandService> commands,
    [Import(AllowDefault = true)] Lazy<ISdkService>? sdks,
    [Import(AllowDefault = true)] Lazy<Debugging.IRunDebugger>? debugger) : ICommandContributor
{
    /// <summary>The name of the Run menu.</summary>
    public const string Menu = Menus.Run;

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Add(
            new CommandDefinition(CommandIds.Run, "Run", "Run") { Icon = "play", KeyBinding = "Shift+F10", Description = "Run the selected run configuration." },
            _ => RunSelectedAsync(),
            _ => workspace.RootPath is not null);
        registry.Add(
            new CommandDefinition(CommandIds.Debug, "Debug", "Run") { Icon = "bug", KeyBinding = "Shift+F9", Description = "Debug the selected run configuration." },
            _ => DebugSelectedAsync(),
            _ => WhyCannotDebug(configurations.Selected) is null);
        registry.Add(
            new CommandDefinition(CommandIds.Stop, "Stop", "Run") { Icon = "stop", KeyBinding = "Ctrl+F2", Description = "Stop the selected configuration's run, or the last one that runs." },
            _ =>
            {
                StopSelected();
                return Task.CompletedTask;
            },
            _ => runs.IsRunning);
        registry.Add(
            new CommandDefinition(CommandIds.EditConfigurations, "Edit Configurations...", "Run") { Icon = "settings", Description = "Add, change and remove run configurations." },
            _ => EditConfigurationsAsync(),
            _ => workspace.RootPath is not null);

        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.Run, "1-run", 0));
        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.Debug, "1-run", 1));
        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.Stop, "1-run", 2));
        registry.AddMenuItem(new MenuItemDefinition(Menu, CommandIds.EditConfigurations, "2-configurations", 0));
    }

    /// <summary>Says why a configuration cannot be debugged now, or returns null when it can.</summary>
    public string? WhyCannotDebug(RunConfiguration? configuration)
    {
        if (configuration is null)
            return "Choose a run configuration first.";

        if (configurations.FindType(configuration.TypeId) is { CanDebug: false } type)
            return $"{type.Name} configurations cannot be debugged.";

        return debugger is null ? "No installed plugin provides a debugger." : debugger.Value.WhyCannotDebug;
    }

    /// <summary>Debugs the selected configuration, or opens the configurations dialog when there is none.</summary>
    public async Task DebugSelectedAsync()
    {
        if (configurations.Selected is { } selected)
            await runs.RunAsync(selected, RunMode.Debug);
        else
            await EditConfigurationsAsync();
    }

    /// <summary>Runs the selected configuration, or opens the configurations dialog when there is none.</summary>
    public async Task RunSelectedAsync()
    {
        if (configurations.Selected is { } selected)
            await runs.RunAsync(selected);
        else
            await EditConfigurationsAsync();
    }

    /// <summary>Stops the selected configuration's run, or the last run that has not finished.</summary>
    public void StopSelected()
    {
        var session = (configurations.Selected is { } selected ? runs.FindActive(selected.Name) : null)
            ?? runs.Sessions.LastOrDefault(s => s.State != RunState.Finished);
        session?.Stop();
    }

    /// <summary>Shows the configurations dialog and saves what it returns.</summary>
    public async Task EditConfigurationsAsync()
    {
        if (workspace.RootPath is null || notifications.Host.Dialogs?.IsOpen == true)
            return;

        var services = new OptionFormServices(sdks?.Value, commands.Value);
        var dialog = new RunConfigurationsDialog(configurations, await configurations.GetOptionsAsync(), services);
        if (await notifications.Dialogs.ShowAsync(dialog) is not RunConfigurationsResult result)
            return;

        try
        {
            configurations.Save(result.Configurations, result.Selected);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            notifications.Notify(NotificationKind.Error, "The run configurations could not be saved", exception.Message);
        }
    }
}
