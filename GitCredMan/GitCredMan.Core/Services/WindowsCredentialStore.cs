using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using GitCredMan.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace GitCredMan.Core.Services;

/// <summary>
/// Stores and retrieves tokens via Windows Credential Manager (advapi32).
/// Blobs are encrypted at rest by DPAPI, scoped to the current user.
///
/// Credential target key:  "GitCredMan:{accountId}"
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStore : ICredentialStore
{
    private readonly ILogger<WindowsCredentialStore> _log;

    public WindowsCredentialStore(ILogger<WindowsCredentialStore> log) => _log = log;

    // ── Win32 structures ──────────────────────────────────────

    private const uint CRED_TYPE_GENERIC          = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint     Flags;
        public uint     Type;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string   TargetName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string?  Comment;
        public long     LastWritten;        // FILETIME
        public uint     CredentialBlobSize;
        public IntPtr   CredentialBlob;
        public uint     Persist;
        public uint     AttributeCount;
        public IntPtr   Attributes;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string?  TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string?  UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWriteW(ref CREDENTIAL cred, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credPtr);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr buffer);

    // ── Public API ────────────────────────────────────────────

    private static string Key(string id) => $"GitCredMan:{id}";

    /// <inheritdoc/>
    public bool Save(string accountId, string token)
    {
        if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(token))
            return false;

        byte[] blob    = Encoding.Unicode.GetBytes(token);
        IntPtr blobPtr = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobPtr, blob.Length);

            var cred = new CREDENTIAL
            {
                Type               = CRED_TYPE_GENERIC,
                TargetName         = Key(accountId),
                Comment            = "Git Credential Manager — PAT",
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob     = blobPtr,
                Persist            = CRED_PERSIST_LOCAL_MACHINE,
                UserName           = accountId,
            };

            bool ok = CredWriteW(ref cred, 0);
            if (!ok) _log.LogWarning("CredWriteW failed: {err}", Marshal.GetLastWin32Error());
            return ok;
        }
        finally
        {
            // Securely zero before freeing unmanaged memory
            unsafe { new Span<byte>((void*)blobPtr, blob.Length).Clear(); }
            Marshal.FreeHGlobal(blobPtr);
            Array.Clear(blob);
        }
    }

    /// <inheritdoc/>
    public string? Load(string accountId)
    {
        if (!CredReadW(Key(accountId), CRED_TYPE_GENERIC, 0, out IntPtr ptr))
            return null;

        try
        {
            var cred = Marshal.PtrToStructure<CREDENTIAL>(ptr);
            if (cred.CredentialBlobSize == 0 || cred.CredentialBlob == IntPtr.Zero)
                return null;

            byte[] blob = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, blob, 0, blob.Length);
            string token = Encoding.Unicode.GetString(blob);
            Array.Clear(blob);   // zero before GC collects
            return token;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to read credential for {id}", accountId);
            return null;
        }
        finally
        {
            CredFree(ptr);
        }
    }

    /// <inheritdoc/>
    public bool Delete(string accountId)
    {
        bool ok = CredDeleteW(Key(accountId), CRED_TYPE_GENERIC, 0);
        if (!ok && Marshal.GetLastWin32Error() != 1168 /* NOT_FOUND */)
            _log.LogWarning("CredDeleteW failed: {err}", Marshal.GetLastWin32Error());
        return ok;
    }

    /// <inheritdoc/>
    public bool Exists(string accountId)
    {
        if (!CredReadW(Key(accountId), CRED_TYPE_GENERIC, 0, out IntPtr ptr))
            return false;
        CredFree(ptr);
        return true;
    }
}
