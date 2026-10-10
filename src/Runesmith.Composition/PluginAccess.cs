namespace Runesmith.Composition;

/// <summary>Checks that a plugin declared the capability a service needs before the service does the work.</summary>
public static class PluginAccess
{
    /// <summary>The output channel refusals are written to.</summary>
    public const string ChannelName = "Plugins";

    /// <summary>Logs and refuses a plugin that uses what needs a capability it did not declare; Runesmith itself and plugins with manifests
    /// in the older format may use everything.</summary>
    /// <exception cref="UnauthorizedAccessException">The plugin did not declare the capability.</exception>
    public static void Demand(PluginInfo? caller, string capability, string service, Action<string> log)
    {
        if (caller is null || caller.Manifest.Allows(capability))
            return;

        var message = $"{caller.Manifest.Name} ({caller.Manifest.Id}) cannot use the {service}: its manifest does not declare the {capability} capability.";
        log(message);
        throw new UnauthorizedAccessException(message);
    }
}
