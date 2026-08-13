namespace ONEVO.Agent.Service.Buffer;

/// <summary>Encrypts/decrypts evidence bytes at rest using machine-scoped DPAPI.</summary>
public interface IEvidenceProtector
{
    byte[] Protect(ReadOnlyMemory<byte> plaintext, Guid attemptId);
    byte[] Unprotect(ReadOnlyMemory<byte> protectedBytes, Guid attemptId);
}
