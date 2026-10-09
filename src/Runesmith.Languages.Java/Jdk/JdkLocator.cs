namespace Runesmith.Languages.Java.Jdk;

/// <summary>Where to look for JDKs; the defaults read the environment and the usual install folders of this system.</summary>
public sealed record JdkSearchOptions
{
    /// <summary>Gets the <c>JAVA_HOME</c> to use, or null to read the environment variable.</summary>
    public string? JavaHome { get; init; } = Environment.GetEnvironmentVariable("JAVA_HOME");

    /// <summary>Gets JDK homes named by versioned variables such as <c>JAVA_HOME_21_X64</c>, which build machines set for each JDK they
    /// have beside the one <c>JAVA_HOME</c> names.</summary>
    public IReadOnlyList<string> VersionedJavaHomes { get; init; } = VersionedHomes();

    /// <summary>Gets the folders searched for <c>java</c>, or null to read the <c>PATH</c>.</summary>
    public IReadOnlyList<string>? PathFolders { get; init; }

    /// <summary>Gets folders whose subfolders are JDK homes, or null for this system's usual ones.</summary>
    public IReadOnlyList<string>? InstallFolders { get; init; }

    /// <summary>Gets more folders whose subfolders are JDK homes, searched besides <see cref="InstallFolders"/>, such as Runesmith's own.</summary>
    public IReadOnlyList<string> ExtraInstallFolders { get; init; } = [];

    /// <summary>Gets the home of the JDK the user chose, listed before the others, or null.</summary>
    public string? PreferredHome { get; init; }

    private static string[] VersionedHomes() =>
    [
        .. Environment.GetEnvironmentVariables().Keys.OfType<string>()
            .Where(name => System.Text.RegularExpressions.Regex.IsMatch(name, @"^JAVA_HOME_\d+_(X64|ARM64|AARCH64)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(name => Environment.GetEnvironmentVariable(name))
            .OfType<string>()
            .Where(home => home.Length > 0),
    ];
}

/// <summary>Finds installed JDKs: through <c>JAVA_HOME</c>, <c>java</c> on the <c>PATH</c>, and the usual install folders on each system.</summary>
public static class JdkLocator
{
    private static readonly string[] WindowsVendorFolders = ["Java", "Eclipse Adoptium", "Microsoft", "Zulu", "Amazon Corretto", "BellSoft", "Semeru", Path.Combine("SapMachine", "JDK")];

    /// <summary>Finds every JDK: the preferred one first, then the newest first; the same home found twice is listed once.</summary>
    public static IReadOnlyList<JdkInstallation> FindAll(JdkSearchOptions? options = null)
    {
        options ??= new JdkSearchOptions();
        var homes = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.PreferredHome))
            homes.Add(Path.TrimEndingDirectorySeparator(options.PreferredHome));
        if (!string.IsNullOrWhiteSpace(options.JavaHome))
            homes.Add(options.JavaHome);
        homes.AddRange(options.VersionedJavaHomes);
        if (FindJavaOnPath(options.PathFolders ?? PathFolders()) is { } fromPath)
            homes.Add(fromPath);

        foreach (var folder in (options.InstallFolders ?? DefaultInstallFolders()).Concat(options.ExtraInstallFolders))
        {
            if (!Directory.Exists(folder))
                continue;

            try
            {
                foreach (var candidate in Directory.EnumerateDirectories(folder))
                {
                    homes.Add(candidate);
                    homes.Add(Path.Combine(candidate, "Contents", "Home"));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var found = new List<JdkInstallation>();
        foreach (var home in homes)
        {
            if (JdkInstallation.FromHome(home) is { } jdk && seen.Add(jdk.HomePath))
                found.Add(jdk);
        }

        var preferred = string.IsNullOrWhiteSpace(options.PreferredHome) ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.PreferredHome));
        return [.. found.OrderByDescending(j => Path.TrimEndingDirectorySeparator(j.HomePath) == preferred).ThenByDescending(j => j.FeatureVersion)];
    }

    /// <summary>Finds the JDK to read a release's API from: the preferred one when it can serve it, else the newest that can, because a
    /// newer JDK's API data covers older releases; or null when none can.</summary>
    public static JdkInstallation? FindFor(int release, JdkSearchOptions? options = null) =>
        FindAll(options).FirstOrDefault(j => j.CanServe(release));

    private static string[] PathFolders() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

    // java on the PATH is often a link, such as through the system's profile or alternatives; its final target is in the JDK's bin folder.
    private static string? FindJavaOnPath(IReadOnlyList<string> folders)
    {
        var executable = OperatingSystem.IsWindows() ? "java.exe" : "java";
        foreach (var folder in folders)
        {
            var path = Path.Combine(folder, executable);
            if (!File.Exists(path))
                continue;

            try
            {
                var target = File.ResolveLinkTarget(path, returnFinalTarget: true)?.FullName ?? path;
                var bin = Path.GetDirectoryName(target);
                // The bin folder itself can be a link too, such as one package's bin linking to the JDK's inside it.
                if (bin is not null && Directory.ResolveLinkTarget(bin, returnFinalTarget: true) is { } linked)
                    bin = linked.FullName;
                return bin is null ? null : Path.GetDirectoryName(bin);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static List<string> DefaultInstallFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        List<string> folders =
        [
            Path.Combine(home, ".sdkman", "candidates", "java"),
            Path.Combine(home, ".jdks"),
            Path.Combine(home, ".gradle", "jdks"),
            Path.Combine(home, ".asdf", "installs", "java"),
        ];

        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)") })
            {
                if (!string.IsNullOrEmpty(root))
                    folders.AddRange(WindowsVendorFolders.Select(vendor => Path.Combine(root, vendor)));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            folders.Add("/Library/Java/JavaVirtualMachines");
            folders.Add(Path.Combine(home, "Library", "Java", "JavaVirtualMachines"));
        }
        else
        {
            folders.Add("/usr/lib/jvm");
            folders.Add("/usr/lib64/jvm");
            folders.Add("/opt/java");
        }

        return folders;
    }
}
