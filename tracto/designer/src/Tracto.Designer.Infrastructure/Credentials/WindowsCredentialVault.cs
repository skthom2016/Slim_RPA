namespace Tracto.Designer.Infrastructure.Credentials;

using System.Runtime.InteropServices;
using System.Text;
using Tracto.Designer.Core.Credentials;

public sealed class WindowsCredentialVault : ICredentialVault
{
    private const string Prefix = "Tracto.Designer/";

    public void StoreCredential(string name, string value)
    {
        var target = Prefix + name;
        var bytes = Encoding.Unicode.GetBytes(value);
        var credBlob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, credBlob, bytes.Length);
            var cred = new CREDENTIAL
            {
                Flags = 0,
                Type = 1, // CRED_TYPE_GENERIC
                TargetName = target,
                Comment = null,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = credBlob,
                Persist = 2, // CRED_PERSIST_LOCAL_MACHINE
                AttributeCount = 0,
                Attributes = IntPtr.Zero,
                TargetAlias = null,
                UserName = Environment.UserName
            };
            if (!CredWrite(ref cred, 0))
                throw new InvalidOperationException(
                    $"Failed to store credential '{name}'. Error: {Marshal.GetLastWin32Error()}");
        }
        finally
        {
            Marshal.FreeHGlobal(credBlob);
        }
    }

    public string? ResolveCredential(string name)
    {
        var target = Prefix + name;
        if (!CredRead(target, 1, 0, out var credPtr)) return null;
        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0) return null;
            var bytes = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public void RemoveCredential(string name)
    {
        CredDelete(Prefix + name, 1, 0);
    }

    public IReadOnlyList<CredentialEntry> ListCredentials()
    {
        if (!CredEnumerate(Prefix + "*", 0, out var count, out var pCreds)) return [];
        try
        {
            var result = new List<CredentialEntry>();
            for (int i = 0; i < count; i++)
            {
                var credPtr = Marshal.ReadIntPtr(pCreds, i * IntPtr.Size);
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                var rawTarget = cred.TargetName ?? string.Empty;
                var entryName = rawTarget.StartsWith(Prefix, StringComparison.Ordinal)
                    ? rawTarget[Prefix.Length..]
                    : rawTarget;
                result.Add(new CredentialEntry(entryName, "****", DateTimeOffset.UtcNow));
            }
            return result;
        }
        finally
        {
            CredFree(pCreds);
        }
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredEnumerate(string? filter, uint flags, out int count, out IntPtr credentials);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}
