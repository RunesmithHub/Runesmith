using HammerUI;

namespace Runesmith.Shell.Debugging;

/// <summary>The debugger's icons that HammerUI does not have, on its 24 × 24 grid.</summary>
internal static class DebugIcons
{
    public const string Breakpoint = "debug-breakpoint";
    public const string LogPoint = "debug-log-point";
    public const string CurrentLine = "debug-current-line";
    public const string StepOver = "debug-step-over";
    public const string StepInto = "debug-step-into";
    public const string StepOut = "debug-step-out";
    public const string Continue = "step-forward";

    private static readonly Lazy<bool> Registered = new(() =>
    {
        Icons.Register(Breakpoint, "M6 12a6 6 0 1 0 12 0a6 6 0 1 0 -12 0");
        Icons.Register(LogPoint, "M12 5 19 12 12 19 5 12Z");
        Icons.Register(CurrentLine, "M4 7h10l6 5-6 5H4Z");
        Icons.Register(StepOver, "M4 13a8 8 0 0 1 15-3.5", "M20 5v5h-5", "M10.5 19a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0 -3 0");
        Icons.Register(StepInto, "M12 3v11", "m8 10 4 4 4-4", "M10.5 19.5a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0 -3 0");
        Icons.Register(StepOut, "M12 14V3", "m8 7 4-4 4 4", "M10.5 19.5a1.5 1.5 0 1 0 3 0a1.5 1.5 0 1 0 -3 0");
        return true;
    });

    /// <summary>Adds the icons once; called before anything shows them.</summary>
    public static void Register() => _ = Registered.Value;
}
