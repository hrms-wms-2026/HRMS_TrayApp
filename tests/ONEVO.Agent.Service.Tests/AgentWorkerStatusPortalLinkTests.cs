namespace ONEVO.Agent.Service.Tests;

using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ONEVO.Agent.Service.Api;
using ONEVO.Agent.Service.Buffer;
using ONEVO.Agent.Service.Configuration;
using ONEVO.Agent.Service.Lifecycle;
using ONEVO.Agent.Service.Policy;
using ONEVO.Agent.Service.Security;
using ONEVO.Agent.Service.Tests.Security;
using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using Xunit;

/// <summary>AUD-030: every status the Tray receives names the enrolled tenant's own portal.</summary>
[Collection(CredentialStoreFileCollection.Name)]
public sealed class AgentWorkerStatusPortalLinkTests : IDisposable
{
    private readonly string _identityDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_identityDir, recursive: true); } catch { }
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "ONEVO.Agent.Service";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private AgentWorker BuildWorker(string? tenantSlug, bool withPortalLinks = true)
    {
        var identityStore = new DeviceIdentityStore(_identityDir);
        if (tenantSlug is not null)
            identityStore.Save(new DeviceIdentity { DeviceId = "d", AgentId = "a", TenantId = "t", DeviceFingerprint = "f", TenantSlug = tenantSlug });

        var options = Options.Create(new AgentOptions { PortalRootDomain = "onexso.com", PortalProductionPort = 443 });
        return new AgentWorker(
            NullLogger<AgentWorker>.Instance,
            null!, // NamedPipeServer - not touched
            new AgentStateMachine(),
            new PolicyCache(),
            ActivityRecordBuffer.CreateInMemory(),
            new PresenceSession(),
            new LifecycleGate(),
            options,
            new OnevoApiClient(null!, NullLogger<OnevoApiClient>.Instance),
            TestStores.NewCredentialStore(),
            identityStore,
            null!, // EnrollmentCoordinator - not touched
            null!, // InactivityEvidenceHandler - not touched
            null!, // EvidenceSpoolStore - not touched
            new PendingLegalChallengeStore(),
            portalLinks: withPortalLinks ? new PortalLinks(options, new ProductionEnvironment()) : null);
    }

    private static StatusResponsePayload Status(AgentWorker worker) =>
        worker.BuildStatusEnvelope(null).Payload!.Value.Deserialize<StatusResponsePayload>()!;

    [Fact]
    public void Enrolled_StatusCarriesTheTenantsPortal()
    {
        Assert.Equal("https://contoso.onexso.com", Status(BuildWorker("contoso")).PortalBaseUrl);
    }

    [Fact]
    public void NotEnrolled_StatusHasNoPortal()
    {
        Assert.Null(Status(BuildWorker(tenantSlug: null)).PortalBaseUrl);
    }

    [Fact]
    public void UnsafeSlug_StatusHasNoPortal()
    {
        Assert.Null(Status(BuildWorker("evil.com#")).PortalBaseUrl);
    }

    [Fact]
    public void WithoutPortalLinks_StatusStillBuilds()
    {
        Assert.Null(Status(BuildWorker("contoso", withPortalLinks: false)).PortalBaseUrl);
    }
}
