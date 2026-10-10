using HammerUI;

namespace Runesmith.Shell.Terminal;

/// <summary>The terminal's icons that HammerUI does not have, on its 24 × 24 grid.</summary>
internal static class TerminalIcons
{
    public const string Terminal = "square-terminal";

    private static readonly Lazy<bool> Registered = new(() =>
    {
        Icons.Register(Terminal, "m7 11 2-2-2-2", "M11 13h4", "M5 3h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z");
        return true;
    });

    /// <summary>Adds the icons once; called before anything shows them.</summary>
    public static void Register() => _ = Registered.Value;
}
