namespace ONEVO.Agent.Shared.Tests;

using ONEVO.Agent.Shared.Diagnostics;
using Xunit;

public class AgentCorrelationTests
{
    [Fact]
    public void Begin_SetsCurrent_AndDisposeRestoresPrevious()
    {
        Assert.Null(AgentCorrelation.Current);
        using (AgentCorrelation.Begin("outer"))
        {
            using (AgentCorrelation.Begin("inner"))
                Assert.Equal("inner", AgentCorrelation.Current);
            Assert.Equal("outer", AgentCorrelation.Current);
        }
        Assert.Null(AgentCorrelation.Current);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bad\r\nX-Injected: 1")]
    [InlineData("has space")]
    [InlineData("ünïcode")]
    public void Begin_IgnoresUnsafeIds_KeepsCurrent(string? id)
    {
        using (AgentCorrelation.Begin("kept"))
        using (AgentCorrelation.Begin(id))
            Assert.Equal("kept", AgentCorrelation.Current);
    }

    [Fact]
    public void Begin_RejectsOverlongId()
    {
        using (AgentCorrelation.Begin(new string('a', 65)))
            Assert.Null(AgentCorrelation.Current);
        using (AgentCorrelation.Begin(new string('a', 64)))
            Assert.NotNull(AgentCorrelation.Current);
    }

    [Fact]
    public async Task Current_FlowsAcrossAwaits_AndDoesNotLeakToOtherFlows()
    {
        string? seenInOtherFlow = "unset";
        using (AgentCorrelation.Begin("flow-a"))
        {
            await Task.Yield();
            Assert.Equal("flow-a", AgentCorrelation.Current);
        }
        await Task.Run(() => seenInOtherFlow = AgentCorrelation.Current);
        Assert.Null(seenInOtherFlow);
    }

    [Fact]
    public void NewId_IsValid()
    {
        Assert.True(AgentCorrelation.IsValid(AgentCorrelation.NewId()));
    }
}
