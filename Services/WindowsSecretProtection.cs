using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FormDesigner.Services;

/// <summary>Windows current-user DPAPI; unsupported hosts keep passwords in memory only.</summary>
internal static class WindowsSecretProtection
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, int flags, out Blob output);

    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);

    public static string Protect(string secret) => Transform(secret, protect: true);
    public static string Unprotect(string ciphertext) => Transform(ciphertext, protect: false);

    private static string Transform(string value, bool protect)
    {
        if (string.IsNullOrEmpty(value) || !OperatingSystem.IsWindows()) return "";
        byte[]? bytes = null;
        var input = new Blob();
        var output = new Blob();
        try
        {
            bytes = protect ? Encoding.UTF8.GetBytes(value) : Convert.FromBase64String(value);
            input.Length = bytes.Length;
            input.Data = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var ok = protect
                ? CryptProtectData(ref input, "FormDesigner PostgreSQL", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) return "";
            var result = new byte[output.Length];
            try
            {
                Marshal.Copy(output.Data, result, 0, result.Length);
                return protect ? Convert.ToBase64String(result) : Encoding.UTF8.GetString(result);
            }
            finally { CryptographicOperations.ZeroMemory(result); }
        }
        catch (Exception ex) when (ex is FormatException or ExternalException or DllNotFoundException) { return ""; }
        finally
        {
            if (bytes is not null)
            {
                if (input.Data != IntPtr.Zero)
                {
                    Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
                    Marshal.FreeHGlobal(input.Data);
                }
                CryptographicOperations.ZeroMemory(bytes);
            }
            if (output.Data != IntPtr.Zero)
            {
                Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length);
                LocalFree(output.Data);
            }
        }
    }
}
