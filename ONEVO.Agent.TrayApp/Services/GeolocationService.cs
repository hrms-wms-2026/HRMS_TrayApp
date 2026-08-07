namespace ONEVO.Agent.TrayApp.Services;

using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;

/// <summary>MAUI Geolocation wrapper — requests permission then reads GPS.</summary>
public sealed class GeolocationService : ILocationService
{
    public async Task<GeoPoint?> GetCurrentAsync(CancellationToken ct = default)
    {
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                    return null;
            }

            // Medium accuracy is enough to match city-level offices.
            var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(12));
            var location = await Geolocation.Default.GetLocationAsync(request, ct);
            if (location is null)
                return null;

            return new GeoPoint(location.Latitude, location.Longitude, location.Accuracy);
        }
        catch (FeatureNotSupportedException)
        {
            return null;
        }
        catch (FeatureNotEnabledException)
        {
            return null;
        }
        catch (PermissionException)
        {
            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
