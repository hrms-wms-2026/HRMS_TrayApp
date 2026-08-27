# TrayApp Demo Mode (Auto-Fallback, No Backend/Service/DB) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the TrayApp `.msix` fully runnable with zero external dependencies (no `ONEVO.Agent.Service`, no backend API, no database) by auto-falling-back to a scripted `DemoNamedPipeClient` whenever the real Service pipe can't be reached within a short grace window, while leaving real-Service behavior completely unchanged when it IS reachable.

**Architecture:** `INamedPipeClient` is the only seam through which the TrayApp talks to the Service/backend/database (confirmed by tracing every ViewModel). Add `DemoNamedPipeClient : INamedPipeClient` — a self-contained scripted state machine (any activation code succeeds, Clock In/Break/Clock Out drive a locally-computed `SessionSnapshot`). Add `AutoFallbackNamedPipeClient : INamedPipeClient, IAsyncDisposable` that starts the real `NamedPipeClient`, races it against a ~4s timer, and permanently commits this process run to whichever backend produces the first signal (or to Demo if neither does in time). Everything else in the app (ViewModels, `CollectorCoordinator`) already depends on `INamedPipeClient`, not the concrete class, so they need no changes. Only `App.xaml.cs` hardcodes the concrete `NamedPipeClient` type and must be repointed at `AutoFallbackNamedPipeClient`. Local activity collectors (app usage, idle time) already write straight to `ISessionDayMetrics`, not through the pipe, so End Session summaries in Demo Mode show genuine on-device data, not fake numbers.

**Tech Stack:** C# / .NET 10, .NET MAUI (Windows), xUnit, `Microsoft.Extensions.Logging.Abstractions` (`NullLogger<T>`), existing `build-msix.ps1` (unchanged).

---

## Why This Works With Almost No New Code

| Concern | Where it lives | Needs a change? |
|---|---|---|
| Talking to Service/backend | `INamedPipeClient` implementations only | Yes — new fallback-aware implementation |
| Connect Workspace / activation | `ConnectWorkspaceViewModel` (already `INamedPipeClient`-only) | No |
| Prepare Workspace, Work Location, Review Setup | Local timers / GPS / `Preferences` only | No |
| Clock In / Active Session / Break / Clock Out | `INamedPipeClient` only | No |
| End Session summary (top apps, idle) | `ISessionDayMetrics`, written directly by collectors | No |
| Collector start/stop gating | `CollectorCoordinator` (already `INamedPipeClient`-only, has its own local-default policy fallback) | No |
| App boot / tray icon / navigation routing | `App.xaml.cs` — **hardcodes concrete `NamedPipeClient`** | **Yes** — repoint to `AutoFallbackNamedPipeClient` |
| DI wiring | `MauiProgram.cs` | Yes — register the two new types |
| MSIX packaging | `build-msix.ps1` | No — same script, same single build |

**Explicitly out of scope:** The biometric/AWS Face Liveness enrollment screen (`enrollment-biometric` route) is not reachable from any current navigation flow (confirmed by grep — nothing calls `GoToAsync("//enrollment-biometric")` or `"enrollment-biometric"`). Demo Mode returns `null` from `StartBiometricEnrollmentAsync`/`CompleteBiometricEnrollmentAsync`, which `BiometricEnrollmentViewModel` already handles by showing an error — no worse than today, and unreachable either way.

---

## File Map

| File | Action | Responsibility |
|---|---|---|
| `ONEVO.Agent.TrayApp/Services/DemoNamedPipeClient.cs` | **Create** | Scripted `INamedPipeClient` — always-succeeds activation, local Clock In/Break/Clock Out state machine |
| `ONEVO.Agent.TrayApp/Services/AutoFallbackNamedPipeClient.cs` | **Create** | Races real vs. grace-window timeout, commits once, delegates every call to the winner |
| `ONEVO.Agent.TrayApp/MauiProgram.cs` | **Modify** | Register `DemoNamedPipeClient` + `AutoFallbackNamedPipeClient`, point `INamedPipeClient` at the fallback wrapper |
| `ONEVO.Agent.TrayApp/App.xaml.cs` | **Modify** | Constructor/field type `NamedPipeClient` → `AutoFallbackNamedPipeClient` |
| `tests/ONEVO.Agent.TrayApp.Tests/Services/DemoNamedPipeClientTests.cs` | **Create** | Unit tests for the scripted state machine |
| `tests/ONEVO.Agent.TrayApp.Tests/Services/AutoFallbackNamedPipeClientTests.cs` | **Create** | Unit tests for the fallback race |

---

## Task 1: `DemoNamedPipeClient`

**Files:**
- Create: `ONEVO.Agent.TrayApp/Services/DemoNamedPipeClient.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/Services/DemoNamedPipeClientTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

public sealed class DemoNamedPipeClientTests
{
    [Fact]
    public async Task StartAsync_PublishesUnenrolledStateAndPolicy()
    {
        var sut = new DemoNamedPipeClient();
        MonitoringState? state = null;
        AgentPolicy? policy = null;
        sut.OnStateReceived += s => state = s;
        sut.OnPolicyReceived += p => policy = p;

        await sut.StartAsync(CancellationToken.None);

        Assert.Equal(MonitoringState.Unenrolled, state);
        Assert.NotNull(policy);
        Assert.True(policy!.ActivitySignalEnabled);
    }

    [Fact]
    public async Task SendActivationAsync_AlwaysSucceedsWithDemoIdentity()
    {
        var sut = new DemoNamedPipeClient();

        var result = await sut.SendActivationAsync("ANYCODE", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("Demo Employee", result.EmployeeName);
        Assert.Equal("demo.employee@onexsoworkspace.com", result.EmployeeEmail);
    }

    [Fact]
    public async Task ClockIn_ThenClockOut_ComputesWorkAndFiresEventsInOrder()
    {
        var sut = new DemoNamedPipeClient();
        await sut.StartAsync(CancellationToken.None);
        await sut.SendActivationAsync("ANYCODE", CancellationToken.None);

        var order = new List<string>();
        sut.OnStatusReceived += _ => order.Add("status");
        sut.OnStateReceived  += _ => order.Add("state");

        var clockIn = await sut.SendLifecycleAsync(LifecycleAction.ClockIn, CancellationToken.None);
        Assert.True(clockIn!.Success);
        Assert.Equal(MonitoringState.Active, clockIn.State);
        Assert.NotNull(clockIn.Session?.ClockInAt);

        var clockOut = await sut.SendLifecycleAsync(LifecycleAction.ClockOut, CancellationToken.None);
        Assert.True(clockOut!.Success);
        Assert.Equal(MonitoringState.Stopped, clockOut.State);
        Assert.NotNull(clockOut.Session?.ClockOutAt);

        Assert.Equal(new[] { "status", "state", "status", "state" }, order);
    }

    [Fact]
    public async Task StartBreak_ThenEndBreak_AccumulatesBreakTimeAndCount()
    {
        var sut = new DemoNamedPipeClient();
        await sut.StartAsync(CancellationToken.None);
        await sut.SendActivationAsync("ANYCODE", CancellationToken.None);
        await sut.SendLifecycleAsync(LifecycleAction.ClockIn, CancellationToken.None);

        var startBreak = await sut.SendLifecycleAsync(LifecycleAction.StartBreak, CancellationToken.None);
        Assert.True(startBreak!.Success);
        Assert.Equal(MonitoringState.Paused, startBreak.State);
        Assert.True(startBreak.Session!.IsOnBreak);

        await Task.Delay(20);

        var endBreak = await sut.SendLifecycleAsync(LifecycleAction.EndBreak, CancellationToken.None);
        Assert.True(endBreak!.Success);
        Assert.Equal(MonitoringState.Active, endBreak.State);
        Assert.False(endBreak.Session!.IsOnBreak);
        Assert.Equal(1, endBreak.Session.BreakSessionCount);
        Assert.True(endBreak.Session.AccumulatedBreak > TimeSpan.Zero);
    }

    [Fact]
    public async Task SendEnvelopeAsync_StatusRequest_RepublishesCurrentStatus()
    {
        var sut = new DemoNamedPipeClient();
        await sut.StartAsync(CancellationToken.None);
        await sut.SendActivationAsync("ANYCODE", CancellationToken.None);
        await sut.SendLifecycleAsync(LifecycleAction.ClockIn, CancellationToken.None);

        StatusResponsePayload? received = null;
        sut.OnStatusReceived += s => received = s;

        await sut.SendEnvelopeAsync(new IpcEnvelope { Type = IpcMessageTypes.StatusRequest }, CancellationToken.None);

        Assert.NotNull(received);
        Assert.Equal(MonitoringState.Active, received!.State);
    }

    [Fact]
    public async Task SendLogoutAsync_ResetsToUnenrolled_AndClockInWorksAgain()
    {
        var sut = new DemoNamedPipeClient();
        await sut.StartAsync(CancellationToken.None);
        await sut.SendActivationAsync("ANYCODE", CancellationToken.None);
        await sut.SendLifecycleAsync(LifecycleAction.ClockIn, CancellationToken.None);

        var logout = await sut.SendLogoutAsync(CancellationToken.None);
        Assert.True(logout!.Success);

        var clockIn = await sut.SendLifecycleAsync(LifecycleAction.ClockIn, CancellationToken.None);
        Assert.True(clockIn!.Success);
        Assert.NotNull(clockIn.Session?.ClockInAt);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail to build**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter DemoNamedPipeClientTests`
Expected: Build error `CS0246: The type or namespace name 'DemoNamedPipeClient' could not be found`

- [ ] **Step 3: Create `DemoNamedPipeClient`**

```csharp
namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Scripted, self-contained stand-in for <see cref="NamedPipeClient"/>, used when the OneXso Agent
/// Service can't be reached (see <see cref="AutoFallbackNamedPipeClient"/>). Every activation code
/// succeeds, and lifecycle actions (Clock In / Break / Clock Out) drive a locally-computed
/// <see cref="SessionSnapshot"/> so the rest of the app — which only ever talks to
/// <see cref="INamedPipeClient"/> — behaves exactly as it would against a real Service, with no
/// backend or database involved. Local activity collectors are untouched by this class: they already
/// write straight to <c>ISessionDayMetrics</c>, so End Session summaries still show genuine on-device
/// activity, not fake numbers.
/// </summary>
public sealed class DemoNamedPipeClient : INamedPipeClient
{
    private readonly object _gate = new();
    private MonitoringState _state = MonitoringState.Unenrolled;
    private DateTimeOffset? _clockInAt;
    private DateTimeOffset? _clockOutAt;
    private DateTimeOffset? _currentBreakStartedAt;
    private TimeSpan _accumulatedBreak = TimeSpan.Zero;
    private int _breakSessionCount;

    private static readonly AgentPolicy DemoPolicy = new()
    {
        Version = "demo-mode",
        ActivitySignalEnabled = true,
        AppUsageEnabled = true,
        ScreenshotEnabled = false,
        CameraVerificationEnabled = false,
        InactivityScreenshotEnabled = false,
        ValidUntil = DateTimeOffset.UtcNow.AddDays(1)
    };

    public event Action? OnDisconnected;
    public event Action<MonitoringState>? OnStateReceived;
    public event Action<StatusResponsePayload>? OnStatusReceived;
    public event Action<AgentPolicy>? OnPolicyReceived;

    public StatusResponsePayload? LastKnownStatus { get; private set; }
    public AgentPolicy? LastKnownPolicy { get; private set; }

    public Task StartAsync(CancellationToken ct)
    {
        LastKnownPolicy = DemoPolicy;
        OnPolicyReceived?.Invoke(DemoPolicy);
        PublishStatus();
        return Task.CompletedTask;
    }

    public Task SubmitCollectionRecordsAsync(IReadOnlyList<CollectionRecord> records, CancellationToken ct) =>
        Task.CompletedTask;

    public Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct)
    {
        if (envelope.Type == IpcMessageTypes.StatusRequest)
            PublishStatus();
        return Task.CompletedTask;
    }

    public Task<EnrollmentResultPayload?> SendActivationAsync(string code, CancellationToken ct)
    {
        lock (_gate)
            _state = MonitoringState.Stopped;

        return Task.FromResult<EnrollmentResultPayload?>(new EnrollmentResultPayload
        {
            Success = true,
            EmployeeName = "Demo Employee",
            EmployeeEmail = "demo.employee@onexsoworkspace.com",
            EmployeeNumber = "DEMO-0001"
        });
    }

    public Task<LogoutResultPayload?> SendLogoutAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            _state = MonitoringState.Unenrolled;
            _clockInAt = null;
            _clockOutAt = null;
            _currentBreakStartedAt = null;
            _accumulatedBreak = TimeSpan.Zero;
            _breakSessionCount = 0;
        }

        PublishStatus();
        return Task.FromResult<LogoutResultPayload?>(new LogoutResultPayload(true, null));
    }

    public Task<LifecycleResultPayload?> SendLifecycleAsync(
        LifecycleAction action, CancellationToken ct, string? breakReason = null)
    {
        LifecycleResultPayload result;
        lock (_gate)
        {
            result = action switch
            {
                LifecycleAction.ClockIn    => ClockIn(),
                LifecycleAction.StartBreak => StartBreak(),
                LifecycleAction.EndBreak   => EndBreak(),
                LifecycleAction.ClockOut   => ClockOut(),
                _ => new LifecycleResultPayload(false, "UNKNOWN_ACTION", "Unsupported action.", _state, BuildSession())
            };
        }

        if (result.Success)
        {
            var status = new StatusResponsePayload(result.State, DateTimeOffset.UtcNow, result.Session);
            LastKnownStatus = status;
            OnStatusReceived?.Invoke(status);
            OnStateReceived?.Invoke(result.State);
        }

        return Task.FromResult<LifecycleResultPayload?>(result);
    }

    public Task<BiometricEnrollmentSessionReadyPayload?> StartBiometricEnrollmentAsync(CancellationToken ct) =>
        Task.FromResult<BiometricEnrollmentSessionReadyPayload?>(null);

    public Task<BiometricEnrollmentResultPayload?> CompleteBiometricEnrollmentAsync(
        Guid attemptId, bool captureSucceeded, string? clientErrorCode, CancellationToken ct) =>
        Task.FromResult<BiometricEnrollmentResultPayload?>(null);

    private LifecycleResultPayload ClockIn()
    {
        if (_state == MonitoringState.Active)
            return new LifecycleResultPayload(false, "ALREADY_ACTIVE", "Already clocked in.", _state, BuildSession());

        _clockInAt = DateTimeOffset.UtcNow;
        _clockOutAt = null;
        _currentBreakStartedAt = null;
        _accumulatedBreak = TimeSpan.Zero;
        _breakSessionCount = 0;
        _state = MonitoringState.Active;
        return new LifecycleResultPayload(true, null, "Clocked in.", _state, BuildSession());
    }

    private LifecycleResultPayload StartBreak()
    {
        if (_state != MonitoringState.Active)
            return new LifecycleResultPayload(false, "NOT_CLOCKED_IN", "No active work session.", _state, BuildSession());

        _currentBreakStartedAt = DateTimeOffset.UtcNow;
        _state = MonitoringState.Paused;
        return new LifecycleResultPayload(true, null, "Break started.", _state, BuildSession());
    }

    private LifecycleResultPayload EndBreak()
    {
        if (_state != MonitoringState.Paused || _currentBreakStartedAt is null)
            return new LifecycleResultPayload(false, "NOT_ON_BREAK", "No break in progress.", _state, BuildSession());

        _accumulatedBreak += DateTimeOffset.UtcNow - _currentBreakStartedAt.Value;
        _currentBreakStartedAt = null;
        _breakSessionCount++;
        _state = MonitoringState.Active;
        return new LifecycleResultPayload(true, null, "Break ended.", _state, BuildSession());
    }

    private LifecycleResultPayload ClockOut()
    {
        if (_clockInAt is null)
            return new LifecycleResultPayload(false, "NO_ACTIVE_SESSION", "No active work session.", _state, BuildSession());

        if (_state == MonitoringState.Paused && _currentBreakStartedAt is not null)
        {
            _accumulatedBreak += DateTimeOffset.UtcNow - _currentBreakStartedAt.Value;
            _currentBreakStartedAt = null;
            _breakSessionCount++;
        }

        _clockOutAt = DateTimeOffset.UtcNow;
        _state = MonitoringState.Stopped;
        return new LifecycleResultPayload(true, null, "Clocked out.", _state, BuildSession());
    }

    private SessionSnapshot BuildSession()
    {
        var work = _clockInAt is null
            ? TimeSpan.Zero
            : (_clockOutAt ?? DateTimeOffset.UtcNow) - _clockInAt.Value - _accumulatedBreak;
        if (work < TimeSpan.Zero) work = TimeSpan.Zero;

        return new SessionSnapshot(
            ClockInAt: _clockInAt,
            ClockOutAt: _clockOutAt,
            IsOnBreak: _state == MonitoringState.Paused,
            CurrentBreakStartedAt: _currentBreakStartedAt,
            AccumulatedBreak: _accumulatedBreak,
            AccumulatedWork: work,
            ScheduleDisplay: "09:00 AM – 06:00 PM",
            BreakSessionCount: _breakSessionCount);
    }

    private void PublishStatus()
    {
        StatusResponsePayload status;
        lock (_gate)
            status = new StatusResponsePayload(_state, DateTimeOffset.UtcNow, _clockInAt is null ? null : BuildSession());

        LastKnownStatus = status;
        OnStatusReceived?.Invoke(status);
        OnStateReceived?.Invoke(status.State);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter DemoNamedPipeClientTests`
Expected: PASS (6 tests)

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/DemoNamedPipeClient.cs tests/ONEVO.Agent.TrayApp.Tests/Services/DemoNamedPipeClientTests.cs
git commit -m "feat(trayapp): add scripted DemoNamedPipeClient for backend-free operation"
```

---

## Task 2: `AutoFallbackNamedPipeClient`

**Files:**
- Create: `ONEVO.Agent.TrayApp/Services/AutoFallbackNamedPipeClient.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/Services/AutoFallbackNamedPipeClientTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
namespace ONEVO.Agent.TrayApp.Tests.Services;

using Microsoft.Extensions.Logging.Abstractions;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;

public sealed class AutoFallbackNamedPipeClientTests
{
    private static AutoFallbackNamedPipeClient CreateSut(TimeSpan graceWindow) =>
        new(
            new NamedPipeClient(NullLogger<NamedPipeClient>.Instance),
            new DemoNamedPipeClient(),
            NullLogger<AutoFallbackNamedPipeClient>.Instance,
            graceWindow);

    [Fact]
    public async Task FallsBackToDemoWhenServiceUnreachableWithinGraceWindow()
    {
        var sut = CreateSut(TimeSpan.FromMilliseconds(50));
        var states = new List<MonitoringState>();
        sut.OnStateReceived += states.Add;

        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(300);

        Assert.Contains(MonitoringState.Unenrolled, states);
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task DemoActivationSucceedsAfterFallback()
    {
        var sut = CreateSut(TimeSpan.FromMilliseconds(50));
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(300);

        var result = await sut.SendActivationAsync("ABCDEF", CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal("Demo Employee", result.EmployeeName);
        await sut.DisposeAsync();
    }

    [Fact]
    public async Task DemoClockInProducesActiveSessionAfterFallback()
    {
        var sut = CreateSut(TimeSpan.FromMilliseconds(50));
        await sut.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await sut.SendActivationAsync("ABCDEF", CancellationToken.None);

        var result = await sut.SendLifecycleAsync(LifecycleAction.ClockIn, CancellationToken.None);

        Assert.NotNull(result);
        Assert.True(result!.Success);
        Assert.Equal(MonitoringState.Active, result!.State);
        Assert.NotNull(result.Session?.ClockInAt);
        await sut.DisposeAsync();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail to build**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter AutoFallbackNamedPipeClientTests`
Expected: Build error `CS0246: The type or namespace name 'AutoFallbackNamedPipeClient' could not be found`

- [ ] **Step 3: Create `AutoFallbackNamedPipeClient`**

```csharp
namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Tries the real Service pipe first; if it hasn't produced any status/state/policy within
/// <c>graceWindow</c> (default 4s), or disconnects before ever producing one, this process
/// permanently switches to <see cref="DemoNamedPipeClient"/> — the TrayApp keeps working with no
/// Service, backend, or database at all. Once a backend is chosen for a run it is not switched
/// again: a real Service that connects and later drops is a genuine disconnect (reported as-is, no
/// silent fallback mid-shift); a real Service that was never reachable within the grace window hands
/// off to Demo Mode once, at startup.
/// </summary>
public sealed class AutoFallbackNamedPipeClient : INamedPipeClient, IAsyncDisposable
{
    private readonly NamedPipeClient _real;
    private readonly DemoNamedPipeClient _demo;
    private readonly ILogger<AutoFallbackNamedPipeClient> _logger;
    private readonly TimeSpan _graceWindow;
    private readonly object _gate = new();
    private readonly TaskCompletionSource<INamedPipeClient> _activeTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _decided;
    private CancellationTokenSource? _graceCts;

    public event Action? OnDisconnected;
    public event Action<MonitoringState>? OnStateReceived;
    public event Action<StatusResponsePayload>? OnStatusReceived;
    public event Action<AgentPolicy>? OnPolicyReceived;

    public StatusResponsePayload? LastKnownStatus =>
        _activeTcs.Task.IsCompletedSuccessfully ? _activeTcs.Task.Result.LastKnownStatus : null;

    public AgentPolicy? LastKnownPolicy =>
        _activeTcs.Task.IsCompletedSuccessfully ? _activeTcs.Task.Result.LastKnownPolicy : null;

    public AutoFallbackNamedPipeClient(
        NamedPipeClient real,
        DemoNamedPipeClient demo,
        ILogger<AutoFallbackNamedPipeClient> logger,
        TimeSpan? graceWindow = null)
    {
        _real = real;
        _demo = demo;
        _logger = logger;
        _graceWindow = graceWindow ?? TimeSpan.FromSeconds(4);
    }

    public Task StartAsync(CancellationToken ct)
    {
        _real.OnStatusReceived += HandleRealStatus;
        _real.OnStateReceived  += HandleRealState;
        _real.OnPolicyReceived += HandleRealPolicy;
        _real.OnDisconnected   += HandleRealDisconnected;

        _ = _real.StartAsync(ct);

        _graceCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var graceToken = _graceCts.Token;
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(_graceWindow, graceToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            SwitchToDemo();
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    private void HandleRealStatus(StatusResponsePayload s)  { CommitToReal(); OnStatusReceived?.Invoke(s); }
    private void HandleRealState(MonitoringState st)        { CommitToReal(); OnStateReceived?.Invoke(st); }
    private void HandleRealPolicy(AgentPolicy p)            { CommitToReal(); OnPolicyReceived?.Invoke(p); }

    private void HandleRealDisconnected()
    {
        bool wasReal;
        lock (_gate)
            wasReal = _decided
                && _activeTcs.Task.IsCompletedSuccessfully
                && ReferenceEquals(_activeTcs.Task.Result, _real);

        if (wasReal)
        {
            OnDisconnected?.Invoke();
            return;
        }

        SwitchToDemo();
    }

    private void CommitToReal()
    {
        lock (_gate)
        {
            if (_decided) return;
            _decided = true;
            _graceCts?.Cancel();
        }

        _logger.LogInformation("AutoFallback: OneXso Agent Service connected — using real pipe");
        _activeTcs.TrySetResult(_real);
    }

    private void SwitchToDemo()
    {
        lock (_gate)
        {
            if (_decided) return;
            _decided = true;
        }

        _logger.LogInformation(
            "AutoFallback: OneXso Agent Service not reachable within {Seconds}s — starting Demo Mode",
            _graceWindow.TotalSeconds);

        _demo.OnStatusReceived += s => OnStatusReceived?.Invoke(s);
        _demo.OnStateReceived  += st => OnStateReceived?.Invoke(st);
        _demo.OnPolicyReceived += p => OnPolicyReceived?.Invoke(p);
        _demo.OnDisconnected   += () => OnDisconnected?.Invoke();

        _activeTcs.TrySetResult(_demo);
        _ = _demo.StartAsync(CancellationToken.None);

        _real.OnStatusReceived -= HandleRealStatus;
        _real.OnStateReceived  -= HandleRealState;
        _real.OnPolicyReceived -= HandleRealPolicy;
        _real.OnDisconnected   -= HandleRealDisconnected;
        _ = DisposeRealQuietlyAsync();
    }

    private async Task DisposeRealQuietlyAsync()
    {
        try { await _real.DisposeAsync().ConfigureAwait(false); }
        catch { /* real was never connected — best-effort cleanup only */ }
    }

    private async Task<INamedPipeClient> GetActiveAsync() => await _activeTcs.Task.ConfigureAwait(false);

    public async Task SubmitCollectionRecordsAsync(IReadOnlyList<CollectionRecord> records, CancellationToken ct) =>
        await (await GetActiveAsync().ConfigureAwait(false)).SubmitCollectionRecordsAsync(records, ct).ConfigureAwait(false);

    public async Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct) =>
        await (await GetActiveAsync().ConfigureAwait(false)).SendEnvelopeAsync(envelope, ct).ConfigureAwait(false);

    public async Task<LifecycleResultPayload?> SendLifecycleAsync(
        LifecycleAction action, CancellationToken ct, string? breakReason = null) =>
        await (await GetActiveAsync().ConfigureAwait(false)).SendLifecycleAsync(action, ct, breakReason).ConfigureAwait(false);

    public async Task<EnrollmentResultPayload?> SendActivationAsync(string code, CancellationToken ct) =>
        await (await GetActiveAsync().ConfigureAwait(false)).SendActivationAsync(code, ct).ConfigureAwait(false);

    public async Task<LogoutResultPayload?> SendLogoutAsync(CancellationToken ct) =>
        await (await GetActiveAsync().ConfigureAwait(false)).SendLogoutAsync(ct).ConfigureAwait(false);

    public async Task<BiometricEnrollmentSessionReadyPayload?> StartBiometricEnrollmentAsync(CancellationToken ct) =>
        await (await GetActiveAsync().ConfigureAwait(false)).StartBiometricEnrollmentAsync(ct).ConfigureAwait(false);

    public async Task<BiometricEnrollmentResultPayload?> CompleteBiometricEnrollmentAsync(
        Guid attemptId, bool captureSucceeded, string? clientErrorCode, CancellationToken ct) =>
        await (await GetActiveAsync().ConfigureAwait(false))
            .CompleteBiometricEnrollmentAsync(attemptId, captureSucceeded, clientErrorCode, ct).ConfigureAwait(false);

    /// <summary>
    /// Overridden explicitly (rather than relying on the interface's default <c>false</c>) so that
    /// when the real Service IS reachable, inactivity-screenshot evidence transfer keeps working
    /// exactly as it does without this wrapper — the default would otherwise silently break that
    /// feature for every real-Service run, not just Demo Mode.
    /// </summary>
    public async Task<bool> SubmitInactivityAttemptAsync(
        InactivityCaptureAttemptPayload attempt, ReadOnlyMemory<byte> jpegBytes, CancellationToken ct) =>
        await (await GetActiveAsync().ConfigureAwait(false))
            .SubmitInactivityAttemptAsync(attempt, jpegBytes, ct).ConfigureAwait(false);

    public async ValueTask DisposeAsync()
    {
        _graceCts?.Cancel();
        _graceCts?.Dispose();
        try { await _real.DisposeAsync().ConfigureAwait(false); } catch { /* best-effort */ }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter AutoFallbackNamedPipeClientTests`
Expected: PASS (3 tests). These construct a real `NamedPipeClient` that will try (and, absent a running Service, fail) to connect to the real named pipe in the background — that's expected and harmless; the 50ms grace window means the test doesn't wait for its retries to exhaust.

- [ ] **Step 5: Run the full TrayApp test suite to confirm nothing else broke**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj`
Expected: PASS (all tests, including the pre-existing suite)

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/AutoFallbackNamedPipeClient.cs tests/ONEVO.Agent.TrayApp.Tests/Services/AutoFallbackNamedPipeClientTests.cs
git commit -m "feat(trayapp): add AutoFallbackNamedPipeClient to race real Service vs Demo Mode"
```

---

## Task 3: Wire Demo Mode into the app

**Files:**
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs:20-24`
- Modify: `ONEVO.Agent.TrayApp/App.xaml.cs:14-33`

- [ ] **Step 1: Update DI registration in `MauiProgram.cs`**

Replace:

```csharp
        // Infrastructure
        builder.Services.AddSingleton<TrayIconService>();
        builder.Services.AddSingleton<NamedPipeClient>();
        builder.Services.AddSingleton<INamedPipeClient>(sp =>
            sp.GetRequiredService<NamedPipeClient>());
```

With:

```csharp
        // Infrastructure
        builder.Services.AddSingleton<TrayIconService>();
        builder.Services.AddSingleton<NamedPipeClient>();
        builder.Services.AddSingleton<DemoNamedPipeClient>();
        builder.Services.AddSingleton<AutoFallbackNamedPipeClient>();
        builder.Services.AddSingleton<INamedPipeClient>(sp =>
            sp.GetRequiredService<AutoFallbackNamedPipeClient>());
```

- [ ] **Step 2: Repoint `App.xaml.cs` at the fallback wrapper**

In `ONEVO.Agent.TrayApp/App.xaml.cs`, change:

```csharp
    private readonly TrayIconService _trayIcon;
    private readonly NamedPipeClient _pipeClient;
    private readonly CollectorCoordinator _collectors;
    private readonly ISessionDayMetrics _dayMetrics;
    private readonly ILogger<App> _logger;
    private bool _allowExit;

    public App(
        TrayIconService trayIcon,
        NamedPipeClient pipeClient,
        CollectorCoordinator collectors,
        ISessionDayMetrics dayMetrics,
        ILogger<App> logger)
```

To:

```csharp
    private readonly TrayIconService _trayIcon;
    private readonly AutoFallbackNamedPipeClient _pipeClient;
    private readonly CollectorCoordinator _collectors;
    private readonly ISessionDayMetrics _dayMetrics;
    private readonly ILogger<App> _logger;
    private bool _allowExit;

    public App(
        TrayIconService trayIcon,
        AutoFallbackNamedPipeClient pipeClient,
        CollectorCoordinator collectors,
        ISessionDayMetrics dayMetrics,
        ILogger<App> logger)
```

No other line in `App.xaml.cs` needs to change — `AutoFallbackNamedPipeClient` implements every member `App.xaml.cs` calls on `_pipeClient` (`OnStatusReceived`, `OnStateReceived`, `OnDisconnected`, `StartAsync`, `DisposeAsync`).

- [ ] **Step 3: Build the TrayApp project to confirm it compiles**

Run: `dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0`
Expected: Build succeeded, 0 errors

- [ ] **Step 4: Run the full test suite once more**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj`
Expected: PASS (all tests)

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/MauiProgram.cs ONEVO.Agent.TrayApp/App.xaml.cs
git commit -m "feat(trayapp): wire AutoFallbackNamedPipeClient into DI and App startup"
```

---

## Task 4: Build the MSIX and verify Demo Mode end-to-end

**Files:** none (verification only, using the existing unmodified `build-msix.ps1`)

- [ ] **Step 1: Build the signed MSIX**

Run (from `C:\HR\tray_app_maui`):

```powershell
.\build-msix.ps1
```

Expected: Script completes and prints `Done: <path>\ONEVO.Agent.TrayApp....msix`. (First run also creates and imports `dev-cert.pfx` — expected per the script's own banner.)

- [ ] **Step 2: Trust the dev cert and install (admin PowerShell), if not already done on this machine**

```powershell
$pw = ConvertTo-SecureString "Dev@1234" -Force -AsPlainText
Import-PfxCertificate -FilePath dev-cert.pfx -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $pw
```

Then double-click the generated `.msix` to install.

- [ ] **Step 3: Confirm `ONEVO.Agent.Service` is NOT running**

```powershell
Get-Process -Name "ONEVO.Agent.Service" -ErrorAction SilentlyContinue
```

Expected: no output (process not running) — this proves the walkthrough below has no Service, backend, or database in the loop.

- [ ] **Step 4: Launch the installed TrayApp and walk the full journey**

- Launch "OneXso WorkPulse" from the Start menu.
- Within a few seconds it should land on **Connect OneXso Workspace** (this is Demo Mode kicking in — confirm via `%LocalAppData%\ONEVO\Agent\tray-boot.log`, which should show an `AutoFallback: ... starting Demo Mode` line from the logger, alongside the existing boot-log lines).
- Enter any 6+ character code (e.g. `DEMO01`) → **Verify & Connect** → should succeed and move to **Setting Up Your Workspace**.
- Continue through **Work Location** → pick any office or "Work From Home" → **Photo** (face capture — uses the real webcam locally, unrelated to backend) → **Confirm Your Details** → **Allow Required Policies** → **Ready to Start Work**.
- Click **Clock In** → should land on **Active Session** with the live timer ticking.
- Click **Start Break**, confirm, wait a few seconds, click **End Break** → break time should reflect the elapsed pause.
- Click **Clock Out** → should land on **Workday Completed** with non-zero Total Shift/Working/Break times, and (after a minute or two of prior activity) real entries under "Top Apps" reflecting whatever was actually running on the machine during the session.

- [ ] **Step 5: Confirm no regression when a real Service IS running**

If a build of `ONEVO.Agent.Service` is available, start it, then relaunch the TrayApp and confirm it reaches **Connect Workspace** the same way it always has (real activation codes required, no "Demo Employee" identity). This is optional if no Service build is on hand right now, but should be checked before this ships to anyone who *will* run the real Service.

- [ ] **Step 6: Report back**

Summarize pass/fail for each sub-step above, plus the exact `tray-boot.log` snippet showing which mode was chosen, so we have written confirmation Demo Mode worked end-to-end on this build.
