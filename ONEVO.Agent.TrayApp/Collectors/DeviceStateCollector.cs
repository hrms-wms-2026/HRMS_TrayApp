namespace ONEVO.Agent.TrayApp.Collectors;

using System.Text.Json;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.Models;

public sealed class DeviceStateCollector : IAgentCollector, IAsyncDisposable
{
    public string Name => "DeviceState";

    private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(60);
    // A fresh GPS fix is comparatively expensive (location-service wakeups, battery) compared to
    // the idle/active read every tick already does - only pull one on every 15th tick (~15
    // minutes at the 60s SampleWindow above), matching the interval decided for this feature.
    private const int LocationFixEveryNthTick = 15;

    private readonly ILogger<DeviceStateCollector> _logger;
    private readonly INamedPipeClient _pipe;
    private readonly ISessionDayMetrics _dayMetrics;
    private readonly ILocationService _location;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;
    private int _idleThresholdSeconds = IdleDetector.DefaultIdleThresholdSeconds;
    private bool _locationTrackingEnabled;
    private int _tickCount;

    public DeviceStateCollector(
        ILogger<DeviceStateCollector> logger,
        INamedPipeClient pipe,
        ISessionDayMetrics dayMetrics,
        ILocationService location)
    {
        _logger     = logger;
        _pipe       = pipe;
        _dayMetrics = dayMetrics;
        _location   = location;
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _idleThresholdSeconds = policy.IdleThresholdMinutes * 60;
        _locationTrackingEnabled = policy.LocationTrackingEnabled;
        _tickCount = 0;
        _cts     = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop    = SampleLoopAsync(_cts.Token);
        _running = true;
        _logger.LogInformation("{Name}: started (idle threshold {Seconds}s)", Name, _idleThresholdSeconds);
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

    /// <summary>Test-only synchronous hook into one sample tick, bypassing the real PeriodicTimer.</summary>
    internal Task EmitSampleForTestAsync(CancellationToken ct) => EmitSampleAsync(ct);

    private async Task EmitSampleAsync(CancellationToken ct)
    {
        try
        {
            var now         = DateTimeOffset.UtcNow;
            var idleSeconds = IdleDetector.GetIdleSeconds();
            var isIdle      = IdleDetector.IsIdle(_idleThresholdSeconds);

            // No-input has persisted through this whole sample window — attribute it as idle time.
            // (idleSeconds is time-since-last-input, not a per-window delta, so this undercounts the
            // first window crossing the threshold and is the simplest correct approximation.)
            if (isIdle)
                _dayMetrics.AddIdleSample(SampleWindow);

            double? latitude = null, longitude = null, accuracyMeters = null;
            _tickCount++;
            if (_locationTrackingEnabled && _tickCount % LocationFixEveryNthTick == 0)
            {
                // Isolated from the outer try: a location fix is a best-effort add-on to this
                // sample. If ILocationService throws (rather than returning a failure result), the
                // idle/active telemetry for this tick must still be submitted below.
                try
                {
                    var result = await _location.GetCurrentAsync(ct);
                    if (result.IsSuccess)
                    {
                        latitude = result.Fix!.Latitude;
                        longitude = result.Fix.Longitude;
                        accuracyMeters = result.Fix.AccuracyMeters;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogDebug(ex, "{Name}: location fix failed, submitting snapshot without coordinates", Name);
                }
            }

            var record = new CollectionRecord
            {
                EventId          = Guid.NewGuid().ToString("N"),
                RecordType       = CollectionRecordTypes.DeviceStateSnapshot,
                SchemaVersion    = CollectionSchemaVersions.DeviceStateSnapshotV1,
                CaptureTimestamp = now,
                DeviceId         = Environment.MachineName,
                Payload          = JsonSerializer.SerializeToElement(new DeviceStateSnapshotPayload
                {
                    CapturedAt     = now,
                    IdleSeconds    = idleSeconds,
                    IsIdle         = isIdle,
                    Latitude       = latitude,
                    Longitude      = longitude,
                    AccuracyMeters = accuracyMeters
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
