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

    /// <summary>Raised after each snapshot is handed to the Service (for UI status).</summary>
    public event Action<ActivitySnapshotPayload, int>? SnapshotEmitted;

    /// <summary>How often last-input time is sampled for active/idle accounting.</summary>
    internal static readonly TimeSpan SamplePeriod = TimeSpan.FromSeconds(5);

    private readonly ILogger<ActivityCountCollector> _logger;
    private readonly NamedPipeClient _pipeClient;
    private readonly ISessionDayMetrics _dayMetrics;
    private readonly string _deviceId;
    private readonly Func<long> _clockMs;
    private readonly Func<int> _secondsSinceLastInput;
    private readonly object _trackerLock = new();
    private ActivityIntervalTracker _tracker;

    private readonly object _counterLock = new();
    private long _keyboardCount;
    private long _mouseCount;
    private int _snapshotSequence;

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    // KEEP ROOTS — GC must not collect these while hooks are installed.
    private NativeMethods.LowLevelProc? _keyboardProc;
    private NativeMethods.LowLevelProc? _mouseProc;
    private GCHandle _keyboardProcHandle;
    private GCHandle _mouseProcHandle;
    private Thread? _hookThread;
    private uint _hookThreadId;

    private CancellationTokenSource? _runCts;
    private Task? _snapshotLoop;
    private int _intervalSeconds = Constants.DefaultActivitySnapshotIntervalSeconds;
    private bool _running;

    public ActivityCountCollector(
        ILogger<ActivityCountCollector> logger,
        NamedPipeClient pipeClient,
        ISessionDayMetrics dayMetrics)
        : this(logger, pipeClient, dayMetrics,
            () => Environment.TickCount64, PrivacyScrubber.GetSecondsSinceLastInput)
    {
    }

    /// <summary>Testing seam: injects the monotonic clock and the seconds-since-last-input source.</summary>
    internal ActivityCountCollector(
        ILogger<ActivityCountCollector> logger,
        NamedPipeClient pipeClient,
        ISessionDayMetrics dayMetrics,
        Func<long> clockMs,
        Func<int> secondsSinceLastInput)
    {
        _logger = logger;
        _pipeClient = pipeClient;
        _dayMetrics = dayMetrics;
        _deviceId = Environment.MachineName;
        _clockMs = clockMs;
        _secondsSinceLastInput = secondsSinceLastInput;
        _tracker = NewTracker();
    }

    // Idle rule unchanged: no input for at least half the interval.
    private ActivityIntervalTracker NewTracker() =>
        new(_intervalSeconds, idleThresholdSeconds: _intervalSeconds / 2, _clockMs());

    public async Task StartAsync(AgentPolicy policy, CancellationToken cancellationToken)
    {
        if (!policy.ActivitySignalEnabled)
        {
            BootLog("policy disabled — not starting");
            return;
        }

        if (_running)
            return;

        _intervalSeconds = Math.Clamp(
            Constants.DefaultActivitySnapshotIntervalSeconds, 60, 300);

        // LL hooks run on whichever thread installed them, inside that thread's message loop —
        // and Windows holds EVERY keystroke/mouse event on the machine until the callback
        // returns. On the UI thread, any layout pass / animation frame / GC pause there turned
        // into system-wide input lag, so the hooks get their own thread that does nothing else.
        await StartHookThreadAsync();
        BootLog($"hooks installed on dedicated thread={_hookThreadId}");

        lock (_trackerLock)
            _tracker = NewTracker();

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _snapshotLoop = SnapshotLoopAsync(_runCts.Token);
        _running = true;
        BootLog($"started interval={_intervalSeconds}s");
        _logger.LogInformation("{Name}: started (interval={Seconds}s)", Name, _intervalSeconds);
    }

    private Task StartHookThreadAsync()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _hookThread = new Thread(() =>
        {
            try
            {
                InstallHooks();
                _hookThreadId = NativeMethods.GetCurrentThreadId();
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
                return;
            }

            ready.TrySetResult();

            // Hook callbacks are delivered from inside GetMessage; nothing else is ever posted
            // here except the WM_QUIT from StopHookThread (GetMessage returns 0 for it).
            while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
            {
            }

            UninstallHooks();
        })
        {
            IsBackground = true,
            Name = "ONEVO input-count hooks",
            // Input waits on this thread — never let busy app work starve it.
            Priority = ThreadPriority.AboveNormal
        };
        _hookThread.Start();
        return ready.Task;
    }

    private void StopHookThread()
    {
        var thread = _hookThread;
        _hookThread = null;
        if (thread is null)
            return;

        if (_hookThreadId != 0)
            NativeMethods.PostThreadMessage(_hookThreadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));
        _hookThreadId = 0;
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

        try
        {
            await EmitSnapshotAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "{Name}: final snapshot emit failed", Name);
        }

        StopHookThread();
        ResetCounters();
        BootLog("stopped");
        _logger.LogInformation("{Name}: stopped", Name);
    }

    private void InstallHooks()
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
        _keyboardProcHandle = GCHandle.Alloc(_keyboardProc);
        _mouseProcHandle = GCHandle.Alloc(_mouseProc);

        // LL hooks: module handle of the executable (null often works for LL hooks).
        var hMod = NativeMethods.GetModuleHandle(null);
        if (hMod == IntPtr.Zero)
        {
            using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
            hMod = NativeMethods.GetModuleHandle(curProcess.MainModule?.ModuleName);
        }

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

        BootLog("hooks installed");
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
        if (_keyboardProcHandle.IsAllocated) _keyboardProcHandle.Free();
        if (_mouseProcHandle.IsAllocated) _mouseProcHandle.Free();
        _keyboardProc = null;
        _mouseProc = null;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
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
        }
        catch
        {
            // never throw out of a hook callback
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
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
        }
        catch
        {
            // never throw out of a hook callback
        }

        return NativeMethods.CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private async Task SnapshotLoopAsync(CancellationToken ct)
    {
        // First snapshot after a short delay so UI shows "working" quickly; it covers only the
        // seconds that actually elapsed (UAT-01).
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, _intervalSeconds)), ct);
            await EmitSnapshotAsync(ct);

            using var timer = new PeriodicTimer(SamplePeriod);
            while (await timer.WaitForNextTickAsync(ct))
                await TickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
        catch (Exception ex)
        {
            BootLog($"snapshot loop error: {ex.Message}");
            _logger.LogError(ex, "{Name}: snapshot loop failed", Name);
        }
    }

    /// <summary>One sampling step; emits a snapshot once a full interval has elapsed.</summary>
    internal async Task TickAsync(CancellationToken ct)
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

    /// <summary>
    /// Emits a snapshot covering the time since the previous one: a partial interval (stop on
    /// break, lock or disconnect) reports only its real duration, and every no-input span of at
    /// least half the interval counts as idle — not just the one in progress at this instant (UAT-01).
    /// </summary>
    internal async Task EmitSnapshotAsync(CancellationToken ct)
    {
        long keyboard;
        long mouse;
        lock (_counterLock)
        {
            keyboard = _keyboardCount;
            mouse = _mouseCount;
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

        var coveredSeconds = activeSeconds + idleSeconds;
        if (coveredSeconds == 0)
            return; // nothing elapsed since the previous snapshot

        var eventDensity = (keyboard + mouse) / (double)coveredSeconds;
        var intensity = Math.Round(
            (decimal)Math.Clamp(eventDensity / 5.0 * 100.0, 0, 100), 2);

        // Foreground process lookup is best-effort; never fail the snapshot.
        string? processName = null;
        try { processName = PrivacyScrubber.GetForegroundProcessNameSafe(); }
        catch { /* ignore */ }

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

        var seq = Interlocked.Increment(ref _snapshotSequence);
        var record = new CollectionRecord
        {
            EventId = Guid.NewGuid().ToString("N"),
            RecordType = CollectionRecordTypes.ActivitySnapshot,
            SchemaVersion = CollectionSchemaVersions.ActivitySnapshotV1,
            CaptureTimestamp = payload.CapturedAt,
            DeviceId = _deviceId,
            Payload = JsonSerializer.SerializeToElement(payload)
        };

        _logger.LogInformation(
            "{Name}: snapshot #{Seq} kb={Kb} mouse={Mouse} eventId={EventId}",
            Name, seq, payload.KeyboardEventsCount, payload.MouseEventsCount, record.EventId);

        if (idleSeconds > 0)
            _dayMetrics.AddIdleSample(TimeSpan.FromSeconds(idleSeconds));

        try
        {
            await _pipeClient.SubmitCollectionRecordsAsync([record], ct);
            BootLog($"snapshot #{seq} submitted to IPC");
        }
        catch (Exception ex)
        {
            BootLog($"snapshot #{seq} IPC submit failed: {ex.Message}");
            _logger.LogWarning(ex, "{Name}: IPC submit failed for snapshot #{Seq}", Name, seq);
        }

        try
        {
            SnapshotEmitted?.Invoke(payload, seq);
        }
        catch (Exception ex)
        {
            BootLog($"snapshot #{seq} UI event failed: {ex.Message}");
        }
    }

    private void ResetCounters()
    {
        lock (_counterLock)
        {
            _keyboardCount = 0;
            _mouseCount = 0;
        }
    }

    private static void BootLog(string message) => TrayBootLog.Write("Activity", message);

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }
}
