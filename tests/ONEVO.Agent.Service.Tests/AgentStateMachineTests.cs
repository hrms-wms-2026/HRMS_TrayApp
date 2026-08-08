namespace ONEVO.Agent.Service.Tests;

using ONEVO.Agent.Service;
using ONEVO.Agent.Shared.Models;
using Xunit;

public class AgentStateMachineTests
{
    [Fact]
    public void InitialState_IsUnenrolled()
    {
        var sm = new AgentStateMachine();
        Assert.Equal(MonitoringState.Unenrolled, sm.CurrentState);
    }

    [Theory]
    [InlineData(MonitoringState.Unenrolled, MonitoringState.Stopped)]
    [InlineData(MonitoringState.Stopped,    MonitoringState.Active)]
    [InlineData(MonitoringState.Active,     MonitoringState.Paused)]
    [InlineData(MonitoringState.Active,     MonitoringState.Stopped)]
    [InlineData(MonitoringState.Paused,     MonitoringState.Active)]
    [InlineData(MonitoringState.Paused,     MonitoringState.Stopped)]
    [InlineData(MonitoringState.Locked,     MonitoringState.Stopped)]
    [InlineData(MonitoringState.Stopped,    MonitoringState.Unenrolled)]
    public void LegalTransition_Succeeds(MonitoringState from, MonitoringState to)
    {
        var sm = BuildAt(from);
        var result = sm.TryTransition(to, out var previous);
        Assert.True(result);
        Assert.Equal(from, previous);
        Assert.Equal(to, sm.CurrentState);
    }

    [Theory]
    [InlineData(MonitoringState.Unenrolled, MonitoringState.Active)]
    [InlineData(MonitoringState.Unenrolled, MonitoringState.Paused)]
    [InlineData(MonitoringState.Stopped,    MonitoringState.Paused)]
    [InlineData(MonitoringState.Active,     MonitoringState.Unenrolled)]
    [InlineData(MonitoringState.Paused,     MonitoringState.Unenrolled)]
    [InlineData(MonitoringState.Unenrolled, MonitoringState.Unenrolled)]
    public void IllegalTransition_Fails_StateUnchanged(MonitoringState from, MonitoringState to)
    {
        var sm = BuildAt(from);
        var result = sm.TryTransition(to, out _);
        Assert.False(result);
        Assert.Equal(from, sm.CurrentState);
    }

    [Theory]
    [InlineData(MonitoringState.Unenrolled)]
    [InlineData(MonitoringState.Stopped)]
    [InlineData(MonitoringState.Active)]
    [InlineData(MonitoringState.Paused)]
    public void AnyState_ToLocked_Succeeds(MonitoringState from)
    {
        var sm = BuildAt(from);
        Assert.True(sm.TryTransition(MonitoringState.Locked, out _));
        Assert.Equal(MonitoringState.Locked, sm.CurrentState);
    }

    [Fact]
    public void SameState_Transition_Fails()
    {
        var sm = BuildAt(MonitoringState.Stopped);
        Assert.False(sm.TryTransition(MonitoringState.Stopped, out _));
    }

    [Fact]
    public void TryTransition_ReturnsCorrectPreviousState()
    {
        var sm = new AgentStateMachine();
        sm.TryTransition(MonitoringState.Stopped, out _);
        sm.TryTransition(MonitoringState.Active, out var previous);
        Assert.Equal(MonitoringState.Stopped, previous);
    }

    private static AgentStateMachine BuildAt(MonitoringState target)
    {
        var sm = new AgentStateMachine();
        var path = target switch
        {
            MonitoringState.Unenrolled => Array.Empty<MonitoringState>(),
            MonitoringState.Stopped    => [MonitoringState.Stopped],
            MonitoringState.Active     => [MonitoringState.Stopped, MonitoringState.Active],
            MonitoringState.Paused     => [MonitoringState.Stopped, MonitoringState.Active, MonitoringState.Paused],
            MonitoringState.Locked     => [MonitoringState.Locked],
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        foreach (var s in path) sm.TryTransition(s, out _);
        return sm;
    }
}
