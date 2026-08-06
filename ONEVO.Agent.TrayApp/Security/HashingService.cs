namespace ONEVO.Agent.TrayApp.Security;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// SHA-256 hashes window titles in memory before any IPC/log/disk write (§8.3).
/// The raw title never leaves this method.
/// </summary>
public static class HashingService
{
    public static string HashWindowTitle(string rawTitle)
    {
        if (rawTitle.Length == 0)
            return string.Empty;

        Span<byte> inputBytes = stackalloc byte[Encoding.UTF8.GetMaxByteCount(rawTitle.Length)];
        var written = Encoding.UTF8.GetBytes(rawTitle, inputBytes);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(inputBytes[..written], hash);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
