using Avalonia.Media;
using HammerUI;

namespace Runesmith.GitHub.Views.Account;

/// <summary>The GitHub plugin's icons, on HammerUI's 24 by 24 stroke grid; registered before any of its UI is created.</summary>
internal static class GitHubIcons
{
    public const string GitHub = "github";
    public const string LogOut = "log-out";
    public const string KeyRound = "key-round";
    public const string Building = "building-2";

    static GitHubIcons()
    {
        Icons.Register(GitHub,
            "M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4",
            "M9 18c-4.51 2-5-2-7-2");
        Icons.Register(LogOut, "M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4", "m16 17 5-5-5-5", "M21 12H9");
        Icons.Register(Building, "M6 22V4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v18Z", "M6 12H4a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h2", "M18 9h2a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-2",
            "M10 6h4", "M10 10h4", "M10 14h4", "M10 18h4");
        Icons.Register(KeyRound, "M2.586 17.414A2 2 0 0 0 2 18.828V21a1 1 0 0 0 1 1h3a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h1a1 1 0 0 0 1-1v-1a1 1 0 0 1 1-1h.172a2 2 0 0 0 1.414-.586l.814-.814a6.5 6.5 0 1 0-4-4z",
            Circle(16.5, 7.5, 0.5));
    }

    /// <summary>Makes sure the icons are registered; call it before creating UI or definitions that name them.</summary>
    public static void EnsureRegistered()
    {
    }

    /// <summary>Gets an icon by name, after registering the plugin's icons.</summary>
    public static Geometry Get(string name) => Icons.Find(name) ?? Icons.Info;

    private static string Circle(double x, double y, double r) =>
        FormattableString.Invariant($"M{x - r} {y}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0");
}
