namespace ONEVO.Agent.TrayApp.Collectors;

using System.Text;
using System.Text.Json;
using ONEVO.Agent.TrayApp.Interop;
using ONEVO.Agent.TrayApp.Security;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.Models;

public sealed class AppUsageCollector : IAgentCollector, IAsyncDisposable
{
    public string Name => "AppUsage";

    private readonly ILogger<AppUsageCollector> _logger;
    private readonly INamedPipeClient _pipe;
    private readonly ISessionDayMetrics _dayMetrics;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;
    private static readonly TimeSpan SampleWindow = TimeSpan.FromSeconds(60);

    public AppUsageCollector(
        ILogger<AppUsageCollector> logger,
        INamedPipeClient pipe,
        ISessionDayMetrics dayMetrics)
    {
        _logger     = logger;
        _pipe       = pipe;
        _dayMetrics = dayMetrics;
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        if (!policy.AppUsageEnabled || _running)
            return Task.CompletedTask;

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
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            string? processName = PrivacyScrubber.GetForegroundProcessNameSafe();
            if (!string.IsNullOrWhiteSpace(processName))
                _dayMetrics.AddAppUsageSample(processName, SampleWindow);

            // Hash window title in memory immediately — raw title is never stored or sent (§8.3)
            var buf = new StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, buf, buf.Capacity);
            var rawTitle = buf.ToString();
            buf.Clear();
            var titleHash = rawTitle.Length > 0 ? HashingService.HashWindowTitle(rawTitle) : string.Empty;

            var record = new CollectionRecord
            {
                EventId          = Guid.NewGuid().ToString("N"),
                RecordType       = CollectionRecordTypes.AppUsageSnapshot,
                SchemaVersion    = CollectionSchemaVersions.AppUsageSnapshotV1,
                CaptureTimestamp = DateTimeOffset.UtcNow,
                DeviceId         = Environment.MachineName,
                Payload          = JsonSerializer.SerializeToElement(new AppUsageSnapshotPayload
                {
                    CapturedAt      = DateTimeOffset.UtcNow,
                    ProcessName     = processName,
                    WindowTitleHash = titleHash
                })
            };

            await _pipe.SubmitCollectionRecordsAsync([record], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "{Name}: sample failed", Name);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
