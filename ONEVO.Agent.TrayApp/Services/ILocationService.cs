using ONEVO.Agent.Shared.Models;

namespace ONEVO.Agent.TrayApp.Services;

/// <summary>Reasons an explicit location capture can fail to produce a fix, mapped from platform-specific exceptions.</summary>
public enum LocationCaptureFailure
{
    PermissionDenied,
    ServicesDisabled,
    NotSupported,
    TimedOut,
    Unavailable
}

/// <summary>
/// Outcome of a single location capture attempt: either a successful <see cref="GeoLocationFix"/>
/// or an explicit <see cref="LocationCaptureFailure"/> reason — never a bare null.
/// </summary>
public sealed record LocationCaptureResult(
    GeoLocationFix? Fix,
    LocationCaptureFailure? Failure)
{
    public bool IsSuccess => Fix is not null;

    public static LocationCaptureResult Success(GeoLocationFix fix) => new(fix, null);

    public static LocationCaptureResult Failed(LocationCaptureFailure failure) => new(null, failure);
}

/// <summary>Captures a single high-accuracy device location fix, distinguishing why a capture failed.</summary>
public interface ILocationService
{
    Task<LocationCaptureResult> GetCurrentAsync(CancellationToken ct = default);
}
