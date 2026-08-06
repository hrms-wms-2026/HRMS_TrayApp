namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Collectors;

public sealed class FakeAgentCollector : IAgentCollector
{
    private TaskCompletionSource _startSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _stopSignal  = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name => "Fake";
    public bool IsRunning { get; private set; }
    public AgentPolicy? LastPolicy { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount  { get; private set; }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        IsRunning = true;
        LastPolicy = policy;
        StartCount++;
        _startSignal.TrySetResult();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        IsRunning = false;
        StopCount++;
        _stopSignal.TrySetResult();
        return Task.CompletedTask;
    }

    public Task WaitForStartAsync(TimeSpan timeout) =>
        _startSignal.Task.WaitAsync(timeout);

    public Task WaitForStopAsync(TimeSpan timeout) =>
        _stopSignal.Task.WaitAsync(timeout);

    public void ResetSignals()
    {
        _startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _stopSignal  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
