using ONEVO.Agent.Shared.Models;

namespace ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Pure, deterministic evaluator that compares a captured <see cref="GeoLocationFix"/> against a confirmed
/// <see cref="WorkLocationReference"/> using the Haversine distance and an accuracy-expanded effective radius.
/// Consumes no platform APIs so it can be unit tested without a running MAUI host.
/// </summary>
public sealed class GeofenceEvaluator : IGeofenceEvaluator
{
    private const double EarthRadiusMeters = 6371000d;
    private const string LowAccuracyReason = "LOW_ACCURACY";

    private readonly double _maxAcceptedAccuracyMeters;

    public GeofenceEvaluator(double maxAcceptedAccuracyMeters = 100)
    {
        _maxAcceptedAccuracyMeters = maxAcceptedAccuracyMeters;
    }

    public ClockInLocationVerification Evaluate(
        Guid attemptId,
        GeoLocationFix current,
        WorkLocationReference reference)
    {
        var currentAccuracy = current.AccuracyMeters ?? 0d;
        var referenceAccuracy = reference.AccuracyMeters ?? 0d;
        var effectiveRadiusMeters = Math.Max(reference.RadiusMeters, referenceAccuracy + currentAccuracy);

        if (current.AccuracyMeters.HasValue && current.AccuracyMeters.Value > _maxAcceptedAccuracyMeters)
        {
            return new ClockInLocationVerification(
                attemptId,
                current,
                reference,
                LocationVerificationVerdict.Inaccurate,
                DistanceMeters: null,
                EffectiveRadiusMeters: effectiveRadiusMeters,
                Reason: LowAccuracyReason);
        }

        var distanceMeters = CalculateHaversineDistanceMeters(
            current.Latitude, current.Longitude,
            reference.Latitude, reference.Longitude);

        var verdict = distanceMeters <= effectiveRadiusMeters
            ? LocationVerificationVerdict.Match
            : LocationVerificationVerdict.Mismatch;

        return new ClockInLocationVerification(
            attemptId,
            current,
            reference,
            verdict,
            DistanceMeters: distanceMeters,
            EffectiveRadiusMeters: effectiveRadiusMeters,
            Reason: null);
    }

    private static double CalculateHaversineDistanceMeters(
        double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusMeters * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
