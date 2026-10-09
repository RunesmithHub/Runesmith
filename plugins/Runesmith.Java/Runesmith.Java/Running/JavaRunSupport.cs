using System.Diagnostics;
using Runesmith.Java.Sdks;
using Runesmith.Sdk.Options;
using Runesmith.Sdk.Running;
using Runesmith.Sdk.Sdks;

namespace Runesmith.Java.Running;

/// <summary>The options and launch pieces the Java configuration types share: the JDK, environment and working directory.</summary>
internal static class JavaRunSupport
{
    public const string Jdk = "jdk";
    public const string Arguments = "arguments";
    public const string Environment = "environment";
    public const string WorkingDirectory = "workingDirectory";

    /// <summary>The JVM option that makes a program wait for a debugger, which prints the port it listens on.</summary>
    public const string JdwpAgent = "-agentlib:jdwp=transport=dt_socket,server=y,suspend=y,address=127.0.0.1:0";

    public static Option JdkOption { get; } = new(Jdk, "JDK", OptionKind.Sdk)
    {
        SdkKind = JdkSdkProvider.SdkKind,
        Description = "The JDK that runs the program; the default JDK when none is chosen.",
    };

    public static Option ArgumentsOption(string label, string description) => new(Arguments, label, OptionKind.List)
    {
        Description = description,
        Placeholder = "Argument",
    };

    public static Option EnvironmentOption { get; } = new(Environment, "Environment variables", OptionKind.List)
    {
        IsKeyValueList = true,
        Group = OptionGroup.Advanced,
    };

    public static Option WorkingDirectoryOption { get; } = new(WorkingDirectory, "Working directory", OptionKind.Path)
    {
        PathKind = PathKind.Folder,
        Group = OptionGroup.Advanced,
        Placeholder = "The project's folder",
    };

    /// <summary>Finds the JDK a configuration asks for: its own, the user's default, <c>JAVA_HOME</c>, or none, which means the <c>java</c>
    /// on the <c>PATH</c>.</summary>
    public static async Task<string?> FindJavaHomeAsync(OptionValues values, ISdkService? sdks, CancellationToken cancellationToken)
    {
        if (values.Get(Jdk) is { Length: > 0 } chosen)
            return chosen;
        if (sdks is not null && await sdks.GetDefaultAsync(JdkSdkProvider.SdkKind, cancellationToken).ConfigureAwait(false) is { } sdk)
            return sdk.Path;
        return System.Environment.GetEnvironmentVariable("JAVA_HOME") is { Length: > 0 } home && Directory.Exists(home) ? home : null;
    }

    /// <summary>Gets a JDK tool, such as <c>java</c> or <c>javac</c>, from a JDK, or its bare name to find on the <c>PATH</c>.</summary>
    public static string Tool(string? javaHome, string name)
    {
        var file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        return javaHome is null ? name : Path.Combine(javaHome, "bin", file);
    }

    /// <summary>Gets the environment of a launch: the configuration's variables and <c>JAVA_HOME</c> when a JDK is known.</summary>
    public static Dictionary<string, string> LaunchEnvironment(OptionValues values, string? javaHome)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        if (javaHome is not null)
            environment["JAVA_HOME"] = javaHome;
        foreach (var (key, value) in values.GetPairs(Environment))
        {
            if (key.Length > 0)
                environment[key] = value;
        }

        return environment;
    }

    /// <summary>Gets the folder a program starts in: the configuration's own, relative to the open folder, or the fallback.</summary>
    public static string ResolveWorkingDirectory(OptionValues values, string rootPath, string fallback) =>
        values.Get(WorkingDirectory) is { Length: > 0 } folder ? Path.GetFullPath(Path.Combine(rootPath, folder)) : fallback;

    /// <summary>Splits a text field such as Maven goals into its words, keeping quoted parts together.</summary>
    public static IReadOnlyList<string> Words(string? text)
    {
        var words = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return words;

        var current = new System.Text.StringBuilder();
        char? quote = null;
        foreach (var c in text)
        {
            if (quote is { } open)
            {
                if (c == open)
                    quote = null;
                else
                    current.Append(c);
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            words.Add(current.ToString());
        return words;
    }

    /// <summary>Turns a build tool's command into a launch plan for a configuration.</summary>
    public static async Task<LaunchPlan> ToolPlanAsync(JavaBuildProvider.BuildTool tool, RunConfiguration configuration, RunContext context,
        ISdkService? sdks, CancellationToken cancellationToken)
    {
        var javaHome = await FindJavaHomeAsync(configuration.Values, sdks, cancellationToken).ConfigureAwait(false);
        return new LaunchPlan(tool.Executable, tool.Arguments, ResolveWorkingDirectory(configuration.Values, context.RootPath, context.RootPath))
        {
            Environment = LaunchEnvironment(configuration.Values, javaHome),
        };
    }

    /// <summary>Runs a program to its end and returns its exit code, passing each line it writes to a callback.</summary>
    public static async Task<int> RunAsync(ProcessStartInfo start, Action<string> line, CancellationToken cancellationToken)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        using var process = new Process { StartInfo = start };
        process.Start();
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It had exited already.
            }
        });
        await Task.WhenAll(ReadAsync(process.StandardOutput, line), ReadAsync(process.StandardError, line)).ConfigureAwait(false);
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static async Task ReadAsync(StreamReader reader, Action<string> line)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } text)
            line(text);
    }
}
