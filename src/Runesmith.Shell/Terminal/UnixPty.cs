using System.Reflection;
using System.Runtime.InteropServices;

namespace Runesmith.Shell.Terminal;

/// <summary>A pseudoterminal on Linux and macOS: a master from <c>posix_openpt</c>, and the program started with <c>posix_spawn</c> in a new
/// session whose controlling terminal is the matching slave.</summary>
/// <remarks>The child runs no managed code: the C library sets up its session, standard streams and folder before it starts the program.</remarks>
internal sealed class UnixPty : PtyProcess
{
    private static readonly Lock Gate = new();
    private readonly int master;
    private readonly int pid;
    private readonly TaskCompletionSource<int> exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int closed;

    private UnixPty(int master, int pid)
    {
        this.master = master;
        this.pid = pid;
        new Thread(WaitForExit) { IsBackground = true, Name = $"pty {pid} wait" }.Start();
    }

    public override int ProcessId => pid;

    public override Task<int> Exited => exited.Task;

    public static new UnixPty Start(PtyStart start)
    {
        var platform = Native.Platform;
        var master = Native.posix_openpt(platform.OpenFlags);
        if (master < 0)
            throw Failure("No pseudoterminal could be opened", Marshal.GetLastPInvokeError());

        try
        {
            if (Native.grantpt(master) != 0 || Native.unlockpt(master) != 0)
                throw Failure("The pseudoterminal could not be set up", Marshal.GetLastPInvokeError());

            string slave;
            lock (Gate)
                slave = Marshal.PtrToStringUTF8(Native.ptsname(master)) ?? throw Failure("The pseudoterminal has no name", Marshal.GetLastPInvokeError());

            var size = new WinSize((ushort)Math.Max(1, start.Rows), (ushort)Math.Max(1, start.Columns));
            Native.SetWindowSize(master, ref size);

            var environment = MergeEnvironment(start.Environment);
            var program = FindProgram(start.Program, environment.GetValueOrDefault("PATH"));
            string[] argv = [program, .. start.Arguments];
            string[] envp = [.. environment.Select(e => $"{e.Key}={e.Value}")];
            var pid = Spawn(program, argv, envp, slave, master, start.WorkingDirectory, platform);
            return new UnixPty(master, pid);
        }
        catch
        {
            _ = Native.close(master);
            throw;
        }
    }

    public override int Read(byte[] buffer)
    {
        var poll = new PollFd { Fd = master, Events = PollFd.In };
        while (Volatile.Read(ref closed) == 0)
        {
            var ready = Native.poll(ref poll, 1, 250);
            if (ready < 0)
            {
                if (Marshal.GetLastPInvokeError() == Native.EINTR)
                    continue;
                return 0;
            }

            if (ready == 0)
                continue;

            var read = Native.read(master, ref MemoryMarshal.GetArrayDataReference(buffer), buffer.Length);
            if (read > 0)
                return (int)read;
            if (read < 0 && Native.IsRetry(Marshal.GetLastPInvokeError()))
                continue;
            return 0;
        }

        return 0;
    }

    public override void Write(ReadOnlySpan<byte> data)
    {
        while (data.Length > 0 && Volatile.Read(ref closed) == 0)
        {
            var written = Native.write(master, ref MemoryMarshal.GetReference(data), data.Length);
            if (written < 0)
            {
                if (Native.IsRetry(Marshal.GetLastPInvokeError()))
                    continue;
                return;
            }

            data = data[(int)written..];
        }
    }

    public override void Resize(int columns, int rows)
    {
        if (Volatile.Read(ref closed) != 0)
            return;

        var size = new WinSize((ushort)Math.Clamp(rows, 1, ushort.MaxValue), (ushort)Math.Clamp(columns, 1, ushort.MaxValue));
        Native.SetWindowSize(master, ref size);
    }

    public override void Kill()
    {
        if (exited.Task.IsCompleted)
            return;

        // The program leads its own session and process group, so the group's id is its pid.
        _ = Native.kill(-pid, Native.SIGHUP);
        _ = Native.kill(pid, Native.SIGHUP);
        _ = Task.Delay(TimeSpan.FromSeconds(2)).ContinueWith(delay =>
        {
            if (!exited.Task.IsCompleted)
            {
                _ = Native.kill(-pid, Native.SIGKILL);
                _ = Native.kill(pid, Native.SIGKILL);
            }
        }, TaskScheduler.Default);
    }

    protected override void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref closed, 1) != 0)
            return;

        Kill();
        _ = Native.close(master);
    }

    private static int Spawn(string program, string[] argv, string[] envp, string slave, int master, string folder, Native.PlatformValues platform)
    {
        var actions = Marshal.AllocHGlobal(1024);
        var attributes = Marshal.AllocHGlobal(1024);
        var signals = Marshal.AllocHGlobal(256);
        var noSignals = Marshal.AllocHGlobal(256);
        var actionsReady = false;
        var attributesReady = false;
        try
        {
            Clear(actions, 1024);
            Clear(attributes, 1024);
            Check(Native.posix_spawn_file_actions_init(actions), "posix_spawn_file_actions_init");
            actionsReady = true;
            Check(Native.posix_spawn_file_actions_addclose(actions, master), "posix_spawn_file_actions_addclose");
            Check(Native.posix_spawn_file_actions_addopen(actions, 0, Utf8(slave), Native.O_RDWR, 0), "posix_spawn_file_actions_addopen");
            Check(Native.posix_spawn_file_actions_adddup2(actions, 0, 1), "posix_spawn_file_actions_adddup2");
            Check(Native.posix_spawn_file_actions_adddup2(actions, 0, 2), "posix_spawn_file_actions_adddup2");
            if (Directory.Exists(folder))
                Check(Native.posix_spawn_file_actions_addchdir_np(actions, Utf8(folder)), "posix_spawn_file_actions_addchdir_np");

            Check(Native.posix_spawnattr_init(attributes), "posix_spawnattr_init");
            attributesReady = true;
            Check(Native.sigfillset(signals), "sigfillset");
            Check(Native.sigemptyset(noSignals), "sigemptyset");
            Check(Native.posix_spawnattr_setsigdefault(attributes, signals), "posix_spawnattr_setsigdefault");
            Check(Native.posix_spawnattr_setsigmask(attributes, noSignals), "posix_spawnattr_setsigmask");
            Check(Native.posix_spawnattr_setflags(attributes, platform.SpawnFlags), "posix_spawnattr_setflags");

            var arguments = Strings(argv);
            var variables = Strings(envp);
            try
            {
                var error = Native.posix_spawn(out var pid, Utf8(program), actions, attributes, arguments, variables);
                if (error != 0)
                    throw Failure($"{program} could not be started", error);
                return pid;
            }
            finally
            {
                Free(arguments);
                Free(variables);
            }
        }
        finally
        {
            if (actionsReady)
                _ = Native.posix_spawn_file_actions_destroy(actions);
            if (attributesReady)
                _ = Native.posix_spawnattr_destroy(attributes);
            Marshal.FreeHGlobal(actions);
            Marshal.FreeHGlobal(attributes);
            Marshal.FreeHGlobal(signals);
            Marshal.FreeHGlobal(noSignals);
        }
    }

    private static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text + "\0");

    // Gets a null-terminated array of UTF-8 strings, as argv and envp are.
    private static IntPtr[] Strings(string[] values) => [.. values.Select(Marshal.StringToCoTaskMemUTF8), IntPtr.Zero];

    private static void Free(IntPtr[] values)
    {
        foreach (var value in values)
            Marshal.FreeCoTaskMem(value);
    }

    private static void Clear(IntPtr memory, int length)
    {
        for (var i = 0; i < length; i += 8)
            Marshal.WriteInt64(memory, i, 0);
    }

    private static void Check(int result, string call)
    {
        if (result != 0)
            throw Failure($"The terminal could not be started: {call} failed", result == -1 ? Marshal.GetLastPInvokeError() : result);
    }

    private static InvalidOperationException Failure(string message, int error) =>
        new($"{message}: {Marshal.GetPInvokeErrorMessage(error)}.");

    private void WaitForExit()
    {
        while (true)
        {
            var result = Native.waitpid(pid, out var status, 0);
            if (result == pid)
            {
                var signal = status & 0x7F;
                exited.TrySetResult(signal == 0 ? (status >> 8) & 0xFF : 128 + signal);
                return;
            }

            if (result < 0 && Marshal.GetLastPInvokeError() == Native.EINTR)
                continue;

            exited.TrySetResult(-1);
            return;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinSize(ushort rows, ushort columns)
    {
        public ushort Rows = rows;
        public ushort Columns = columns;
        public ushort Width;
        public ushort Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public const short In = 1;

        public int Fd;
        public short Events;
        public short Returned;
    }

    private static class Native
    {
        public const int EINTR = 4;
        public const int O_RDWR = 2;
        public const int SIGHUP = 1;
        public const int SIGKILL = 9;
        private const string LibC = "libc";

        private static readonly int EAGAIN = OperatingSystem.IsMacOS() ? 35 : 11;

        static Native() => NativeLibrary.SetDllImportResolver(typeof(Native).Assembly, Resolve);

        public static bool IsRetry(int error) => error == EINTR || error == EAGAIN;

        /// <summary>The values that differ between Linux and macOS.</summary>
        public static PlatformValues Platform { get; } = OperatingSystem.IsMacOS()
            ? new PlatformValues(O_RDWR | 0x20000, 0x04 | 0x08 | 0x400 | 0x4000, 0x80087467)
            : new PlatformValues(O_RDWR | 0x100 | 0x80000, 0x04 | 0x08 | 0x80, 0x5414);

        public static void SetWindowSize(int fd, ref WinSize size)
        {
            // Apple's arm64 calling convention passes variadic arguments on the stack, after the eight argument registers.
            if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
                _ = ioctl_variadic(fd, Platform.SetWindowSize, 0, 0, 0, 0, 0, 0, ref size);
            else
                _ = ioctl(fd, Platform.SetWindowSize, ref size);
        }

        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (name != LibC)
                return IntPtr.Zero;

            return NativeLibrary.Load(OperatingSystem.IsMacOS() ? "/usr/lib/libSystem.B.dylib" : "libc.so.6");
        }

        public readonly record struct PlatformValues(int OpenFlags, short SpawnFlags, nuint SetWindowSize);

        [DllImport(LibC, SetLastError = true)]
        public static extern int posix_openpt(int flags);

        [DllImport(LibC, SetLastError = true)]
        public static extern int grantpt(int fd);

        [DllImport(LibC, SetLastError = true)]
        public static extern int unlockpt(int fd);

        [DllImport(LibC, SetLastError = true)]
        public static extern IntPtr ptsname(int fd);

        [DllImport(LibC, SetLastError = true)]
        public static extern int close(int fd);

        [DllImport(LibC, SetLastError = true)]
        public static extern nint read(int fd, ref byte buffer, nint count);

        [DllImport(LibC, SetLastError = true)]
        public static extern nint write(int fd, ref byte buffer, nint count);

        [DllImport(LibC, SetLastError = true)]
        public static extern int poll(ref PollFd fds, nuint count, int timeout);

        [DllImport(LibC, SetLastError = true)]
        public static extern int kill(int pid, int signal);

        [DllImport(LibC, SetLastError = true)]
        public static extern int waitpid(int pid, out int status, int options);

        [DllImport(LibC, SetLastError = true)]
        private static extern int ioctl(int fd, nuint request, ref WinSize size);

        [DllImport(LibC, EntryPoint = "ioctl", SetLastError = true)]
        private static extern int ioctl_variadic(int fd, nuint request, nint pad1, nint pad2, nint pad3, nint pad4, nint pad5, nint pad6, ref WinSize size);

        [DllImport(LibC, SetLastError = true)]
        public static extern int sigfillset(IntPtr set);

        [DllImport(LibC, SetLastError = true)]
        public static extern int sigemptyset(IntPtr set);

        [DllImport(LibC)]
        public static extern int posix_spawn_file_actions_init(IntPtr actions);

        [DllImport(LibC)]
        public static extern int posix_spawn_file_actions_destroy(IntPtr actions);

        [DllImport(LibC)]
        public static extern int posix_spawn_file_actions_addclose(IntPtr actions, int fd);

        [DllImport(LibC)]
        public static extern int posix_spawn_file_actions_addopen(IntPtr actions, int fd, byte[] path, int flags, uint mode);

        [DllImport(LibC)]
        public static extern int posix_spawn_file_actions_adddup2(IntPtr actions, int fd, int newFd);

        [DllImport(LibC)]
        public static extern int posix_spawn_file_actions_addchdir_np(IntPtr actions, byte[] path);

        [DllImport(LibC)]
        public static extern int posix_spawnattr_init(IntPtr attributes);

        [DllImport(LibC)]
        public static extern int posix_spawnattr_destroy(IntPtr attributes);

        [DllImport(LibC)]
        public static extern int posix_spawnattr_setflags(IntPtr attributes, short flags);

        [DllImport(LibC)]
        public static extern int posix_spawnattr_setsigdefault(IntPtr attributes, IntPtr signals);

        [DllImport(LibC)]
        public static extern int posix_spawnattr_setsigmask(IntPtr attributes, IntPtr signals);

        [DllImport(LibC)]
        public static extern int posix_spawn(out int pid, byte[] path, IntPtr actions, IntPtr attributes, IntPtr[] argv, IntPtr[] envp);
    }
}
