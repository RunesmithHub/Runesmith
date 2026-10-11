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
            new(EnabledKey(server.Id), Strings.LanguagesSettingRun(server.Name), Category, true)
            {
                Description = Strings.LanguagesSettingRunDescription(server.Name, string.Join(", ", server.LanguageIds)),
            },
            new(CommandKey(server.Id), Strings.LanguagesSettingCommand(server.Name), Category, "")
            {
                Description = Strings.LanguagesSettingCommandDescription(server.Command),
            },
        ]).Append(new SettingDefinition(Trace, Strings.LanguagesSettingTrace, Category, false)
        {
            Description = Strings.LanguagesSettingTraceDescription,
        });

    /// <summary>Gets the key of the setting that turns a server on or off.</summary>
    public static string EnabledKey(string serverId) => $"languageServers.{serverId}.enabled";

    /// <summary>Gets the key of the setting that replaces a server's executable.</summary>
    public static string CommandKey(string serverId) => $"languageServers.{serverId}.command";
}
