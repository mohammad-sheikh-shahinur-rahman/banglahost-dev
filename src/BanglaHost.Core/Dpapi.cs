using System.Runtime.InteropServices;

namespace BanglaHost.Core;

/// <summary>
/// DPAPI (CurrentUser scope) without a NuGet dependency — P/Invoke over crypt32,
/// so Core keeps its zero-PackageReference posture. Used to protect the database
/// root password at rest (B10): config.json holds an opaque "dpapi:&lt;base64&gt;"
/// blob instead of clear text.
/// </summary>
internal static class Dpapi
{
    [StructLayout(LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptProtectData(
        ref DATA_BLOB pDataIn, string? szDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(
        ref DATA_BLOB pDataIn, string? ppszDataDescr, ref DATA_BLOB pOptionalEntropy,
        IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    public static string Protect(string plaintext)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var inn = new DATA_BLOB { cbData = bytes.Length, pbData = pin.AddrOfPinnedObject() };
            var entropy = new DATA_BLOB();
            if (!CryptProtectData(ref inn, null, ref entropy, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, out var outt))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                var buf = new byte[outt.cbData];
                Marshal.Copy(outt.pbData, buf, 0, buf.Length);
                return Convert.ToBase64String(buf);
            }
            finally { LocalFree(outt.pbData); }
        }
        finally { pin.Free(); }
    }

    public static string? Unprotect(string base64)
    {
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch { return null; }
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var inn = new DATA_BLOB { cbData = bytes.Length, pbData = pin.AddrOfPinnedObject() };
            var entropy = new DATA_BLOB();
            if (!CryptUnprotectData(ref inn, null, ref entropy, IntPtr.Zero, IntPtr.Zero,
                    CRYPTPROTECT_UI_FORBIDDEN, out var outt))
                return null;   // wrong user/profile — treat as unset, prompt the user
            try
            {
                var buf = new byte[outt.cbData];
                Marshal.Copy(outt.pbData, buf, 0, buf.Length);
                return System.Text.Encoding.UTF8.GetString(buf);
            }
            finally { LocalFree(outt.pbData); }
        }
        finally { pin.Free(); }
    }
}
