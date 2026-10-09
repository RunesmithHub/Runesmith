namespace Runesmith.Languages.Java.Jdk;

/// <summary>An installed JDK.</summary>
/// <param name="HomePath">The JDK's home folder, the one with <c>bin</c>, <c>lib</c> and the <c>release</c> file.</param>
/// <param name="Version">The full version from the <c>release</c> file, such as <c>25.0.4.1</c> or <c>1.8.0_392</c>.</param>
/// <param name="FeatureVersion">The Java release it is, such as 25 or 8.</param>
public sealed record JdkInstallation(string HomePath, string Version, int FeatureVersion)
{
    /// <summary>Gets the path of the per-release API data JDK 9 and later have.</summary>
    public string CtSymPath => Path.Combine(HomePath, "lib", "ct.sym");

    /// <summary>Gets the path of JDK 8's class library.</summary>
    public string RuntimeJarPath => Path.Combine(HomePath, "jre", "lib", "rt.jar");

    /// <summary>Gets the release numbers this JDK can give the API of: 8 up to its own.</summary>
    public bool CanServe(int release) => release >= 8 && release <= FeatureVersion;

    /// <summary>Reads a JDK's version from its <c>release</c> file, or returns null when the folder is not a JDK.</summary>
    public static JdkInstallation? FromHome(string homePath)
    {
        var releaseFile = Path.Combine(homePath, "release");
        if (!File.Exists(releaseFile))
            return null;

        try
        {
            foreach (var line in File.ReadLines(releaseFile))
            {
                if (!line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal))
                    continue;

                var version = line["JAVA_VERSION=".Length..].Trim().Trim('"');
                if (ParseFeatureVersion(version) is not { } feature)
                    return null;

                var home = Path.GetFullPath(homePath);
                var hasApi = File.Exists(Path.Combine(home, "lib", "ct.sym")) || File.Exists(Path.Combine(home, "jre", "lib", "rt.jar"));
                return hasApi ? new JdkInstallation(home, version, feature) : null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    /// <summary>Gets the feature release of a version text: 8 for <c>1.8.0_392</c>, 21 for <c>21.0.2</c>, 25 for <c>25-ea</c>.</summary>
    public static int? ParseFeatureVersion(string version)
    {
        var text = version.StartsWith("1.", StringComparison.Ordinal) ? version[2..] : version;
        var end = 0;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
            end++;
        return end > 0 && int.TryParse(text.AsSpan(0, end), System.Globalization.CultureInfo.InvariantCulture, out var feature) ? feature : null;
    }
}
