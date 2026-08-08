namespace ONEVO.Agent.Shared.Models;

public sealed record DeviceIdentity
{
    public required string DeviceId { get; init; }
    public required string AgentId { get; init; }
    public required string TenantId { get; init; }
    public required string DeviceFingerprint { get; init; }
    // NO JWT field — Device JWT is owned exclusively by Service via DPAPI (§8.2)
}
