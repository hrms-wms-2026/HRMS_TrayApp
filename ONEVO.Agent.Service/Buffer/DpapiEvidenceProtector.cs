namespace ONEVO.Agent.Service.Buffer;

using System.Security.Cryptography;

public sealed class DpapiEvidenceProtector : IEvidenceProtector
{
    public byte[] Protect(ReadOnlyMemory<byte> plaintext, Guid attemptId) =>
        ProtectedData.Protect(
            plaintext.ToArray(),
            attemptId.ToByteArray(),
            DataProtectionScope.LocalMachine);

    public byte[] Unprotect(ReadOnlyMemory<byte> protectedBytes, Guid attemptId) =>
        ProtectedData.Unprotect(
            protectedBytes.ToArray(),
            attemptId.ToByteArray(),
            DataProtectionScope.LocalMachine);
}
