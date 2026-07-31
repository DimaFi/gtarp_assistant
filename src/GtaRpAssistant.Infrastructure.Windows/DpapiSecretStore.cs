using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using GtaRpAssistant.Core;

namespace GtaRpAssistant.Infrastructure.Windows;

public sealed class DpapiSecretStore(string directory) : ISecretStore
{
    public async Task SaveAsync(string key, string value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var encrypted = Protect(Encoding.UTF8.GetBytes(value));
        await File.WriteAllBytesAsync(PathFor(key), encrypted, cancellationToken);
    }

    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;
        return Encoding.UTF8.GetString(Unprotect(await File.ReadAllBytesAsync(path, cancellationToken)));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = PathFor(key); if (File.Exists(path)) File.Delete(path); return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        var safe = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(directory, safe + ".secret");
    }

    private static byte[] Protect(byte[] data) => Crypt(data, protect: true);
    private static byte[] Unprotect(byte[] data) => Crypt(data, protect: false);

    private static byte[] Crypt(byte[] data, bool protect)
    {
        var input = new DataBlob(); var output = new DataBlob();
        try
        {
            input.Data = Marshal.AllocHGlobal(data.Length); input.Size = data.Length; Marshal.Copy(data, 0, input.Data, data.Length);
            var ok = protect ? CryptProtectData(ref input, null, nint.Zero, nint.Zero, nint.Zero, 0x1, out output) : CryptUnprotectData(ref input, nint.Zero, nint.Zero, nint.Zero, nint.Zero, 0x1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size]; Marshal.Copy(output.Data, result, 0, output.Size); return result;
        }
        finally { if (input.Data != 0) Marshal.FreeHGlobal(input.Data); if (output.Data != 0) LocalFree(output.Data); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct DataBlob { public int Size; public nint Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptProtectData(ref DataBlob input, string? description, nint entropy, nint reserved, nint prompt, int flags, out DataBlob output);
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool CryptUnprotectData(ref DataBlob input, nint description, nint entropy, nint reserved, nint prompt, int flags, out DataBlob output);
    [DllImport("kernel32.dll")] private static extern nint LocalFree(nint memory);
}
