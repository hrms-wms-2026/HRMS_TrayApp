namespace ONEVO.Agent.TrayApp.Collectors;

using System.Text;
using System.Text.Json;
using ONEVO.Agent.TrayApp.Interop;
using ONEVO.Agent.TrayApp.Security;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.Models;

/// <summary>Foreground window observation. The title is already hashed — raw text never leaves the probe.</summary>
internal readonly record struct ForegroundApp(IntPtr Hwnd, string? ProcessName, string TitleHash);

/// <summary>
/// Polls the foreground app every <see cref="DefaultPollInterval"/> so app switches are detected
/// as they happen, accumulates per-app time locally in an <see cref="AppUsageWindow"/>, and emits
/// one snapshot (the dominant app) per <see cref="DefaultSampleWindow"/>.
/// </summary>
public sealed class AppUsageCollector : IAgentCollector, IAsyncDisposable
{
    public string Name => "AppUsage";

    internal static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan DefaultSampleWindow = TimeSpan.FromSeconds(60);

    private readonly ILogger<AppUsageCollector> _logger;
    private readonly INamedPipeClient _pipe;
    private readonly ISessionDayMetrics _dayMetrics;
    private readonly IAppIconCache _iconCache;
    private readonly Func<ForegroundApp?> _probe;
    private readonly TimeSpan _pollInterval;
    private readonly int _pollsPerWindow;
    private readonly AppUsageWindow _window = new();
    private int _pollsInWindow;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;

    public AppUsageCollector(
        ILogger<AppUsageCollector> logger,
        INamedPipeClient pipe,
        ISessionDayMetrics dayMetrics,
        IAppIconCache iconCache)
        : this(logger, pipe, dayMetrics, iconCache, ProbeForeground, DefaultPollInterval, DefaultSampleWindow)
    {
    }

    internal AppUsageCollector(
        ILogger<AppUsageCollector> logger,
        INamedPipeClient pipe,
        ISessionDayMetrics dayMetrics,
        IAppIconCache iconCache,
        Func<ForegroundApp?> probe,
        TimeSpan pollInterval,
        TimeSpan sampleWindow)
    {
        _logger         = logger;
        _pipe           = pipe;
        _dayMetrics     = dayMetrics;
        _iconCache      = iconCache;
        _probe          = probe;
        _pollInterval   = pollInterval;
        _pollsPerWindow = Math.Max(1, (int)Math.Round(sampleWindow / pollInterval));
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        if (!policy.AppUsageEnabled || _running)
            return Task.CompletedTask;

        _cts     = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop    = PollLoopAsync(_cts.Token);
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

        // A partial window is dropped rather than emitted: collection has stopped (break,
        // clock-out, lock, IPC loss) and the backend would count the sample as a full minute.
        _window.Complete();
        _pollsInWindow = 0;
        _logger.LogInformation("{Name}: stopped", Name);
    }

    private async Task PollLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(_pollInterval);
            while (await timer.WaitForNextTickAsync(ct))
                await PollAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Test-only synchronous hook into one poll tick, bypassing the real PeriodicTimer.</summary>
    internal Task PollForTestAsync(CancellationToken ct) => PollAsync(ct);

    private async Task PollAsync(CancellationToken ct)
    {
        try
        {
            var app = _probe();
            if (app is { } current)
            {
                var switched = _window.Observe(current.ProcessName, current.TitleHash, _pollInterval);
                if (switched && !string.IsNullOrWhiteSpace(current.ProcessName))
                {
                    _logger.LogDebug("{Name}: foreground app changed to {Process}", Name, current.ProcessName);
                    _iconCache.TryCacheFromForegroundWindow(current.Hwnd, current.ProcessName);
                }
            }

            if (++_pollsInWindow >= _pollsPerWindow)
            {
                _pollsInWindow = 0;
                await EmitWindowAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "{Name}: poll failed", Name);
        }
    }

    private async Task EmitWindowAsync(CancellationToken ct)
    {
        var result = _window.Complete();
        if (result is null) return;

        foreach (var (processName, duration) in result.ProcessDurations)
            _dayMetrics.AddAppUsageSample(processName, duration);

        var now = DateTimeOffset.UtcNow;
        var record = new CollectionRecord
        {
            EventId          = Guid.NewGuid().ToString("N"),
            RecordType       = CollectionRecordTypes.AppUsageSnapshot,
            SchemaVersion    = CollectionSchemaVersions.AppUsageSnapshotV1,
            CaptureTimestamp = now,
            DeviceId         = Environment.MachineName,
            Payload          = JsonSerializer.SerializeToElement(new AppUsageSnapshotPayload
            {
                CapturedAt      = now,
                ProcessName     = result.DominantProcessName,
                WindowTitleHash = result.DominantTitleHash
            })
        };

        await _pipe.SubmitCollectionRecordsAsync([record], ct);
    }

    private static ForegroundApp? ProbeForeground()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        var processName = PrivacyScrubber.GetForegroundProcessNameSafe();

        // Hash window title in memory immediately — raw title is never stored or sent (§8.3)
        var buf = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, buf, buf.Capacity);
        var rawTitle = buf.ToString();
        buf.Clear();
        var titleHash = rawTitle.Length > 0 ? HashingService.HashWindowTitle(rawTitle) : string.Empty;

        return new ForegroundApp(hwnd, processName, titleHash);
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
