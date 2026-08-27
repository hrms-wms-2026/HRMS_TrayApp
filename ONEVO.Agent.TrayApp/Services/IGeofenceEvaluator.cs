using ONEVO.Agent.Shared.Models;

namespace ONEVO.Agent.TrayApp.Services;

/// <summary>Pure, deterministic geofence check comparing a captured location fix against a confirmed work location.</summary>
public interface IGeofenceEvaluator
{
    ClockInLocationVerification Evaluate(
        Guid attemptId,
        GeoLocationFix current,
        WorkLocationReference reference);
}
