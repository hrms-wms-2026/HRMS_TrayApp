namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.Shared.Models;

/// <summary>
/// Screenshot collection — off unless effective policy enables it (§7.5).
/// Never runs during break, stopped, or uncertain lifecycle state.
/// </summary>
public sealed class ScreenshotCollector : IAgentCollector, IAsyncDisposable
{
    public string Name => "Screenshot";

    private readonly ILogger<ScreenshotCollector> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;

    public ScreenshotCollector(ILogger<ScreenshotCollector> logger) => _logger = logger;

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        // Policy MUST explicitly enable screenshots — default is off.
        if (!policy.ScreenshotEnabled)
        {
            _logger.LogDebug("{Name}: policy disabled — not starting", Name);
            return Task.CompletedTask;
        }

        if (_running) return Task.CompletedTask;

        _cts     = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop    = CaptureLoopAsync(_cts.Token);
        _running = true;
        _logger.LogInformation("{Name}: started (policy-enabled)", Name);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!_running) return;
        _running = false;
        if (_cts is not null) { await _cts.CancelAsync(); _cts.Dispose(); _cts = null; }
        if (_loop is not null) { try { await _loop.WaitAsync(TimeSpan.FromSeconds(3), ct); } catch { } _loop = null; }
        _logger.LogInformation("{Name}: stopped", Name);
    }

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(300));
            while (await timer.WaitForNextTickAsync(ct))
                _logger.LogDebug("{Name}: capture tick (stub)", Name);
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
