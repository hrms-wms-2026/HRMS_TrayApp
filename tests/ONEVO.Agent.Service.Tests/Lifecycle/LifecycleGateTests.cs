namespace ONEVO.Agent.Service.Tests;

using ONEVO.Agent.Service.Lifecycle;
using Xunit;

public sealed class LifecycleGateTests
{
    [Fact]
    public void Default_CanActivate_IsFalse()
    {
        Assert.False(new LifecycleGate().CanActivate);
    }

    [Fact]
    public void AllGatesTrue_CanActivate_IsTrue()
    {
        var gate = new LifecycleGate();
        gate.SetDeviceEnrolled(true);
        gate.SetCredentialValid(true);
        gate.SetDeviceApproved(true);
        gate.SetEmployeeSessionActive(true);
        gate.SetConsentValid(true);
        gate.SetPolicyAllowsCollection(true);
        gate.SetPresenceSessionActive(true);
        gate.SetNotOnBreak(true);
        gate.SetNotOnApprovedTimeOff(true);
        Assert.True(gate.CanActivate);
    }

    [Fact]
    public void SingleGateFalse_CanActivate_IsFalse()
    {
        var gate = BuildFullyOpen();
        gate.SetNotOnBreak(false);
        Assert.False(gate.CanActivate);
    }

    [Fact]
    public void OnBreak_BlocksActivation()
    {
        var gate = BuildFullyOpen();
        gate.SetNotOnBreak(false);
        Assert.False(gate.CanActivate);
    }

    [Fact]
    public void ApprovedTimeOff_BlocksActivation()
    {
        var gate = BuildFullyOpen();
        gate.SetNotOnApprovedTimeOff(false);
        Assert.False(gate.CanActivate);
    }

    [Fact]
    public void RevokedDevice_BlocksActivation()
    {
        var gate = BuildFullyOpen();
        gate.SetDeviceApproved(false);
        Assert.False(gate.CanActivate);
    }

    [Fact]
    public void Snapshot_ReflectsCurrentState()
    {
        var gate = BuildFullyOpen();
        var snap = gate.Snapshot();
        Assert.True(snap.CanActivate);
        Assert.True(snap.NotOnBreak);
    }

    private static LifecycleGate BuildFullyOpen()
    {
        var g = new LifecycleGate();
        g.SetDeviceEnrolled(true);
        g.SetCredentialValid(true);
        g.SetDeviceApproved(true);
        g.SetEmployeeSessionActive(true);
        g.SetConsentValid(true);
        g.SetPolicyAllowsCollection(true);
        g.SetPresenceSessionActive(true);
        g.SetNotOnBreak(true);
        g.SetNotOnApprovedTimeOff(true);
        return g;
    }
}
