using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Luma.Services;

// Internal-only broker: UI models cannot obtain raw credential blobs.
internal sealed class CredentialManagerStore(string profileNamespace)
{
    private const uint Generic = 1, LocalMachine = 2;
    private const int NotFound = 1168, MaxBlobBytes = 2560;
    private string Target(Guid id, Guid version) => $"Luma/Vault/{profileNamespace}/{id:N}/{version:N}";

    internal void Write(Guid id, Guid version, string password)
    {
        var bytes = Encoding.Unicode.GetBytes(password);
        try
        {
            if (bytes.Length is < 2 or > MaxBlobBytes) throw new ArgumentException("Login secret length is invalid.");
            using var blob = new SecretBuffer(bytes.Length);
            Marshal.Copy(bytes, 0, blob.DangerousGetHandle(), bytes.Length);
            var credential = new Credential
            {
                Type = Generic, TargetName = Target(id, version), Persist = LocalMachine,
                CredentialBlobSize = (uint)bytes.Length, CredentialBlob = blob.DangerousGetHandle(),
                UserName = "Luma" // no username/PII in Credential Manager metadata
            };
            if (!CredWriteW(ref credential, 0)) throw NativeError();
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    internal string? Read(Guid id, Guid version)
    {
        if (!CredReadW(Target(id, version), Generic, 0, out var handle))
        {
            var error = Marshal.GetLastWin32Error();
            handle?.Dispose();
            if (error == NotFound) return null;
            throw new Win32Exception(error, "Credential Manager operation failed.");
        }
        using (handle)
        {
            var credential = Marshal.PtrToStructure<Credential>(handle.DangerousGetHandle());
            if (credential.Type != Generic || credential.CredentialBlobSize is < 2 or > MaxBlobBytes ||
                credential.CredentialBlobSize % 2 != 0 || credential.CredentialBlob == IntPtr.Zero)
                throw new InvalidDataException("Credential Manager record is invalid.");
            var bytes = new byte[(int)credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                for (var i = 0; i < credential.CredentialBlobSize; i++) Marshal.WriteByte(credential.CredentialBlob, i, 0);
            }
        }
    }

    internal void Delete(Guid id, Guid version)
    {
        if (!CredDeleteW(Target(id, version), Generic, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != NotFound) throw new Win32Exception(error, "Credential Manager operation failed.");
        }
    }
    private static Win32Exception NativeError() => new(Marshal.GetLastWin32Error(), "Credential Manager operation failed.");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags, Type;
        public string? TargetName, Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist, AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias, UserName;
    }
    private sealed class CredentialHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public CredentialHandle() : base(true) { }
        protected override bool ReleaseHandle() { CredFree(handle); return true; }
    }
    private sealed class SecretBuffer : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly int _length;
        public SecretBuffer(int length) : base(true) { _length = length; SetHandle(Marshal.AllocHGlobal(length)); }
        protected override bool ReleaseHandle()
        {
            for (var i = 0; i < _length; i++) Marshal.WriteByte(handle, i, 0);
            Marshal.FreeHGlobal(handle);
            return true;
        }
    }
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, uint type, uint flags, out CredentialHandle credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll", ExactSpelling = true)]
    private static extern void CredFree(IntPtr buffer);
}
