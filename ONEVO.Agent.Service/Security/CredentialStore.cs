namespace ONEVO.Agent.Service.Security;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

public sealed class CredentialStore
{
    private static readonly string DefaultCredentialDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ONEVO", "Agent");

    private readonly string _credentialPath;
    private readonly string _refreshTokenPath;

    /// <summary>Production callers use the parameterless constructor (real ProgramData path).
    /// Tests pass <paramref name="credentialDirectoryOverride"/> (a per-test temp directory) so a
    /// test run never overwrites or deletes a real enrolled agent's credentials (AUD-017).</summary>
    public CredentialStore(string? credentialDirectoryOverride = null)
    {
        var directory = credentialDirectoryOverride ?? DefaultCredentialDirectory;
        _credentialPath = Path.Combine(directory, "credential.dat");
        _refreshTokenPath = Path.Combine(directory, "refresh.dat");
    }

    public void StoreDeviceJwt(string jwt) => ProtectAndWrite(_credentialPath, jwt);

    public string? ReadDeviceJwt() => ReadAndUnprotect(_credentialPath);

    public void ClearDeviceJwt()
    {
        if (File.Exists(_credentialPath))
            File.Delete(_credentialPath);
    }

    /// <summary>
    /// The refresh token is what survives a Service restart (the access token is
    /// short-lived). Rotation must write the new token before the caller discards
    /// the old one, so a crash mid-refresh cannot brick enrollment.
    /// </summary>
    public void StoreRefreshToken(string refreshToken) => ProtectAndWrite(_refreshTokenPath, refreshToken);

    public string? ReadRefreshToken() => ReadAndUnprotect(_refreshTokenPath);

    public void ClearRefreshToken()
    {
        if (File.Exists(_refreshTokenPath))
            File.Delete(_refreshTokenPath);
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
