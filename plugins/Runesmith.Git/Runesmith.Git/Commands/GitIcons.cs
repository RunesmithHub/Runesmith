using Avalonia.Media;
using HammerUI;

namespace Runesmith.Git.Commands;

/// <summary>The icons of the Git features, on HammerUI's 24 by 24 stroke grid, registered by name before the first control shows them.</summary>
internal static class GitIcons
{
    public const string Branch = "git-branch";
    public const string Commit = "git-commit";
    public const string Merge = "git-merge";
    public const string History = "history";
    public const string Pull = "arrow-down-to-line";
    public const string Push = "arrow-up-from-line";
    public const string Fetch = "refresh";
    public const string PullRequest = "git-pull-request";
    public const string Globe = "globe";
    public const string CircleDot = "circle-dot";

    private static int registered;

    /// <summary>Registers the icons once; any thread may call it.</summary>
    public static void Register()
    {
        if (Interlocked.Exchange(ref registered, 1) == 1)
            return;

        Icons.Register(Branch, "M6 3v12", Circle(18, 6, 3), Circle(6, 18, 3), "M18 9a9 9 0 0 1-9 9");
        Icons.Register(Commit, Circle(12, 12, 3), "M3 12h6", "M15 12h6");
        Icons.Register(Merge, Circle(18, 18, 3), Circle(6, 6, 3), "M6 21V9a9 9 0 0 0 9 9");
        Icons.Register(History, "M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8", "M3 3v5h5", "M12 7v5l4 2");
        Icons.Register(Pull, "M12 17V3", "m6 11 6 6 6-6", "M19 21H5");
        Icons.Register(Push, "m18 9-6-6-6 6", "M12 3v14", "M5 21h14");
        Icons.Register(PullRequest, Circle(18, 18, 3), Circle(6, 6, 3), "M13 6h3a2 2 0 0 1 2 2v7", "M6 9v12");
        Icons.Register(Globe, Circle(12, 12, 10), "M12 2a14.5 14.5 0 0 0 0 20 14.5 14.5 0 0 0 0-20", "M2 12h20");
        Icons.Register(CircleDot, Circle(12, 12, 10), Circle(12, 12, 1));
    }

    /// <summary>Gets an icon by name, such as a host's, after registering the Git icons; unknown names give a neutral one.</summary>
    public static Geometry Get(string? name)
    {
        Register();
        return Icons.Find(name) ?? Icons.Info;
    }

    private static string Circle(double x, double y, double r) =>
        FormattableString.Invariant($"M{x - r} {y}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0");
}
