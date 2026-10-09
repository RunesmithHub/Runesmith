using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Runesmith.Shell.Services.Secrets;

/// <summary>Keeps secrets in the Windows Credential Manager as generic credentials named <c>runesmith/key</c>.</summary>
[SupportedOSPlatform("windows")]
internal sealed class CredentialManagerBackend : ISecretBackend
{
    private const int GenericType = 1;
    private const int PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public string Name => "the Windows Credential Manager";

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        if (!NativeMethods.CredRead(Target(key), GenericType, 0, out var handle))
        {
            var error = Marshal.GetLastPInvokeError();
            return error == ErrorNotFound ? Task.FromResult<string?>(null) : throw Failure("read", error);
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.Credential>(handle);
            var bytes = new byte[credential.CredentialBlobSize];
            if (bytes.Length > 0)
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Task.FromResult<string?>(Encoding.Unicode.GetString(bytes));
        }
        finally
        {
            NativeMethods.CredFree(handle);
        }
    }

    public Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        var bytes = Encoding.Unicode.GetBytes(value);
        var blob = Marshal.AllocHGlobal(Math.Max(1, bytes.Length));
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new NativeMethods.Credential
            {
                Type = GenericType,
                TargetName = Target(key),
                UserName = key,
                CredentialBlob = blob,
                CredentialBlobSize = bytes.Length,
                Persist = PersistLocalMachine,
            };
            if (!NativeMethods.CredWrite(ref credential, 0))
                throw Failure("write", Marshal.GetLastPInvokeError());
            return Task.CompletedTask;
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, blob, bytes.Length);
            Marshal.FreeHGlobal(blob);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        if (!NativeMethods.CredDelete(Target(key), GenericType, 0) && Marshal.GetLastPInvokeError() is var error and not ErrorNotFound)
            throw Failure("delete", error);
        return Task.CompletedTask;
    }

    private static string Target(string key) => "runesmith/" + key;

    private static SecretStoreException Failure(string action, int error) =>
        new($"The Credential Manager could not {action} the credential: {Marshal.GetPInvokeErrorMessage(error)}");

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct Credential
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string? Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredWrite(ref Credential credential, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll", SetLastError = false)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern void CredFree(IntPtr buffer);
    }
}
