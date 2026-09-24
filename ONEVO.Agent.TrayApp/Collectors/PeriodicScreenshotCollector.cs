namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.Shared;
using ONEVO.Agent.TrayApp.Capture;
using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Captures a working-hours screenshot on a fixed interval and uploads it immediately.
/// Runs only while monitoring is active and screenshot capture is enabled.
/// </summary>
public sealed class PeriodicScreenshotCollector : IAgentCollector
{
    private readonly ILogger<PeriodicScreenshotCollector> _logger;
    private readonly IScreenshotCaptureService _capture;
    private readonly INamedPipeClient _pipe;
    private readonly TimeSpan _firstDelay;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;

    public string Name => "PeriodicScreenshot";

    public bool IsRunning
    {
        get { lock (_gate) return _running; }
    }

    public PeriodicScreenshotCollector(
        ILogger<PeriodicScreenshotCollector> logger,
        IScreenshotCaptureService capture,
        INamedPipeClient pipe,
        TimeSpan? firstDelay = null,
        TimeSpan? interval = null)
    {
        _logger = logger;
        _capture = capture;
        _pipe = pipe;
        _firstDelay = firstDelay ?? TimeSpan.FromSeconds(5);
        _interval = interval ?? TimeSpan.FromSeconds(Constants.PeriodicScreenshotIntervalSeconds);
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        if (!policy.ScreenshotEnabled || !policy.ActivitySignalEnabled)
        {
            _logger.LogDebug("{Name}: screenshot capture disabled — not starting", Name);
            return Task.CompletedTask;
        }

        lock (_gate)
        {
            if (_running)
                return Task.CompletedTask;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _loop = LoopAsync(_cts.Token);
            _running = true;
        }

        _logger.LogInformation("{Name}: started interval={Seconds}s", Name, (int)_interval.TotalSeconds);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            if (!_running)
                return;
            _running = false;
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        if (loop is not null)
        {
            try { await loop.WaitAsync(TimeSpan.FromSeconds(3), ct); }
            catch { /* stopping */ }
        }

        _logger.LogInformation("{Name}: stopped", Name);
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        try
        {
            if (_firstDelay > TimeSpan.Zero)
                await Task.Delay(_firstDelay, ct);
            await CaptureAndSubmitAsync(ct);

            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(ct))
                await CaptureAndSubmitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
    }

    private async Task CaptureAndSubmitAsync(CancellationToken ct)
    {
        try
        {
            var shot = await _capture.CaptureAsync(ct);
            if (!shot.Success || shot.JpegBytes.IsEmpty)
            {
                _logger.LogInformation("{Name}: capture skipped ({Reason})", Name, shot.FailureCode ?? "empty");
                return;
            }

            var capturedAt = shot.CapturedAt ?? DateTimeOffset.UtcNow;
            var accepted = await _pipe.SubmitPeriodicScreenshotAsync(capturedAt, shot.JpegBytes, ct);
            _logger.LogInformation(
                "{Name}: upload {Result} bytes={Bytes}",
                Name,
                accepted ? "accepted" : "rejected",
                shot.JpegBytes.Length);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "{Name}: capture failed", Name);
        }
    }
}
