namespace ONEVO.Agent.TrayApp.Services;

/// <summary>Device GPS / live location (Windows location services).</summary>
public interface ILocationService
{
    /// <summary>
    /// Returns current coordinates, or null if unavailable / denied / not supported.
    /// </summary>
    Task<GeoPoint?> GetCurrentAsync(CancellationToken ct = default);
}

public sealed record GeoPoint(double Latitude, double Longitude, double? AccuracyMeters = null);
