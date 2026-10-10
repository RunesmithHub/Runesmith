using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Runesmith.Shell.Terminal;

/// <summary>A pseudo console on Windows 10 1809 and later: the program is attached to a ConPTY, which turns its console into the escape
/// sequences the terminal reads, through two pipes.</summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsPty : PtyProcess
{
    private readonly IntPtr console;
    private readonly SafeWaitHandle process;
    private readonly FileStream input;
    private readonly FileStream output;
    private readonly int processId;
    private readonly TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly RegisteredWaitHandle wait;
    private readonly ManualResetEvent processEnded;
    private int closed;

    private WindowsPty(IntPtr console, SafeWaitHandle process, int processId, SafeFileHandle input, SafeFileHandle output)
    {
        this.console = console;
        this.process = process;
        this.processId = processId;
        this.input = new FileStream(input, FileAccess.Write, 1);
        this.output = new FileStream(output, FileAccess.Read, 1);
        processEnded = new ManualResetEvent(false) { SafeWaitHandle = process };
        wait = ThreadPool.RegisterWaitForSingleObject(processEnded, (_, _) => OnExited(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    public override int ProcessId => processId;

    public override Task<int> Exited => exited.Task;

    public static new WindowsPty Start(PtyStart start)
    {
        if (!Native.CreatePipe(out var consoleInput, out var input, IntPtr.Zero, 0) || !Native.CreatePipe(out var output, out var consoleOutput, IntPtr.Zero, 0))
            throw new InvalidOperationException($"The terminal's pipes could not be made: {new Win32Exception().Message}");

        var size = new Coord((short)Math.Clamp(start.Columns, 1, short.MaxValue), (short)Math.Clamp(start.Rows, 1, short.MaxValue));
        var result = Native.CreatePseudoConsole(size, consoleInput, consoleOutput, 0, out var console);
        consoleInput.Dispose();
        consoleOutput.Dispose();
        if (result != 0)
        {
            input.Dispose();
            output.Dispose();
            throw new InvalidOperationException($"The pseudo console could not be made: {new Win32Exception(result).Message}");
        }

        try
        {
            var (process, id) = Spawn(start, console);
            return new WindowsPty(console, process, id, input, output);
        }
        catch
        {
            Native.ClosePseudoConsole(console);
            input.Dispose();
            output.Dispose();
            throw;
        }
    }

    public override int Read(byte[] buffer)
    {
        try
        {
            return output.Read(buffer, 0, buffer.Length);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            return 0;
        }
    }

    public override void Write(ReadOnlySpan<byte> data)
    {
        try
        {
            input.Write(data);
            input.Flush();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
        }
    }

    public override void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref closed) == 0)
            _ = Native.ResizePseudoConsole(console, new Coord((short)Math.Clamp(columns, 1, short.MaxValue), (short)Math.Clamp(rows, 1, short.MaxValue)));
    }

    public override void Kill()
    {
        if (!exited.Task.IsCompleted && !process.IsClosed)
            _ = Native.TerminateProcess(process, 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref closed, 1) != 0)
            return;

        Kill();
        Native.ClosePseudoConsole(console);
        wait.Unregister(null);
        input.Dispose();
        output.Dispose();
        processEnded.Dispose();
    }

    private void OnExited()
    {
        var code = Native.GetExitCodeProcess(process, out var exitCode) ? unchecked((int)exitCode) : -1;
        exited.TrySetResult(code);
    }

    private static (SafeWaitHandle Process, int Id) Spawn(PtyStart start, IntPtr console)
    {
        var environment = MergeEnvironment(start.Environment);
        var program = FindProgram(start.Program, environment.GetValueOrDefault("PATH"));
        var commandLine = new StringBuilder(Quote(program));
        foreach (var argument in start.Arguments)
            commandLine.Append(' ').Append(Quote(argument));
        var line = (commandLine + "\0").ToCharArray();

        var block = new StringBuilder();
        foreach (var (name, value) in environment)
            block.Append(name).Append('=').Append(value).Append('\0');
        block.Append('\0');
        var environmentBlock = Marshal.StringToHGlobalUni(block.ToString());

        var listSize = IntPtr.Zero;
        _ = Native.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
        var list = Marshal.AllocHGlobal(listSize);
        var listReady = false;
        try
        {
            if (!Native.InitializeProcThreadAttributeList(list, 1, 0, ref listSize))
                throw new InvalidOperationException($"The terminal could not be started: {new Win32Exception().Message}");
            listReady = true;
            if (!Native.UpdateProcThreadAttribute(list, 0, Native.PseudoConsoleAttribute, console, IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new InvalidOperationException($"The terminal could not be started: {new Win32Exception().Message}");

            var info = new StartupInfoEx { AttributeList = list };
            info.StartupInfo.Size = Marshal.SizeOf<StartupInfoEx>();
            var folder = Directory.Exists(start.WorkingDirectory) ? start.WorkingDirectory : null;
            if (!Native.CreateProcessW(null, line, IntPtr.Zero, IntPtr.Zero, false, Native.ExtendedStartupInfoPresent | Native.UnicodeEnvironment,
                    environmentBlock, folder, ref info, out var started))
                throw new InvalidOperationException($"{program} could not be started: {new Win32Exception().Message}");

            _ = Native.CloseHandle(started.Thread);
            return (new SafeWaitHandle(started.Process, ownsHandle: true), started.ProcessId);
        }
        finally
        {
            if (listReady)
                Native.DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
            Marshal.FreeHGlobal(environmentBlock);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Coord(short x, short y)
    {
        public readonly short X = x;
        public readonly short Y = y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Size;
        public IntPtr Reserved2;
        public IntPtr StdInput;
        public IntPtr StdOutput;
        public IntPtr StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public int ProcessId;
        public int ThreadId;
    }

    private static class Native
    {
        public const uint ExtendedStartupInfoPresent = 0x00080000;
        public const uint UnicodeEnvironment = 0x00000400;
        public static readonly IntPtr PseudoConsoleAttribute = 0x00020016;

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, IntPtr attributes, int size);

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int ResizePseudoConsole(IntPtr console, Coord size);

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern void ClosePseudoConsole(IntPtr console);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);

        [DllImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern void DeleteProcThreadAttributeList(IntPtr list);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool CreateProcessW(string? application, [In, Out] char[] commandLine, IntPtr processAttributes, IntPtr threadAttributes,
            bool inheritHandles, uint flags, IntPtr environment, string? folder, ref StartupInfoEx startup, out ProcessInformation information);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool TerminateProcess(SafeWaitHandle process, uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool GetExitCodeProcess(SafeWaitHandle process, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
