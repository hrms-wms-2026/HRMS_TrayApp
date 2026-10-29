namespace ONEVO.Agent.TrayApp.Collectors;

using System.Runtime.InteropServices;
using System.Text.Json;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Interop;
using ONEVO.Agent.TrayApp.Security;
using ONEVO.Agent.TrayApp.Services;

/// <summary>
/// Counts keyboard and mouse events only via low-level hooks (§7.1).
/// Never stores key codes, characters, coordinates, or clipboard.
/// Snapshots are handed to Service over IPC — never uploaded directly.
/// </summary>
public sealed class ActivityCountCollector : IAgentCollector, IAsyncDisposable
{
    public string Name => "ActivityCount";

    /// <summary>How often last-input time is sampled for idle accounting.</summary>
    internal static readonly TimeSpan SamplePeriod = TimeSpan.FromSeconds(5);

    private readonly ILogger<ActivityCountCollector> _logger;
    private readonly Func<IReadOnlyList<CollectionRecord>, CancellationToken, Task> _submit;
    private readonly Func<long> _clockMs;
    private readonly Func<int> _secondsSinceLastInput;
    private readonly string _deviceId;
    private readonly object _trackerLock = new();
    private ActivityIntervalTracker _tracker;

    private readonly object _counterLock = new();
    private long _keyboardCount;
    private long _mouseCount;

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private NativeMethods.LowLevelProc? _keyboardProc;
    private NativeMethods.LowLevelProc? _mouseProc;

    private CancellationTokenSource? _runCts;
    private Task? _snapshotLoop;
    private int _intervalSeconds = Constants.DefaultActivitySnapshotIntervalSeconds;
    private bool _running;

    // Keep delegates alive so GC does not collect them while hooks are set.
    // (Hook callbacks must remain rooted for the lifetime of SetWindowsHookEx.)

    public ActivityCountCollector(
        ILogger<ActivityCountCollector> logger,
        NamedPipeClient pipeClient)
        : this(logger, pipeClient.SubmitCollectionRecordsAsync,
            () => Environment.TickCount64, PrivacyScrubber.GetSecondsSinceLastInput)
    {
    }

    internal ActivityCountCollector(
        ILogger<ActivityCountCollector> logger,
        Func<IReadOnlyList<CollectionRecord>, CancellationToken, Task> submit,
        Func<long> clockMs,
        Func<int> secondsSinceLastInput)
    {
        _logger = logger;
        _submit = submit;
        _clockMs = clockMs;
        _secondsSinceLastInput = secondsSinceLastInput;
        _deviceId = Environment.MachineName;
        _tracker = NewTracker();
    }

    // Idle rule kept from the original heuristic: no input for at least half the interval.
    private ActivityIntervalTracker NewTracker() =>
        new(_intervalSeconds, idleThresholdSeconds: _intervalSeconds / 2, _clockMs());

    public Task StartAsync(AgentPolicy policy, CancellationToken cancellationToken)
    {
        if (!policy.ActivitySignalEnabled)
        {
            _logger.LogInformation("{Name}: policy disabled — not starting", Name);
            return Task.CompletedTask;
        }

        if (_running)
            return Task.CompletedTask;

        _intervalSeconds = Math.Clamp(
            Constants.DefaultActivitySnapshotIntervalSeconds, 15, 300);

        InstallHooks();
        lock (_trackerLock)
            _tracker = NewTracker();
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _snapshotLoop = SnapshotLoopAsync(_runCts.Token);
        _running = true;
        _logger.LogInformation("{Name}: started (interval={Seconds}s)", Name, _intervalSeconds);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_running)
            return;

        _running = false;

        if (_runCts is not null)
        {
            await _runCts.CancelAsync();
            try
            {
                if (_snapshotLoop is not null)
                    await _snapshotLoop.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException or TaskCanceledException)
            {
                // best-effort stop
            }
            _runCts.Dispose();
            _runCts = null;
            _snapshotLoop = null;
        }

        // Final snapshot before unhook: covers only the time since the last snapshot (ISS-014).
        try
        {
            await EmitSnapshotAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "{Name}: final snapshot emit failed", Name);
        }

        UninstallHooks();
        ResetCounters();
        _logger.LogInformation("{Name}: stopped", Name);
    }

    private void InstallHooks()
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;

        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule
            ?? throw new InvalidOperationException("Cannot resolve main module for hooks");
        var hMod = NativeMethods.GetModuleHandle(curModule.ModuleName);

        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
        if (_keyboardHook == IntPtr.Zero)
            throw new InvalidOperationException(
                $"SetWindowsHookEx(WH_KEYBOARD_LL) failed: {Marshal.GetLastWin32Error()}");

        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _mouseProc, hMod, 0);
        if (_mouseHook == IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
            throw new InvalidOperationException(
                $"SetWindowsHookEx(WH_MOUSE_LL) failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private void UninstallHooks()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
        _keyboardProc = null;
        _mouseProc = null;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Count only key-down; never read vkCode/scanCode from lParam.
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            if (msg is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                lock (_counterLock)
                {
                    if (_keyboardCount < Constants.MaxEventsPerInterval)
                        _keyboardCount++;
                }
            }
        }
        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Count clicks/wheel only — never read POINT from lParam.
        if (nCode >= 0)
        {
            var msg = wParam.ToInt32();
            if (msg is NativeMethods.WM_LBUTTONDOWN
                or NativeMethods.WM_RBUTTONDOWN
                or NativeMethods.WM_MBUTTONDOWN
                or NativeMethods.WM_MOUSEWHEEL
                or NativeMethods.WM_XBUTTONDOWN)
            {
                lock (_counterLock)
                {
                    if (_mouseCount < Constants.MaxEventsPerInterval)
                        _mouseCount++;
                }
            }
        }
        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private async Task SnapshotLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(SamplePeriod);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                await TickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
    }

    /// <summary>
    /// One sampling step; emits a snapshot once the interval has elapsed. Never throws except on
    /// cancellation, so a transient IPC failure cannot end the loop for good (ISS-017).
    /// </summary>
    internal async Task TickAsync(CancellationToken ct)
    {
        try
        {
            bool due;
            lock (_trackerLock)
            {
                var now = _clockMs();
                _tracker.Sample(now, _secondsSinceLastInput());
                due = _tracker.ElapsedMs(now) >= _intervalSeconds * 1000L;
            }

            if (due)
                await EmitSnapshotAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("{Name}: snapshot hand-off failed: {Message}", Name, ex.Message);
        }
    }

    internal async Task EmitSnapshotAsync(CancellationToken ct)
    {
        long keyboard;
        long mouse;
        lock (_counterLock)
        {
            keyboard = _keyboardCount;
            mouse = _mouseCount;
            // Reset only after we have taken the snapshot values (§7.1).
            _keyboardCount = 0;
            _mouseCount = 0;
        }

        int activeSeconds;
        int idleSeconds;
        lock (_trackerLock)
        {
            var now = _clockMs();
            _tracker.Sample(now, _secondsSinceLastInput());
            (activeSeconds, idleSeconds) = _tracker.Complete(now);
        }

        var totalSeconds = activeSeconds + idleSeconds;
        if (totalSeconds == 0)
            return; // nothing elapsed since the previous snapshot

        // Intensity 0–100 from event density over the time actually covered (counts only — no content).
        var eventDensity = (keyboard + mouse) / (double)totalSeconds;
        var intensity = Math.Round(
            (decimal)Math.Clamp(eventDensity / 5.0 * 100.0, 0, 100), 2);

        var processName = PrivacyScrubber.GetForegroundProcessNameSafe();

        var payload = new ActivitySnapshotPayload
        {
            CapturedAt = DateTimeOffset.UtcNow,
            KeyboardEventsCount = (int)Math.Min(keyboard, int.MaxValue),
            MouseEventsCount = (int)Math.Min(mouse, int.MaxValue),
            ActiveSeconds = activeSeconds,
            IdleSeconds = idleSeconds,
            IntensityScore = intensity,
            ForegroundProcessName = processName
        };

        var record = new CollectionRecord
        {
            EventId = Guid.NewGuid().ToString("N"),
            RecordType = CollectionRecordTypes.ActivitySnapshot,
            SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
            CaptureTimestamp = payload.CapturedAt,
            DeviceId = _deviceId,
            Payload = JsonSerializer.SerializeToElement(payload)
        };

        // Do not log counts or process name in production paths (privacy).
        _logger.LogDebug("{Name}: snapshot handed off eventId={EventId}", Name, record.EventId);

        await _submit([record], ct);
    }

    private void ResetCounters()
    {
        lock (_counterLock)
        {
            _keyboardCount = 0;
            _mouseCount = 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }
}
