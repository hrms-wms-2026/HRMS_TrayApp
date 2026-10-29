namespace ONEVO.Agent.Service.Security;

using System.Security.Cryptography;
using System.Text;

public sealed class CredentialStore : ICredentialStore
{
    private readonly string _directory;
    private readonly string _credentialPath;

    public CredentialStore() : this(SecureAgentStorage.DefaultDirectory)
    {
    }

    internal CredentialStore(string directory)
    {
        _directory = directory;
        _credentialPath = Path.Combine(directory, "credential.dat");
    }

    public void StoreDeviceJwt(string jwt)
    {
        // DPAPI LocalMachine scope is decryptable by any local account, so confidentiality
        // depends on the folder ACL applied here (ISS-010).
        SecureAgentStorage.EnsureDirectory(_directory);
        var encrypted = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(jwt), null, DataProtectionScope.LocalMachine);
        SecureAgentStorage.WriteAllBytesAtomic(_credentialPath, encrypted);
    }

    public string? ReadDeviceJwt()
    {
        if (!File.Exists(_credentialPath)) return null;
        try
        {
            var encrypted = File.ReadAllBytes(_credentialPath);
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
        if (File.Exists(_credentialPath))
            File.Delete(_credentialPath);
    }
}
