namespace Runesmith.Sdk.Terminals;

/// <summary>How a terminal starts: the program it runs, where and with which environment, and whether the user sees it at once.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
public sealed record TerminalOptions
{
    /// <summary>Gets the program to run, found on the PATH unless it is a full path, or null for the user's shell.</summary>
    public string? Command { get; init; }

    /// <summary>Gets the arguments of <see cref="Command"/>, or of the shell when there is no command, unquoted.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Gets the folder the program starts in, or null for the open folder, or the user's home folder when none is open.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Gets environment variables to set over Runesmith's own environment; a null value removes a variable.</summary>
    public IReadOnlyDictionary<string, string?> Environment { get; init; } = new Dictionary<string, string?>();

    /// <summary>Gets whether the terminal stays out of the Terminal panel until <see cref="ITerminal.Show"/> is called; its program runs
    /// meanwhile.</summary>
    public bool IsHidden { get; init; }
}

/// <summary>The text a terminal's program wrote.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="Text">The text, with the program's escape sequences, such as those for colors, as it wrote them.</param>
public sealed class TerminalDataEventArgs(string Text) : EventArgs
{
    public string Text { get; } = Text;
}

/// <summary>How a terminal's program ended.</summary>
/// <remarks>Added in plugin API 0.1.2.</remarks>
/// <param name="ExitCode">The program's exit code, or null when it is unknown.</param>
public sealed class TerminalExitedEventArgs(int? ExitCode) : EventArgs
{
    public int? ExitCode { get; } = ExitCode;
}

/// <summary>A tab of the Terminal panel and the program that runs in it.</summary>
/// <remarks>Added in plugin API 0.1.2. Its members can be called from any thread, and its events are raised on a background thread.</remarks>
public interface ITerminal
{
    /// <summary>Gets the tab's name.</summary>
    string Name { get; }

    /// <summary>Gets the program's process id, or null when it did not start.</summary>
    int? ProcessId { get; }

    /// <summary>Gets whether the program ended.</summary>
    bool HasExited { get; }

    /// <summary>Gets the program's exit code once it ended, or null.</summary>
    int? ExitCode { get; }

    /// <summary>Types text into the terminal, as if the user typed it; with <paramref name="addNewLine"/>, presses Enter after it.</summary>
    void SendText(string text, bool addNewLine = true);

    /// <summary>Shows the terminal's tab in the Terminal panel and shows the panel; with <paramref name="takeFocus"/>, moves the keyboard
    /// focus to it.</summary>
    void Show(bool takeFocus = true);

    /// <summary>Ends the program and closes the tab.</summary>
    void Close();

    /// <summary>Raised when the program writes text; the first handler added also gets what the program wrote before it was added, up to
    /// 64 KB.</summary>
    event EventHandler<TerminalDataEventArgs>? Data;

    /// <summary>Raised once, when the program ends.</summary>
    event EventHandler<TerminalExitedEventArgs>? Exited;
}

/// <summary>Creates terminals in the Terminal panel.</summary>
/// <remarks>Added in plugin API 0.1.2. A terminal starts a program, so a plugin needs the <c>process</c> capability in its manifest to create
/// one.</remarks>
public interface ITerminalService
{
    /// <summary>Gets the terminals that are open, including hidden ones.</summary>
    IReadOnlyList<ITerminal> Terminals { get; }

    /// <summary>Starts a program, or the user's shell, in a new terminal named <paramref name="name"/>, and shows it unless
    /// <see cref="TerminalOptions.IsHidden"/> is set.</summary>
    /// <exception cref="UnauthorizedAccessException">The calling plugin did not declare the process capability.</exception>
    /// <exception cref="InvalidOperationException">The program could not be started; the message says why.</exception>
    ITerminal CreateTerminal(string name, TerminalOptions? options = null);
}
