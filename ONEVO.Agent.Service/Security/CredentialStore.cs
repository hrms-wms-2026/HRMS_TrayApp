namespace ONEVO.Agent.Service.Security;

using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

public sealed class CredentialStore
{
    private static readonly string CredentialPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ONEVO", "Agent", "credential.dat");

    public void StoreDeviceJwt(string jwt)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CredentialPath)!);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(jwt), null, DataProtectionScope.LocalMachine);
        File.WriteAllBytes(CredentialPath, encrypted);
    }

    public string? ReadDeviceJwt()
    {
        if (!File.Exists(CredentialPath)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(CredentialPath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.LocalMachine);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public void ClearDeviceJwt()
    {
        if (File.Exists(CredentialPath))
            File.Delete(CredentialPath);
    }
}
