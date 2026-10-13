using Runesmith.Composition;

namespace Runesmith.App;

/// <summary>Reports problems found before the window shows to the standard error output and a log file, since there is no window to show
/// them in yet.</summary>
internal static class StartupReports
{
    /// <summary>Reports that Runesmith's own assemblies do not fit together, so it starts in safe mode.</summary>
    public static void IncompleteBuild(IReadOnlyList<HostMismatch> mismatches)
    {
        var details = string.Join(Environment.NewLine, mismatches.Select(Describe));
        var log = CrashLog.Write("incomplete", "Runesmith started in safe mode because this build of it is incomplete", details);
        Console.Error.WriteLine("This build of Runesmith is incomplete, so it starts in safe mode. Rebuild Runesmith.");
        Console.Error.WriteLine(details);
        if (log is not null)
            Console.Error.WriteLine(Strings.AppDetailsInLog(log));
    }

    /// <summary>Reports a start whose window has not shown, with the plugins it got through.</summary>
    public static void Stuck(string report, StartRecord progress)
    {
        var plugins = progress.Plugins.Select(p => $"  {p.Name} {p.Version} ({p.Id})").ToList();
        var details = plugins.Count == 0 ? report : string.Join(Environment.NewLine, [report, "", "Plugins so far:", .. plugins]);
        var log = CrashLog.Write("stuck", "Runesmith's window had not shown", details);
        Console.Error.WriteLine(log is null ? report : report + " " + Strings.AppDetailsInLog(log));
    }

    internal static string Describe(HostMismatch mismatch) => mismatch.RequiredBy is { } user
        ? $"{user} was built against {mismatch.Assembly} {mismatch.Expected}, and this build has {mismatch.Assembly} {mismatch.Found}."
        : $"{mismatch.Assembly} is {mismatch.Found}, and the plugin API is {mismatch.Expected}.";
}
