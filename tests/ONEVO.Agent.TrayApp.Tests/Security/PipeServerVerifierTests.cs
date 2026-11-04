namespace ONEVO.Agent.TrayApp.Tests.Security;

using System.IO.Pipes;
using System.Security.Principal;
using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.TrayApp.Security;
using ONEVO.Agent.TrayApp.Services;
using Xunit;

public sealed class PipeServerVerifierTests
{
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier CurrentUser = WindowsIdentity.GetCurrent().User!;
    private static readonly SecurityIdentifier OtherUser = new("S-1-5-21-1111111111-2222222222-3333333333-1001");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SystemOrAdministratorsOwner_IsTrusted(bool allowCurrentUser)
    {
        Assert.Null(PipeServerVerifier.CheckOwner(LocalSystem, CurrentUser, allowCurrentUser));
        Assert.Null(PipeServerVerifier.CheckOwner(Administrators, CurrentUser, allowCurrentUser));
    }

    [Fact]
    public void AnotherUsersPipe_IsRejected_EvenWhenCurrentUserIsAllowed()
    {
        Assert.NotNull(PipeServerVerifier.CheckOwner(OtherUser, CurrentUser, allowCurrentUserOwner: true));
    }

    [Fact]
    public void CurrentUsersPipe_IsRejected_UnlessAllowed()
    {
        Assert.NotNull(PipeServerVerifier.CheckOwner(CurrentUser, CurrentUser, allowCurrentUserOwner: false));
        Assert.Null(PipeServerVerifier.CheckOwner(CurrentUser, CurrentUser, allowCurrentUserOwner: true));
    }

    [Fact]
    public void UnknownOwner_IsRejected()
    {
        Assert.NotNull(PipeServerVerifier.CheckOwner(null, CurrentUser, allowCurrentUserOwner: true));
    }

    [Fact]
    public async Task Check_ReadsTheOwnerOfARealConnectedPipe()
    {
        var pipeName = $"ONEVO.Agent.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        var accept = server.WaitForConnectionAsync();
        await client.ConnectAsync(5_000);
        await accept;

        // The in-test server is owned by the test user (or Administrators when elevated).
        Assert.Null(PipeServerVerifier.Check(client, allowCurrentUserOwner: true));
    }

    [Fact]
    public async Task Client_RejectedServer_NeverGetsTheHandshakeOrAnyMessage()
    {
        var pipeName = $"ONEVO.Agent.Tests.{Guid.NewGuid():N}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClient(
            NullLogger<NamedPipeClient>.Instance, pipeName, _ => "squatter");

        await client.StartAsync(cts.Token);
        await server.WaitForConnectionAsync(cts.Token);

        // The client drops the connection without writing anything (no nonce response, no status request).
        using var reader = new StreamReader(server, leaveOpen: true);
        string? line;
        try { line = await reader.ReadLineAsync(cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (IOException) { line = null; }

        Assert.Null(line);
    }
}
