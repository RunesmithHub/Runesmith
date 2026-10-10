using HammerUI;

namespace Runesmith.Shell.Testing;

/// <summary>The Test Explorer's icons that HammerUI does not have, on its 24 × 24 grid.</summary>
internal static class TestIcons
{
    public const string Tests = "test-flask";
    public const string Run = "test-run";
    public const string Debug = "bug";
    public const string Passed = "test-passed";
    public const string Failed = "test-failed";
    public const string Skipped = "test-skipped";
    public const string Queued = "test-queued";
    public const string Running = "test-running";
    public const string NotRun = "test-not-run";
    public const string RunAll = "test-run-all";
    public const string RerunFailed = "test-rerun-failed";

    private static readonly Lazy<bool> Registered = new(() =>
    {
        Icons.Register(Tests, "M10 2v7.5L4.7 19.6A1.6 1.6 0 0 0 6.1 22h11.8a1.6 1.6 0 0 0 1.4-2.4L14 9.5V2", "M8.5 2h7", "M7 16h10");
        Icons.Register(Run, "M7 4.5v15l12-7.5z");
        Icons.Register(Passed, "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20", "m8 12 3 3 5-6");
        Icons.Register(Failed, "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20", "m15 9-6 6", "m9 9 6 6");
        Icons.Register(Skipped, "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20", "M8 12h8");
        Icons.Register(Queued, "M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20", "M12 7v5l3 2");
        Icons.Register(Running, "M21 12a9 9 0 1 1-6.2-8.6");
        Icons.Register(NotRun, "M12 4a8 8 0 1 0 0 16a8 8 0 1 0 0-16");
        Icons.Register(RunAll, "M4 4.5v15l10-7.5z", "M14 4.5v15l7-7.5z");
        Icons.Register(RerunFailed, "M3 12a9 9 0 0 1 15-6.7L21 8", "M21 3v5h-5", "M12 9v4", "M12 16.5h.01");
        return true;
    });

    /// <summary>Adds the icons once; called before anything shows them.</summary>
    public static void Register() => _ = Registered.Value;

    /// <summary>Gets the icon and theme brush a state shows with.</summary>
    public static (string Icon, string Brush) Of(TestState state) => state switch
    {
        TestState.Passed => (Passed, "SuccessBrush"),
        TestState.Failed or TestState.Errored => (Failed, "DangerBrush"),
        TestState.Skipped => (Skipped, "TextMutedBrush"),
        TestState.Queued => (Queued, "TextMutedBrush"),
        TestState.Running => (Running, "AccentBrush"),
        _ => (NotRun, "TextMutedBrush"),
    };
}
