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
    private static readonly string BootLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ONEVO", "Agent", "tray-boot.log");

    public string Name => "ActivityCount";

    /// <summary>Raised after each snapshot is handed to the Service (for UI status).</summary>
    public event Action<ActivitySnapshotPayload, int>? SnapshotEmitted;

    private readonly ILogger<ActivityCountCollector> _logger;
    private readonly NamedPipeClient _pipeClient;
    private readonly string _deviceId;

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

    private CancellationTokenSource? _runCts;
    private Task? _snapshotLoop;
    private int _intervalSeconds = Constants.DefaultActivitySnapshotIntervalSeconds;
    private bool _running;

    public ActivityCountCollector(
        ILogger<ActivityCountCollector> logger,
        NamedPipeClient pipeClient)
    {
        _logger = logger;
        _pipeClient = pipeClient;
        _deviceId = Environment.MachineName;
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken cancellationToken)
    {
        if (!policy.ActivitySignalEnabled)
        {
            BootLog("policy disabled — not starting");
            return Task.CompletedTask;
        }

        if (_running)
            return Task.CompletedTask;

        _intervalSeconds = Math.Clamp(
            Constants.DefaultActivitySnapshotIntervalSeconds, 60, 300);

        // Install hooks on the UI thread — LL hooks must live with a message pump.
        void Install()
        {
            InstallHooks();
            BootLog($"hooks installed on thread={Environment.CurrentManagedThreadId}");
        }

        if (Microsoft.Maui.ApplicationModel.MainThread.IsMainThread)
            Install();
        else
            Microsoft.Maui.ApplicationModel.MainThread.InvokeOnMainThreadAsync(Install).GetAwaiter().GetResult();

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _snapshotLoop = SnapshotLoopAsync(_runCts.Token);
        _running = true;
        BootLog($"started interval={_intervalSeconds}s");
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
        // First tick after a short delay so UI shows "working" quickly.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, _intervalSeconds)), ct);
            await EmitSnapshotAsync(ct);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_intervalSeconds));
            while (await timer.WaitForNextTickAsync(ct))
            {
                await EmitSnapshotAsync(ct);
            }
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

    private async Task EmitSnapshotAsync(CancellationToken ct)
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

        var interval = _intervalSeconds;
        var secondsSinceInput = PrivacyScrubber.GetSecondsSinceLastInput();
        var idleSeconds = secondsSinceInput >= interval / 2
            ? Math.Min(interval, secondsSinceInput)
            : 0;
        var activeSeconds = Math.Max(0, interval - idleSeconds);

        var eventDensity = (keyboard + mouse) / (double)Math.Max(1, interval);
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

        BootLog($"snapshot #{seq} kb={payload.KeyboardEventsCount} mouse={payload.MouseEventsCount}");
        _logger.LogInformation(
            "{Name}: snapshot #{Seq} kb={Kb} mouse={Mouse} eventId={EventId}",
            Name, seq, payload.KeyboardEventsCount, payload.MouseEventsCount, record.EventId);

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

    private static void BootLog(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BootLogPath)!);
            File.AppendAllText(BootLogPath, $"{DateTimeOffset.Now:O} [Activity] {message}{Environment.NewLine}");
        }
        catch
        {
            // ignore
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }
}
