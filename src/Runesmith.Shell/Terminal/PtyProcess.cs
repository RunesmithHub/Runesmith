namespace Runesmith.Shell.Terminal;

/// <summary>What to start in a pseudoterminal.</summary>
/// <param name="Program">The executable, found on the PATH unless it is a full path.</param>
/// <param name="Arguments">Its arguments, unquoted.</param>
/// <param name="WorkingDirectory">The folder it starts in.</param>
/// <param name="Environment">Variables to set over Runesmith's own environment; a null value removes one.</param>
/// <param name="Columns">The terminal's width, in cells.</param>
/// <param name="Rows">The terminal's height, in cells.</param>
internal sealed record PtyStart(string Program, IReadOnlyList<string> Arguments, string WorkingDirectory, IReadOnlyDictionary<string, string?> Environment,
    int Columns, int Rows);

/// <summary>A program running in a pseudoterminal: a real terminal device on Linux and macOS, a pseudo console on Windows, so the program
/// sees a terminal, with its size, line editing and signals such as Ctrl+C.</summary>
internal abstract class PtyProcess : IDisposable
{
    /// <summary>Gets the process id.</summary>
    public abstract int ProcessId { get; }

    /// <summary>Gets a task that ends with the exit code once the program has ended; a program ended by a signal gets 128 plus the
    /// signal's number.</summary>
    public abstract Task<int> Exited { get; }

    /// <summary>Starts a program in a new pseudoterminal.</summary>
    /// <exception cref="InvalidOperationException">The program could not be started; the message says why.</exception>
    public static PtyProcess Start(PtyStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        return OperatingSystem.IsWindows() ? WindowsPty.Start(start) : UnixPty.Start(start);
    }

    /// <summary>Reads what the program wrote, waiting until there is some; returns 0 once the program and every process sharing its
    /// terminal closed it, or the pseudoterminal was disposed.</summary>
    public abstract int Read(byte[] buffer);

    /// <summary>Writes to the program's input, as typed keys.</summary>
    public abstract void Write(ReadOnlySpan<byte> data);

    /// <summary>Changes the terminal's size; the program is told with SIGWINCH, or its console's resize event on Windows.</summary>
    public abstract void Resize(int columns, int rows);

    /// <summary>Ends the program and the processes it started in its terminal: politely first, then by force.</summary>
    public abstract void Kill();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected abstract void Dispose(bool disposing);

    /// <summary>Finds a program on the PATH the way a shell does, or returns it as it is when it has a folder or nothing matches.</summary>
    internal static string FindProgram(string program, string? path)
    {
        if (program.Contains('/', StringComparison.Ordinal) || (OperatingSystem.IsWindows() && program.Contains('\\', StringComparison.Ordinal)) || string.IsNullOrEmpty(path))
            return program;

        string[] extensions = OperatingSystem.IsWindows() && !Path.HasExtension(program) ? [".exe", ".cmd", ".bat", ".com"] : [""];
        foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(folder, program + extension);
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        return program;
    }

    /// <summary>Quotes an argument for a Windows command line, the way the C runtime reads command lines back into arguments.</summary>
    internal static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '\n', '\v', '"']) < 0)
            return argument;

        var quoted = new System.Text.StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            quoted.Append('\\', c == '"' ? (backslashes * 2) + 1 : backslashes);
            backslashes = 0;
            quoted.Append(c);
        }

        quoted.Append('\\', backslashes * 2).Append('"');
        return quoted.ToString();
    }

    /// <summary>Gets Runesmith's environment with the start's variables over it.</summary>
    internal static SortedDictionary<string, string> MergeEnvironment(IReadOnlyDictionary<string, string?> overrides)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var environment = new SortedDictionary<string, string>(comparer);
        foreach (System.Collections.DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
                environment[name] = value;
        }

        foreach (var (name, value) in overrides)
        {
            if (value is null)
                environment.Remove(name);
            else
                environment[name] = value;
        }

        return environment;
    }
}
