using System.Runtime.Versioning;
using Microsoft.Win32;
using Runesmith.Hub.Links;

namespace Runesmith.App;

/// <summary>Registers Runesmith for <c>runesmith://</c> links. Windows reads the registration from the current user's registry, which needs no
/// administrator rights; Linux reads it from the menu entry Runesmith ships, and macOS from an application bundle.</summary>
internal static class LinkRegistration
{
    private const string Key = @"Software\Classes\" + HubLink.Scheme;

    /// <summary>Registers this executable for links, or updates the registration when Runesmith moved; when <paramref name="register"/> is off,
    /// removes a registration that points to this executable.</summary>
    public static void Apply(bool register, string executable)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            if (register)
                Register(executable);
            else
                Unregister(executable);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
        }
    }

    /// <summary>Gets the command Windows runs for a link.</summary>
    public static string Command(string executable) => $"\"{executable}\" \"%1\"";

    [SupportedOSPlatform("windows")]
    private static void Register(string executable)
    {
        using (var existing = Registry.CurrentUser.OpenSubKey(Key + @"\shell\open\command"))
        {
            if (existing?.GetValue(null) as string == Command(executable))
                return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(Key);
        key.SetValue(null, "URL:Runesmith");
        key.SetValue("URL Protocol", "");
        using (var icon = key.CreateSubKey("DefaultIcon"))
            icon.SetValue(null, $"\"{executable}\",0");
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue(null, Command(executable));
    }

    [SupportedOSPlatform("windows")]
    private static void Unregister(string executable)
    {
        using (var existing = Registry.CurrentUser.OpenSubKey(Key + @"\shell\open\command"))
        {
            if (existing?.GetValue(null) as string != Command(executable))
                return;
        }

        Registry.CurrentUser.DeleteSubKeyTree(Key, throwOnMissingSubKey: false);
    }
}
