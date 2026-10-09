using System.Composition;
using Runesmith.Sdk.Commands;

namespace Runesmith.Languages.Servers;

/// <summary>The commands that show a language server's output and restart the servers.</summary>
[Export(typeof(ICommandContributor))]
[Shared]
[method: ImportingConstructor]
public sealed class LanguageServerCommands(Lazy<LanguageServerManager> manager) : ICommandContributor
{
    /// <summary>Shows a server's output; the argument is the server's id, or null for the server that most needs attention.</summary>
    public const string ShowServerOutput = "languages.showServerOutput";

    /// <summary>Stops every language server and starts the ones that open documents need again.</summary>
    public const string RestartServers = "languages.restartServers";

    /// <summary>Runs a command of a language server, such as one a code action or a code lens names; the argument says which.</summary>
    public const string RunServerCommand = "languages.runServerCommand";

    public void Contribute(ICommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Add(
            new CommandDefinition(ShowServerOutput, "Show Language Server Output", "Languages") { Icon = "terminal" },
            argument =>
            {
                manager.Value.ShowOutput(argument as string);
                return Task.CompletedTask;
            });
        registry.Add(
            new CommandDefinition(RestartServers, "Restart Language Servers", "Languages")
            {
                Icon = "refresh",
                Description = "Stops every language server and starts them again, including ones that were not installed or kept stopping.",
            },
            _ => manager.Value.RestartAsync());
        registry.Add(
            new CommandDefinition(RunServerCommand, "Run Language Server Command", "Languages") { ShowInPalette = false },
            argument => argument is ServerCommandInvocation invocation ? invocation.Client.ExecuteCommandAsync(invocation.Command, CancellationToken.None) : Task.CompletedTask,
            argument => argument is ServerCommandInvocation { Client.State: Lsp.LanguageServerState.Running });
    }
}
