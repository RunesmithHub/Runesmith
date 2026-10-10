using System.Collections.ObjectModel;
using System.Composition;
using Runesmith.Composition;
using Runesmith.Sdk.Settings;
using Runesmith.Sdk.Shell;
using Runesmith.Sdk.Terminals;
using Runesmith.Sdk.Workspace;
using Runesmith.Shell.Services;
using RunesmithHub.Protocol;

namespace Runesmith.Shell.Terminal;

/// <summary>Starts and keeps the terminals of the Terminal panel: the user's shells, and terminals plugins create, which need the process
/// capability.</summary>
[Export(typeof(ITerminalService))]
[Export]
[Shared]
public sealed class TerminalService : ITerminalService, IDisposable
{
    private readonly IWorkspace workspace;
    private readonly ISettingsService? settings;
    private readonly Func<PluginInfo?> caller;
    private readonly Action<string> log;
    private readonly Lock gate = new();
    private readonly List<TerminalSession> all = [];
    private int shellCount;

    [ImportingConstructor]
    public TerminalService(IWorkspace workspace, ISettingsService settings, IOutputService output)
        : this(workspace, settings, PluginCallers.Current, line => output.GetChannel(PluginAccess.ChannelName).AppendLine(line))
    {
    }

    internal TerminalService(IWorkspace workspace, ISettingsService? settings, Func<PluginInfo?> caller, Action<string> log)
    {
        this.workspace = workspace;
        this.settings = settings;
        this.caller = caller;
        this.log = log;
    }

    /// <summary>Gets the tabs the panel shows, on the UI thread.</summary>
    public ObservableCollection<ITerminal> Tabs { get; } = [];

    public IReadOnlyList<ITerminal> Terminals
    {
        get
        {
            lock (gate)
                return [.. all];
        }
    }

    /// <summary>Raised, on the UI thread, when a terminal is to be shown; its argument says whether it takes the keyboard focus.</summary>
    internal event EventHandler<(TerminalSession Session, bool Focus)>? ShowRequested;

    /// <exception cref="UnauthorizedAccessException">The calling plugin did not declare the process capability.</exception>
    public ITerminal CreateTerminal(string name, TerminalOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        PluginAccess.Demand(caller(), Capabilities.Process, "terminal service", log);
        options ??= new TerminalOptions();
        var environment = TerminalShells.BaseEnvironment();
        string program;
        IReadOnlyList<string> arguments;
        if (options.Command is { Length: > 0 } command)
            (program, arguments) = (command, options.Arguments);
        else
        {
            program = TerminalShells.DefaultShell(Setting(TerminalSettings.Shell, ""));
            arguments = options.Arguments.Count > 0 ? options.Arguments : TerminalShells.Prepare(program, [], integrate: false).Arguments;
        }

        foreach (var (key, value) in options.Environment)
            environment[key] = value;
        var session = Start(name, program, arguments, options.WorkingDirectory, environment, hidden: options.IsHidden, closesOnSuccess: false);
        if (!options.IsHidden)
            Show(session, focus: false);
        return session;
    }

    /// <summary>Starts the user's shell in a new tab and shows it.</summary>
    /// <param name="folder">The folder it starts in, or null for the open folder.</param>
    /// <exception cref="InvalidOperationException">The shell could not be started.</exception>
    internal TerminalSession CreateShell(string? folder = null, bool focus = true)
    {
        var shell = TerminalShells.DefaultShell(Setting(TerminalSettings.Shell, ""));
        var (arguments, variables) = TerminalShells.Prepare(shell, [], Setting(TerminalSettings.ShellIntegration, true));
        var environment = TerminalShells.BaseEnvironment();
        foreach (var (key, value) in variables)
            environment[key] = value;
        var name = Path.GetFileNameWithoutExtension(shell);
        var count = Interlocked.Increment(ref shellCount);
        var session = Start(count == 1 ? name : $"{name} ({count})", shell, arguments, folder, environment, hidden: false, closesOnSuccess: true);
        Show(session, focus);
        return session;
    }

    /// <summary>Starts a program in a new tab, such as a run configuration's, without the capability check plugins get.</summary>
    internal TerminalSession StartProgram(string name, string program, IReadOnlyList<string> arguments, string folder, IReadOnlyDictionary<string, string> variables)
    {
        var environment = TerminalShells.BaseEnvironment();
        foreach (var (key, value) in variables)
            environment[key] = value;
        var session = Start(name, program, arguments, folder, environment, hidden: false, closesOnSuccess: false);
        Show(session, focus: true);
        return session;
    }

    /// <summary>Shows a terminal's tab, adding it to the panel when it was hidden.</summary>
    internal void Show(TerminalSession session, bool focus) => UiThread.Run(() =>
    {
        session.IsHidden = false;
        if (!Tabs.Contains(session) && Terminals.Contains(session))
            Tabs.Add(session);
        ShowRequested?.Invoke(this, (session, focus));
    });

    /// <summary>Ends a terminal's program and removes its tab.</summary>
    internal void Close(TerminalSession session)
    {
        lock (gate)
            all.Remove(session);
        session.Dispose();
        UiThread.Run(() => Tabs.Remove(session));
    }

    public void Dispose()
    {
        foreach (var session in Terminals.OfType<TerminalSession>())
            session.Dispose();
    }

    private TerminalSession Start(string name, string program, IReadOnlyList<string> arguments, string? folder, IReadOnlyDictionary<string, string?> environment,
        bool hidden, bool closesOnSuccess)
    {
        var start = new PtyStart(program, arguments, ResolveFolder(folder), environment, 80, 24);
        var session = new TerminalSession(name, start, Math.Max(0, Setting(TerminalSettings.Scrollback, 10_000)), Show, Close)
        {
            IsHidden = hidden,
            ClosesOnSuccess = closesOnSuccess,
        };
        lock (gate)
            all.Add(session);
        session.Exited += (_, e) =>
        {
            if (session.ClosesOnSuccess && e.ExitCode == 0)
                Close(session);
        };
        return session;
    }

    private string ResolveFolder(string? folder)
    {
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            return folder;
        if (workspace.RootPath is { } root && Directory.Exists(root))
            return root;
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    private T Setting<T>(string key, T fallback)
    {
        try
        {
            return settings is null ? fallback : settings.Get<T>(key);
        }
        catch (Exception exception) when (exception is KeyNotFoundException or InvalidCastException)
        {
            return fallback;
        }
    }
}
