using System.Composition;
using Runesmith.Composition;
using Runesmith.Editor.Input;
using Runesmith.Sdk.Documents;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;

namespace Runesmith.Shell.Editors;

/// <summary>Turns off the keyboard hooks of the plugins the user listed in <see cref="SettingKeys.DisabledKeyHooks"/>, and logs hooks that fail,
/// with their plugin, to the Plugins output channel.</summary>
[Export]
[Shared]
public sealed class KeyHookPolicy
{
    private readonly EditorKeyHooks hooks;
    private readonly ISettingsService settings;

    [ImportingConstructor]
    public KeyHookPolicy(EditorKeyHooks hooks, ISettingsService settings, IOutputService output)
    {
        this.hooks = hooks;
        this.settings = settings;
        hooks.HookFailed += (_, e) => output.GetChannel(PluginAccess.ChannelName)
            .AppendLine($"[{DateTime.Now:HH:mm:ss}] The keyboard hook {e.Hook?.GetType().Name ?? "of a plugin"}{Describe(e.Hook)} failed: {e.Exception}");
        settings.Changed += (_, e) =>
        {
            if (e.Key == SettingKeys.DisabledKeyHooks)
                Apply();
        };
        Apply();
    }

    /// <summary>Gets the plugin a hook comes from, or null for Runesmith's own.</summary>
    public static PluginInfo? PluginOf(IEditorKeyHook hook)
    {
        ArgumentNullException.ThrowIfNull(hook);
        return PluginCallers.Of(hook.GetType().Assembly);
    }

    /// <summary>Reads the plugin ids in the setting: separated by commas, semicolons or spaces.</summary>
    public static IReadOnlySet<string> ParseDisabled(string? value) =>
        (value ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void Apply()
    {
        var disabled = ParseDisabled(settings.Get<string>(SettingKeys.DisabledKeyHooks));
        hooks.IsEnabled = disabled.Count == 0 ? null : hook => PluginOf(hook) is not { } plugin || !disabled.Contains(plugin.Manifest.Id);
    }

    private static string Describe(IEditorKeyHook? hook) => hook is not null && PluginOf(hook) is { } plugin ? $" of {plugin.Manifest.Name} ({plugin.Manifest.Id})" : "";
}
