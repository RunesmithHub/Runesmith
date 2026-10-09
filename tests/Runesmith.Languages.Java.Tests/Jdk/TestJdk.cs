using Runesmith.Languages.Java.Jdk;

namespace Runesmith.Languages.Java.Tests.Jdk;

/// <summary>The JDK the tests read real class files from: the newest one found through <c>JAVA_HOME</c>, the <c>PATH</c> or the usual folders.</summary>
internal static class TestJdk
{
    private static readonly Lazy<JdkInstallation?> Newest = new(() => JdkLocator.FindAll() is [var newest, ..] ? newest : null);

    /// <summary>Gets the JDK, or skips the test when there is none.</summary>
    public static JdkInstallation Require(int minimumVersion = 25)
    {
        var jdk = Newest.Value;
        if (jdk is null || jdk.FeatureVersion < minimumVersion)
            Assert.Skip($"This test reads a JDK {minimumVersion} or newer; set JAVA_HOME to one.");
        return jdk!;
    }

    private static readonly Dictionary<int, JdkApi> Apis = [];

    /// <summary>Gets a release's API from the JDK, opened once per test run.</summary>
    public static JdkApi Api(int release)
    {
        var jdk = Require();
        lock (Apis)
        {
            if (!Apis.TryGetValue(release, out var api))
                Apis[release] = api = JdkApi.Open(jdk, release);
            return api;
        }
    }

    /// <summary>Compiles Java sources with the JDK's <c>javac</c> into a temporary folder and returns it, or skips the test.</summary>
    public static string Compile(IReadOnlyDictionary<string, string> sources, params string[] options)
    {
        var jdk = Require();
        var javac = Path.Combine(jdk.HomePath, "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac");
        if (!File.Exists(javac))
            Assert.Skip("The JDK has no javac.");

        var folder = Directory.CreateTempSubdirectory("runesmith-javac-").FullName;
        var files = new List<string>();
        foreach (var (name, text) in sources)
        {
            var path = Path.Combine(folder, "src", name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            files.Add(path);
        }

        var start = new System.Diagnostics.ProcessStartInfo(javac) { RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var option in options.Concat(["-d", Path.Combine(folder, "classes")]).Concat(files))
            start.ArgumentList.Add(option);

        using var process = System.Diagnostics.Process.Start(start)!;
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, errors);
        return Path.Combine(folder, "classes");
    }
}
