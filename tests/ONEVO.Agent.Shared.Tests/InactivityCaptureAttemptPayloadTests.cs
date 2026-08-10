namespace ONEVO.Agent.Shared.Tests;

using System;
using System.Text.Json;
using ONEVO.Agent.Shared.Models;
using Xunit;

public class InactivityCaptureAttemptPayloadTests
{
    [Fact]
    public void Attempt_payload_serializes_without_identity_or_image_data()
    {
        var value = new InactivityCaptureAttemptPayload
        {
            AttemptId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PolicyVersion = "policy-7",
            IdleStartedAt = DateTimeOffset.Parse("2026-08-10T01:00:00Z"),
            PromptedAt = DateTimeOffset.Parse("2026-08-10T01:05:00Z"),
            DecisionAt = DateTimeOffset.Parse("2026-08-10T01:05:03Z"),
            CapturedAt = null,
            IdleDurationSeconds = 300,
            MonitorCount = 0,
            Outcome = InactivityCaptureOutcomes.Declined,
            FailureCode = null
        };

        var json = JsonSerializer.Serialize(value);
        Assert.DoesNotContain("tenant", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("employee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data_base64", json, StringComparison.OrdinalIgnoreCase);
    }
}
