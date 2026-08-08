namespace ONEVO.Agent.Service.Security;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

public sealed class CredentialStore
{
    private static readonly string CredentialPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ONEVO", "Agent", "credential.dat");

    private static readonly string RefreshTokenPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ONEVO", "Agent", "refresh.dat");

    public void StoreDeviceJwt(string jwt) => ProtectAndWrite(CredentialPath, jwt);

    public string? ReadDeviceJwt() => ReadAndUnprotect(CredentialPath);

    public void ClearDeviceJwt()
    {
        if (File.Exists(CredentialPath))
            File.Delete(CredentialPath);
    }

    /// <summary>
    /// The refresh token is what survives a Service restart (the access token is
    /// short-lived). Rotation must write the new token before the caller discards
    /// the old one, so a crash mid-refresh cannot brick enrollment.
    /// </summary>
    public void StoreRefreshToken(string refreshToken) => ProtectAndWrite(RefreshTokenPath, refreshToken);

    public string? ReadRefreshToken() => ReadAndUnprotect(RefreshTokenPath);

    public void ClearRefreshToken()
    {
        if (File.Exists(RefreshTokenPath))
            File.Delete(RefreshTokenPath);
    }

    private static void ProtectAndWrite(string path, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(value), null, DataProtectionScope.LocalMachine);

        // Write to a temp file then rename over the target — an interrupted write
        // (crash, power loss) can never leave a partially-written credential file.
        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, encrypted);
        File.Move(tempPath, path, overwrite: true);
    }

    private static string? ReadAndUnprotect(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(path);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
