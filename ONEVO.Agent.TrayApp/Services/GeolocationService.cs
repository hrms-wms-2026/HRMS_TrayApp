using ONEVO.Agent.Shared.Models;

namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Captures a single high-accuracy Windows location fix via MAUI Essentials Geolocation.
/// Requests <see cref="Permissions.LocationWhenInUse"/> if not already granted and maps every
/// platform failure mode to an explicit <see cref="LocationCaptureFailure"/> instead of returning null.
/// </summary>
public sealed class GeolocationService : ILocationService
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(12);

    public async Task<LocationCaptureResult> GetCurrentAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();

            if (status != PermissionStatus.Granted)
                return LocationCaptureResult.Failed(LocationCaptureFailure.PermissionDenied);

            var request = new GeolocationRequest(GeolocationAccuracy.High, CaptureTimeout);
            var location = await Geolocation.Default.GetLocationAsync(request, ct);

            if (location is null)
                return LocationCaptureResult.Failed(LocationCaptureFailure.Unavailable);

            var fix = new GeoLocationFix(
                location.Latitude,
                location.Longitude,
                location.Accuracy,
                location.Timestamp);

            return LocationCaptureResult.Success(fix);
        }
        catch (PermissionException)
        {
            return LocationCaptureResult.Failed(LocationCaptureFailure.PermissionDenied);
        }
        catch (FeatureNotEnabledException)
        {
            return LocationCaptureResult.Failed(LocationCaptureFailure.ServicesDisabled);
        }
        catch (FeatureNotSupportedException)
        {
            return LocationCaptureResult.Failed(LocationCaptureFailure.NotSupported);
        }
        catch (OperationCanceledException)
        {
            // Covers TaskCanceledException too (it derives from OperationCanceledException) —
            // both the request's internal timeout and an external ct surface here.
            return LocationCaptureResult.Failed(LocationCaptureFailure.TimedOut);
        }
        catch
        {
            return LocationCaptureResult.Failed(LocationCaptureFailure.Unavailable);
        }
    }
}
