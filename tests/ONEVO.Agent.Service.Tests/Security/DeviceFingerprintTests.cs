namespace ONEVO.Agent.Service.Tests.Security;

using ONEVO.Agent.Service.Security;
using Xunit;

public class DeviceFingerprintTests
{
    [Fact]
    public void Compute_IsDeterministic_AcrossCalls()
    {
        // Backend revokes all refresh tokens for the device on any fingerprint
        // mismatch — this must never drift between calls on the same machine.
        var first = DeviceFingerprint.Compute();
        var second = DeviceFingerprint.Compute();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_ReturnsNonEmptyLowercaseHex()
    {
        var fingerprint = DeviceFingerprint.Compute();

        Assert.False(string.IsNullOrWhiteSpace(fingerprint));
        Assert.Equal(fingerprint, fingerprint.ToLowerInvariant());
        Assert.Matches("^[0-9a-f]+$", fingerprint);
    }
}
