using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

namespace Runesmith.Composition;

/// <summary>Tells which plugin code belongs to, from the load context its assembly was loaded into, so services can keep each plugin's
/// data apart and check what it declared.</summary>
public static class PluginCallers
{
    /// <summary>Gets the plugin an assembly belongs to, or null when it is part of Runesmith or .NET.</summary>
    public static PluginInfo? Of(Assembly assembly) => AssemblyLoadContext.GetLoadContext(assembly) switch
    {
        PluginLoadContext context => context.Plugin,
        ContractsLoadContext contracts => contracts.OwnerOf(assembly),
        _ => null,
    };

    /// <summary>Gets the plugin that called the service calling this method, or null when Runesmith did.</summary>
    /// <remarks>The caller is the code nearest on the call stack outside the service's own assembly and .NET, so the service's own helpers
    /// and the steps of an async method do not count.</remarks>
    public static PluginInfo? Current()
    {
        var frames = new StackTrace(1, fNeedFileInfo: false).GetFrames();
        var service = frames.Length > 0 ? frames[0].GetMethod()?.DeclaringType?.Assembly : null;
        foreach (var frame in frames)
        {
            if (frame.GetMethod()?.DeclaringType?.Assembly is not { } assembly || assembly == service || IsFramework(assembly))
                continue;

            return Of(assembly);
        }

        return null;
    }

    private static bool IsFramework(Assembly assembly) =>
        assembly == typeof(object).Assembly
        || (AssemblyLoadContext.GetLoadContext(assembly) == AssemblyLoadContext.Default && assembly.GetName().Name is { } name && HostRuntime.IsFramework(name));
}
