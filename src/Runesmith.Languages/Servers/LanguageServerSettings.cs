using System.Composition;
using Runesmith.Sdk.Languages;
using Runesmith.Sdk.Settings;

namespace Runesmith.Languages.Servers;

/// <summary>The settings of each language server: whether it runs and which executable it runs, plus protocol tracing.</summary>
[Export(typeof(ISettingContributor))]
[Shared]
[method: ImportingConstructor]
public sealed class LanguageServerSettings([ImportMany] IEnumerable<LanguageServerDefinition> servers) : ISettingContributor
{
    private const string Category = "Languages";

    /// <summary>The key of the setting that writes every message to and from language servers to their output channels.</summary>
    public const string Trace = "languageServers.trace";

    public IEnumerable<SettingDefinition> Settings =>
        servers.SelectMany(IEnumerable<SettingDefinition> (server) =>
        [
            new(EnabledKey(server.Id), $"Run {server.Name}", Category, true)
            {
                Description = $"Start {server.Name} for {string.Join(", ", server.LanguageIds)} files, for completion, hover, go to definition and problems.",
            },
            new(CommandKey(server.Id), $"{server.Name} command", Category, "")
            {
                Description = $"The executable to run instead of {server.Command}, such as a full path. Leave empty to use {server.Command} from the PATH.",
            },
        ]).Append(new SettingDefinition(Trace, "Trace language servers", Category, false)
        {
            Description = "Write every message to and from language servers to their output channels, for finding out why a server misbehaves.",
        });

    /// <summary>Gets the key of the setting that turns a server on or off.</summary>
    public static string EnabledKey(string serverId) => $"languageServers.{serverId}.enabled";

    /// <summary>Gets the key of the setting that replaces a server's executable.</summary>
    public static string CommandKey(string serverId) => $"languageServers.{serverId}.command";
}
