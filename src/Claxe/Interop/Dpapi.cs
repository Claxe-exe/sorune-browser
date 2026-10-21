using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Claxe.Interop;

/// <summary>
/// Windows Data Protection API, scoped to the current user: only this Windows account
/// on this machine can decrypt what it protects. Used for cookies and history.
/// </summary>
internal static partial class Dpapi
{
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    // Fixed app-specific entropy, so other apps using DPAPI can't trivially read Claxe's blobs.
    private static readonly byte[] Entropy = "Claxe.Browser.v1"u8.ToArray();

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public nint Data;
    }

    public static byte[] Protect(byte[] data) => Transform(data, protect: true);

    /// <exception cref="Win32Exception">The data is corrupt or belongs to another user.</exception>
    public static byte[] Unprotect(byte[] data) => Transform(data, protect: false);

    private static unsafe byte[] Transform(byte[] data, bool protect)
    {
        fixed (byte* input = data)
        fixed (byte* entropy = Entropy)
        {
            var inBlob = new DataBlob { Size = data.Length, Data = (nint)input };
            var entropyBlob = new DataBlob { Size = Entropy.Length, Data = (nint)entropy };
            var ok = protect
                ? CryptProtectData(ref inBlob, 0, ref entropyBlob, 0, 0, CRYPTPROTECT_UI_FORBIDDEN, out var outBlob)
                : CryptUnprotectData(ref inBlob, 0, ref entropyBlob, 0, 0, CRYPTPROTECT_UI_FORBIDDEN, out outBlob);
            if (!ok)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            try
            {
                var result = new byte[outBlob.Size];
                Marshal.Copy(outBlob.Data, result, 0, outBlob.Size);
                return result;
            }
            finally
            {
                LocalFree(outBlob.Data);
            }
        }
    }

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref DataBlob dataIn, nint description, ref DataBlob entropy,
        nint reserved, nint prompt, int flags, out DataBlob dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref DataBlob dataIn, nint description, ref DataBlob entropy,
        nint reserved, nint prompt, int flags, out DataBlob dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
}
