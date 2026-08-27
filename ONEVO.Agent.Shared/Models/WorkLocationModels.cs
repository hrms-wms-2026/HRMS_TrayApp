namespace ONEVO.Agent.Shared.Models;

public enum WorkLocationKind
{
    Office,
    WorkFromHome,
    OtherApprovedLocation,
}

public enum LocationVerificationVerdict
{
    Match,
    Mismatch,
    Unavailable,
    Inaccurate,
}

public sealed record GeoLocationFix(
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    DateTimeOffset CapturedAt);

public sealed record WorkLocationReference(
    WorkLocationKind Kind,
    string Code,
    string DisplayName,
    double Latitude,
    double Longitude,
    double? AccuracyMeters,
    double RadiusMeters,
    DateTimeOffset ConfirmedAt);

public sealed record ClockInLocationVerification(
    Guid AttemptId,
    GeoLocationFix? CurrentFix,
    WorkLocationReference Reference,
    LocationVerificationVerdict Verdict,
    double? DistanceMeters,
    double EffectiveRadiusMeters,
    string? Reason);
