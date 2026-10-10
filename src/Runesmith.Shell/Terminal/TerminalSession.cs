using System.Globalization;
using System.Text;
using Runesmith.Sdk.Terminals;

namespace Runesmith.Shell.Terminal;

/// <summary>A tab of the Terminal panel: a program in a pseudoterminal and the screen its output draws.</summary>
/// <remarks>Its members can be called from any thread. The screen is read and changed only while holding <see cref="Gate"/>; the events are
/// raised on the thread that reads the program's output.</remarks>
internal sealed class TerminalSession : ITerminal, IDisposable
{
    private readonly PtyProcess pty;
    private readonly Action<TerminalSession, bool> show;
    private readonly Action<TerminalSession> close;
    private readonly Decoder dataDecoder = new UTF8Encoding(false).GetDecoder();
    private readonly Lock writeGate = new();
    private int disposed;
    private bool bellRung;
    private readonly Lock dataGate = new();
    private EventHandler<TerminalDataEventArgs>? data;
    private StringBuilder? pending = new();

    /// <summary>Starts the program and begins reading its output.</summary>
    /// <exception cref="InvalidOperationException">The program could not be started.</exception>
    public TerminalSession(string name, PtyStart start, int scrollbackLines, Action<TerminalSession, bool> show, Action<TerminalSession> close)
    {
        Name = name;
        this.show = show;
        this.close = close;
        Screen = new TerminalScreen(start.Columns, start.Rows, scrollbackLines) { Reply = reply => Write(reply), Bell = () => bellRung = true };
        WorkingDirectory = start.WorkingDirectory;
        pty = PtyProcess.Start(start);
        ProcessId = pty.ProcessId;
        new Thread(ReadOutput) { IsBackground = true, Name = $"terminal {name}" }.Start();
    }

    public string Name { get; }

    public int? ProcessId { get; }

    public bool HasExited { get; private set; }

    public int? ExitCode { get; private set; }

    /// <summary>Gets the screen the program draws; hold <see cref="Gate"/> while using it.</summary>
    public TerminalScreen Screen { get; }

    /// <summary>Gets the lock that guards <see cref="Screen"/>.</summary>
    public Lock Gate { get; } = new();

    /// <summary>Gets whether the tab is kept out of the panel until it is shown.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Gets whether the tab closes by itself when its program ends with exit code 0, as a shell that the user exits does.</summary>
    public bool ClosesOnSuccess { get; init; }

    /// <summary>Gets the folder the program started in.</summary>
    public string WorkingDirectory { get; }

    /// <summary>Gets the folder the shell is in now, when its shell integration says so, or the folder it started in.</summary>
    public string CurrentDirectory
    {
        get
        {
            lock (Gate)
                return Screen.WorkingDirectory ?? WorkingDirectory;
        }
    }

    /// <summary>Gets whether the tab shows the title the program sets, as the user's shells do, rather than its name.</summary>
    public bool ShowsProgramTitle { get; init; }

    /// <summary>Gets the title the tab shows: the program's own title when it shows one, or the name.</summary>
    public string Title
    {
        get
        {
            lock (Gate)
                return ShowsProgramTitle && Screen.Title is { Length: > 0 } title ? title : Name;
        }
    }

    /// <summary>Raised after the screen changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised when the program rings the bell.</summary>
    public event EventHandler? Bell;

    /// <summary>Raised with the text the program writes; the first handler added also gets what it wrote before, up to 64 KB.</summary>
    public event EventHandler<TerminalDataEventArgs>? Data
    {
        add
        {
            string? early;
            lock (dataGate)
            {
                data += value;
                early = pending?.ToString();
                pending = null;
            }

            if (!string.IsNullOrEmpty(early))
                value?.Invoke(this, new TerminalDataEventArgs(early));
        }

        remove
        {
            lock (dataGate)
                data -= value;
        }
    }

    public event EventHandler<TerminalExitedEventArgs>? Exited;

    /// <summary>Writes text to the program's input.</summary>
    public void Write(string text)
    {
        if (text.Length == 0 || HasExited)
            return;

        var bytes = Encoding.UTF8.GetBytes(text);
        lock (writeGate)
            pty.Write(bytes);
    }

    /// <summary>Pastes text: line breaks become Enter, and a program that asked for bracketed paste gets it marked as pasted.</summary>
    public void Paste(string text)
    {
        bool bracketed;
        lock (Gate)
            bracketed = Screen.BracketedPaste;
        var normalized = text.ReplaceLineEndings("\r");
        Write(bracketed ? $"\u001b[200~{normalized.Replace("\u001b[201~", "", StringComparison.Ordinal)}\u001b[201~" : normalized);
    }

    public void SendText(string text, bool addNewLine = true)
    {
        ArgumentNullException.ThrowIfNull(text);
        Write(addNewLine ? text + "\r" : text);
    }

    /// <summary>Changes the size of the screen and tells the program.</summary>
    public void Resize(int columns, int rows)
    {
        lock (Gate)
        {
            if (Screen.Columns == columns && Screen.Rows == rows)
                return;
            Screen.Resize(columns, rows);
        }

        pty.Resize(columns, rows);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Show(bool takeFocus = true) => show(this, takeFocus);

    public void Close() => close(this);

    /// <summary>Ends the program, leaving the tab.</summary>
    public void Kill() => pty.Kill();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
            pty.Dispose();
    }

    private void RaiseData(string text)
    {
        EventHandler<TerminalDataEventArgs>? handler;
        lock (dataGate)
        {
            handler = data;
            if (handler is null && pending is not null)
            {
                pending.Append(text);
                if (pending.Length > 65536)
                    pending.Remove(0, pending.Length - 65536);
            }
        }

        handler?.Invoke(this, new TerminalDataEventArgs(text));
    }

    private void ReadOutput()
    {
        var buffer = new byte[16384];
        var chars = new char[buffer.Length + 4];
        int read;
        while ((read = pty.Read(buffer)) > 0)
        {
            bellRung = false;
            lock (Gate)
                Screen.Feed(buffer.AsSpan(0, read));

            Changed?.Invoke(this, EventArgs.Empty);
            if (bellRung)
                Bell?.Invoke(this, EventArgs.Empty);
            var count = dataDecoder.GetChars(buffer, 0, read, chars, 0);
            if (count > 0)
                RaiseData(new string(chars, 0, count));
        }

        int? code = null;
        try
        {
            code = pty.Exited.Wait(TimeSpan.FromSeconds(5)) ? pty.Exited.Result : null;
        }
        catch (AggregateException)
        {
        }

        ExitCode = code;
        HasExited = true;
        if (Volatile.Read(ref disposed) == 0)
        {
            var message = code is { } exit ? string.Create(CultureInfo.CurrentCulture, $"Process exited with code {exit}.") : "Process exited.";
            lock (Gate)
                Screen.Feed($"\r\n\u001b[0;2m{message}\u001b[0m\r\n");
            Changed?.Invoke(this, EventArgs.Empty);
        }

        Exited?.Invoke(this, new TerminalExitedEventArgs(code));
    }
}
