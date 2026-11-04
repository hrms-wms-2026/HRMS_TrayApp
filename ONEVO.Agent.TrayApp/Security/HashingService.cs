namespace ONEVO.Agent.TrayApp.Security;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Hashes window titles in memory before any IPC/log/disk write (§8.3). The raw title never leaves
/// this method.
///
/// The hash is an HMAC-SHA256 with a random key that never leaves this device. A plain SHA-256
/// can be reversed for any title someone can guess ("Inbox - Outlook", "Facebook - Chrome",
/// a colleague's name in a chat window) by hashing the guess and comparing. With the key kept on
/// the device, the same title still gives the same value here (so repeats can be grouped), but a
/// hash seen on the backend cannot be tested against guesses.
/// </summary>
public static class HashingService
{
    private static readonly Lazy<byte[]> DeviceKey = new(LoadOrCreateKey, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Tests redirect the key file away from the real profile; production uses the default.</summary>
    internal static string KeyPath { get; set; } = DefaultKeyPath;

    public static string DefaultKeyPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ONEVO", "Agent", "title-hash.key");

    public static string HashWindowTitle(string rawTitle) => HashWindowTitle(rawTitle, DeviceKey.Value);

    internal static string HashWindowTitle(string rawTitle, byte[] key)
    {
        if (rawTitle.Length == 0)
            return string.Empty;

        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(rawTitle));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Reads the device key (DPAPI CurrentUser-protected), creating it on first use. If the file
    /// cannot be read or written, a key for this process only is used: titles are still hashed with
    /// a secret, they just do not group across restarts.
    /// </summary>
    internal static byte[] LoadOrCreateKey()
    {
        try
        {
            if (File.Exists(KeyPath))
            {
                var key = ProtectedData.Unprotect(File.ReadAllBytes(KeyPath), null, DataProtectionScope.CurrentUser);
                if (key.Length == 32)
                    return key;
            }
        }
        catch (Exception)
        {
            // Corrupt or from another user/profile: replace it below.
        }

        var created = RandomNumberGenerator.GetBytes(32);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
            var tempPath = KeyPath + ".tmp";
            File.WriteAllBytes(tempPath, ProtectedData.Protect(created, null, DataProtectionScope.CurrentUser));
            File.Move(tempPath, KeyPath, overwrite: true);
        }
        catch (Exception)
        {
            // Process-only key (see summary).
        }

        return created;
    }
}
