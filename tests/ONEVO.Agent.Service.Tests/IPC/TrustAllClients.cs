using System.IO.Pipes;
using ONEVO.Agent.Service.IPC;

namespace ONEVO.Agent.Service.Tests.IPC;

/// <summary>Test-only verifier: the test process itself is the pipe client, and it is not the Tray App.</summary>
internal sealed class TrustAllClients : IIpcClientVerifier
{
    public static readonly TrustAllClients Instance = new();

    public bool IsTrusted(NamedPipeServerStream pipe) => true;
}
