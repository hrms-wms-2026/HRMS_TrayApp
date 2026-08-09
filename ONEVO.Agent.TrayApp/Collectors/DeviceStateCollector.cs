namespace ONEVO.Agent.TrayApp.Collectors;

using System.Text.Json;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.Models;

public sealed class DeviceStateCollector : IAgentCollector, IAsyncDisposable
{
    public string Name => "DeviceState";

    private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(60);

    private readonly ILogger<DeviceStateCollector> _logger;
    private readonly INamedPipeClient _pipe;
    private readonly ISessionDayMetrics _dayMetrics;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;

    public DeviceStateCollector(
        ILogger<DeviceStateCollector> logger,
        INamedPipeClient pipe,
        ISessionDayMetrics dayMetrics)
    {
        _logger     = logger;
        _pipe       = pipe;
        _dayMetrics = dayMetrics;
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _cts     = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop    = SampleLoopAsync(_cts.Token);
        _running = true;
        _logger.LogInformation("{Name}: started", Name);
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

    private async Task SampleLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(SampleWindow);
            while (await timer.WaitForNextTickAsync(ct))
                await EmitSampleAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task EmitSampleAsync(CancellationToken ct)
    {
        try
        {
            var now         = DateTimeOffset.UtcNow;
            var idleSeconds = IdleDetector.GetIdleSeconds();
            var isIdle      = IdleDetector.IsIdle();

            // No-input has persisted through this whole sample window — attribute it as idle time.
            // (idleSeconds is time-since-last-input, not a per-window delta, so this undercounts the
            // first window crossing the threshold and is the simplest correct approximation.)
            if (isIdle)
                _dayMetrics.AddIdleSample(SampleWindow);

            var record = new CollectionRecord
            {
                EventId          = Guid.NewGuid().ToString("N"),
                RecordType       = CollectionRecordTypes.DeviceStateSnapshot,
                SchemaVersion    = CollectionSchemaVersions.DeviceStateSnapshotV1,
                CaptureTimestamp = now,
                DeviceId         = Environment.MachineName,
                Payload          = JsonSerializer.SerializeToElement(new DeviceStateSnapshotPayload
                {
                    CapturedAt  = now,
                    IdleSeconds = idleSeconds,
                    IsIdle      = isIdle
                })
            };
            await _pipe.SubmitCollectionRecordsAsync([record], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "{Name}: emit failed", Name);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
