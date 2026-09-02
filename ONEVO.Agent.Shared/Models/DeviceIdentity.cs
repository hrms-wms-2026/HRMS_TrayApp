namespace ONEVO.Agent.Shared.Models;

public sealed record DeviceIdentity
{
    public required string DeviceId { get; init; }
    public required string AgentId { get; init; }
    public required string TenantId { get; init; }
    public required string DeviceFingerprint { get; init; }
    // NO JWT field — Device JWT is owned exclusively by Service via DPAPI (§8.2)

    /// <summary>The subdomain slug of the tenant this device last connected to (e.g. "acme"),
    /// used to skip the generic base-host login detour on a later "Connect via Browser".
    /// Optional (not `required`) so existing identity.json files without it still deserialize.</summary>
    public string? TenantSlug { get; init; }
}
