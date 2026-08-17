namespace ONEVO.Agent.TrayApp.Collectors;

using System.Diagnostics;
using System.Text.Json;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Phase 1 probabilistic meeting detection via known process names (§7.4).
/// Process found ≠ actively in meeting; result is a hint, not proof.
/// </summary>
public sealed class MeetingDetector : IAgentCollector, IAsyncDisposable
{
    private static readonly HashSet<string> MeetingProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "teams",   "teams.exe",
            "zoom",    "zoom.exe",
            "webex",   "webex.exe",
            "slack",   "slack.exe",
            "msteams", "msteams.exe"
        };

    private static readonly TimeSpan SampleWindow = TimeSpan.FromMinutes(2);

    public string Name => "MeetingDetector";

    private readonly ILogger<MeetingDetector> _logger;
    private readonly INamedPipeClient _pipe;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;

    public MeetingDetector(ILogger<MeetingDetector> logger, INamedPipeClient pipe)
    {
        _logger = logger;
        _pipe = pipe;
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop = SampleLoopAsync(_cts.Token);
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
            await EmitSampleAsync(ct);
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
            var now = DateTimeOffset.UtcNow;
            var (isRunning, processName) = DetectMeetingProcess();

            var record = new CollectionRecord
            {
                EventId          = Guid.NewGuid().ToString("N"),
                RecordType       = CollectionRecordTypes.MeetingSignal,
                SchemaVersion    = CollectionSchemaVersions.MeetingSignalV1,
                CaptureTimestamp = now,
                DeviceId         = Environment.MachineName,
                Payload          = JsonSerializer.SerializeToElement(new MeetingSignalPayload
                {
                    CapturedAt          = now,
                    IsMeetingAppRunning = isRunning,
                    ProcessName         = processName
                })
            };
            await _pipe.SubmitCollectionRecordsAsync([record], ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "{Name}: emit failed", Name);
        }
    }

    /// <summary>
    /// Returns true if a known meeting-app process is running.
    /// Probabilistic — background process ≠ active meeting.
    /// </summary>
    public static bool IsMeetingAppRunning() => DetectMeetingProcess().IsRunning;

    private static (bool IsRunning, string? ProcessName) DetectMeetingProcess()
    {
        try
        {
            var match = Process.GetProcesses()
                .FirstOrDefault(p => MeetingProcessNames.Contains(p.ProcessName));
            return (match is not null, match?.ProcessName);
        }
        catch { return (false, null); }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
