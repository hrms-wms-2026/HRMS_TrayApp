namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.TrayApp.Services;

public sealed class GeofenceEvaluatorTests
{
    private static readonly WorkLocationReference Reference = new(
        WorkLocationKind.WorkFromHome, "WFH", "Work From Home",
        6.9271, 79.8612, 20, 250, DateTimeOffset.Parse("2026-08-27T00:00:00Z"));

    [Fact]
    public void Evaluate_InsideRadius_ReturnsMatch()
    {
        var current = new GeoLocationFix(6.9272, 79.8612, 15, DateTimeOffset.UtcNow);
        var result = new GeofenceEvaluator().Evaluate(Guid.NewGuid(), current, Reference);
        Assert.Equal(LocationVerificationVerdict.Match, result.Verdict);
        Assert.True(result.DistanceMeters < result.EffectiveRadiusMeters);
    }

    [Fact]
    public void Evaluate_OutsideRadius_ReturnsMismatch()
    {
        var current = new GeoLocationFix(6.9371, 79.8612, 15, DateTimeOffset.UtcNow);
        var result = new GeofenceEvaluator().Evaluate(Guid.NewGuid(), current, Reference);
        Assert.Equal(LocationVerificationVerdict.Mismatch, result.Verdict);
    }

    [Fact]
    public void Evaluate_PoorAccuracy_ReturnsInaccurate_NotMismatch()
    {
        var current = new GeoLocationFix(6.9371, 79.8612, 180, DateTimeOffset.UtcNow);
        var result = new GeofenceEvaluator(maxAcceptedAccuracyMeters: 100)
            .Evaluate(Guid.NewGuid(), current, Reference);
        Assert.Equal(LocationVerificationVerdict.Inaccurate, result.Verdict);
        Assert.Equal("LOW_ACCURACY", result.Reason);
    }

    [Fact]
    public void Evaluate_AccuracySumCanExpandEffectiveRadius()
    {
        var looseReference = Reference with { RadiusMeters = 100, AccuracyMeters = 70 };
        var current = new GeoLocationFix(6.9282, 79.8612, 60, DateTimeOffset.UtcNow);
        var result = new GeofenceEvaluator().Evaluate(Guid.NewGuid(), current, looseReference);
        Assert.Equal(130, result.EffectiveRadiusMeters);
    }
}
