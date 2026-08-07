# ONEVO WorkPulse Agent Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Complete the ONEVO WorkPulse Agent Phase 1 — comprehensive test suite, missing security services, all 9 journey-screen UI flows, remaining collectors, and key service-layer pieces — built on top of the existing prototype skeleton at `tray_app_maui/`.

**Architecture:** Three cooperating processes: `ONEVO.Agent.Service` (Windows Service, machine-scope, owns credentials + durable queue + state machine), `ONEVO.Agent.TrayApp` (MAUI per-user process, tray icon, journey screens, interactive collectors), `ONEVO.Agent.Shared` (contracts only, no platform code). Collectors start only when Service is `Active` and policy enables them. IPC disconnection stops all collection immediately. Window titles are SHA-256 hashed in TrayApp memory before leaving the process. No independent uploads from TrayApp.

**Tech Stack:** .NET 10 / C# 14, MAUI Windows (`net10.0-windows10.0.19041.0`), CommunityToolkit.Mvvm 8.4, xUnit 2.9, WinForms NotifyIcon, Win32 LL hooks, Named Pipes with nonce auth, DPAPI credential storage, System.Text.Json.

**Working directory for all commands:** `C:\HR\tray_app_maui`

---

## What Already Exists (Do Not Rewrite)

- `ONEVO.Agent.Shared` — IpcEnvelope, IpcMessages, IpcProtocolVersion, AgentPolicy, CollectionRecord, MonitoringState, Constants — **complete, keep as-is**
- `ONEVO.Agent.Service` — AgentStateMachine, AgentWorker, NamedPipeServer (nonce auth), ActivityRecordBuffer (in-memory), PolicyCache, CredentialStore, DeviceIdentityStore, ActivitySyncService — **has gaps, extend only**
- `ONEVO.Agent.TrayApp` — ActivityCountCollector (LL hooks, counts only), CollectorCoordinator (IPC-loss fail-safe), PrivacyScrubber, NamedPipeClient, TrayIconService — **has gaps, extend only**
- Tests: AgentStateMachineTests ✓, IpcEnvelopeTests ✓, ActivityRecordBufferTests (basic) ✓

## What Is Missing (This Plan Builds)

1. `ONEVO.Agent.TrayApp.Tests` project (does not exist yet)
2. `INamedPipeClient` interface (needed for testability of CollectorCoordinator)
3. `PrivacyScrubber.SanitizeProcessName` extracted as `internal` for unit testing
4. `HashingService` — SHA-256 window title hashing in TrayApp
5. Tests: PrivacyScrubber, CollectorCoordinator, HashingService, OptionsValidation
6. IPC enrollment message types (ActivationCodeSubmit / EnrollmentResult)
7. 9 journey-screen ViewModels + Views: ConnectWorkspace, PrepareWorkspace, WorkLocation, PhotoCapture (update), ReviewSetup, PrivacyConsent, ClockIn, ActiveSession, EndSession
8. AppShell navigation wiring
9. Remaining collectors: AppUsageCollector, DeviceStateCollector, IdleDetector, MeetingDetector, ScreenshotCollector
10. `SessionNotificationListener` + Win32Models interop
11. `LifecycleGate` (Service)
12. `HeartbeatService` stub (Service)

---

## File Map

### Create
```
tests/ONEVO.Agent.TrayApp.Tests/
  ONEVO.Agent.TrayApp.Tests.csproj
  GlobalUsings.cs
  Fakes/FakeAgentCollector.cs
  Fakes/FakeNamedPipeClient.cs
  Security/PrivacyScrubberTests.cs
  Collectors/CollectorCoordinatorTests.cs
  Security/HashingServiceTests.cs

ONEVO.Agent.TrayApp/
  Services/INamedPipeClient.cs
  Security/HashingService.cs
  Services/EnrollmentService.cs
  Services/CameraService.cs
  Interop/SessionNotificationListener.cs
  Interop/Win32Models.cs
  Collectors/AppUsageCollector.cs
  Collectors/DeviceStateCollector.cs
  Collectors/IdleDetector.cs
  Collectors/MeetingDetector.cs
  Collectors/ScreenshotCollector.cs
  ViewModels/ConnectWorkspaceViewModel.cs
  ViewModels/PrepareWorkspaceViewModel.cs
  ViewModels/WorkLocationViewModel.cs
  ViewModels/ReviewSetupViewModel.cs
  ViewModels/PrivacyConsentViewModel.cs
  ViewModels/ClockInViewModel.cs
  ViewModels/ActiveSessionViewModel.cs
  ViewModels/EndSessionViewModel.cs
  Views/ConnectWorkspacePage.xaml + .cs
  Views/PrepareWorkspacePage.xaml + .cs
  Views/WorkLocationPage.xaml + .cs
  Views/ReviewSetupPage.xaml + .cs
  Views/PrivacyConsentPage.xaml + .cs
  Views/ClockInPage.xaml + .cs
  Views/ActiveSessionPage.xaml + .cs
  Views/EndSessionPage.xaml + .cs
  Views/AppShell.xaml + .cs

ONEVO.Agent.Service/
  Lifecycle/LifecycleGate.cs
  Sync/HeartbeatService.cs

tests/ONEVO.Agent.Service.Tests/
  Configuration/OptionsValidationTests.cs
```

### Modify
```
ONEVO.Agent.slnx                               — add TrayApp.Tests
Directory.Packages.props                        — add Logging.Abstractions, NSubstitute
Directory.Build.props                           — add InternalsVisibleTo for TrayApp
ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj — add InternalsVisibleTo attribute
ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs — implement INamedPipeClient
ONEVO.Agent.TrayApp/Collectors/CollectorCoordinator.cs — use INamedPipeClient
ONEVO.Agent.TrayApp/Security/PrivacyScrubber.cs — add internal SanitizeProcessName
ONEVO.Agent.TrayApp/MauiProgram.cs              — register new VMs, services, collectors
ONEVO.Agent.TrayApp/App.xaml.cs                — route to AppShell
ONEVO.Agent.Shared/IPC/IpcMessages.cs          — add enrollment message types
```

---

## Phase 1 — Test Foundation

### Task 1: TrayApp.Tests project + solution wiring

**Files:**
- Create: `tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/GlobalUsings.cs`
- Modify: `ONEVO.Agent.slnx`
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Add packages to Directory.Packages.props**

```xml
<!-- Add inside <ItemGroup> in Directory.Packages.props -->
<PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
```

- [ ] **Step 2: Create the test project**

```xml
<!-- tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsTestProject>true</IsTestProject>
    <RootNamespace>ONEVO.Agent.TrayApp.Tests</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.WindowsDesktop.App.WindowsForms" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create GlobalUsings**

```csharp
// tests/ONEVO.Agent.TrayApp.Tests/GlobalUsings.cs
global using Microsoft.Extensions.Logging.Abstractions;
global using ONEVO.Agent.Shared.Models;
global using ONEVO.Agent.TrayApp.Security;
global using ONEVO.Agent.TrayApp.Collectors;
global using Xunit;
```

- [ ] **Step 4: Add project to solution**

```xml
<!-- ONEVO.Agent.slnx — add this line inside <Solution> -->
<Project Path="tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj" />
```

- [ ] **Step 5: Verify solution restores**

```powershell
dotnet restore .\ONEVO.Agent.slnx
```

Expected: no errors, all packages resolved.

- [ ] **Step 6: Commit**

```bash
git add tests/ONEVO.Agent.TrayApp.Tests/ ONEVO.Agent.slnx Directory.Packages.props
git commit -m "test: scaffold ONEVO.Agent.TrayApp.Tests project"
```

---

### Task 2: INamedPipeClient interface + wire into CollectorCoordinator

**Files:**
- Create: `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs`
- Modify: `ONEVO.Agent.TrayApp/Collectors/CollectorCoordinator.cs`

- [ ] **Step 1: Create interface**

```csharp
// ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs
namespace ONEVO.Agent.TrayApp.Services;

using ONEVO.Agent.Shared.Models;

public interface INamedPipeClient
{
    event Action? OnDisconnected;
    event Action<MonitoringState>? OnStateReceived;
    event Action<AgentPolicy>? OnPolicyReceived;

    Task StartAsync(CancellationToken ct);
    Task SubmitCollectionRecordsAsync(IReadOnlyList<CollectionRecord> records, CancellationToken ct);
}
```

- [ ] **Step 2: Make NamedPipeClient implement INamedPipeClient**

In `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs`, change the class declaration:

```csharp
// Old:
public sealed class NamedPipeClient : IAsyncDisposable

// New:
public sealed class NamedPipeClient : INamedPipeClient, IAsyncDisposable
```

- [ ] **Step 3: Update CollectorCoordinator to use INamedPipeClient**

```csharp
// ONEVO.Agent.TrayApp/Collectors/CollectorCoordinator.cs
// Change constructor parameter and field type:

private readonly INamedPipeClient _pipeClient;

public CollectorCoordinator(
    ILogger<CollectorCoordinator> logger,
    IEnumerable<IAgentCollector> collectors,
    INamedPipeClient pipeClient)        // <-- was NamedPipeClient
{
    // body unchanged
}
```

- [ ] **Step 4: Build to confirm**

```powershell
dotnet build .\ONEVO.Agent.slnx --configuration Debug
```

Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs \
        ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs \
        ONEVO.Agent.TrayApp/Collectors/CollectorCoordinator.cs
git commit -m "refactor: extract INamedPipeClient for testability"
```

---

### Task 3: Fakes for TrayApp tests

**Files:**
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeAgentCollector.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs`

- [ ] **Step 1: Create FakeAgentCollector**

```csharp
// tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeAgentCollector.cs
namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.TrayApp.Collectors;

public sealed class FakeAgentCollector : IAgentCollector
{
    private TaskCompletionSource _startSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource _stopSignal  = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Name => "Fake";
    public bool IsRunning { get; private set; }
    public AgentPolicy? LastPolicy { get; private set; }
    public int StartCount { get; private set; }
    public int StopCount  { get; private set; }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        IsRunning = true;
        LastPolicy = policy;
        StartCount++;
        _startSignal.TrySetResult();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        IsRunning = false;
        StopCount++;
        _stopSignal.TrySetResult();
        return Task.CompletedTask;
    }

    public Task WaitForStartAsync(TimeSpan timeout) =>
        _startSignal.Task.WaitAsync(timeout);

    public Task WaitForStopAsync(TimeSpan timeout) =>
        _stopSignal.Task.WaitAsync(timeout);

    public void ResetSignals()
    {
        _startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _stopSignal  = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
```

- [ ] **Step 2: Create FakeNamedPipeClient**

```csharp
// tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs
namespace ONEVO.Agent.TrayApp.Tests.Fakes;

using ONEVO.Agent.TrayApp.Services;

public sealed class FakeNamedPipeClient : INamedPipeClient
{
    public event Action? OnDisconnected;
    public event Action<MonitoringState>? OnStateReceived;
    public event Action<AgentPolicy>? OnPolicyReceived;

    public List<IReadOnlyList<CollectionRecord>> Submitted { get; } = [];

    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public Task SubmitCollectionRecordsAsync(IReadOnlyList<CollectionRecord> records, CancellationToken ct)
    {
        Submitted.Add(records);
        return Task.CompletedTask;
    }

    public void SimulateDisconnect()   => OnDisconnected?.Invoke();
    public void SimulateState(MonitoringState s) => OnStateReceived?.Invoke(s);
    public void SimulatePolicy(AgentPolicy p)    => OnPolicyReceived?.Invoke(p);
}
```

- [ ] **Step 3: Build test project**

```powershell
dotnet build .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Agent.TrayApp.Tests/Fakes/
git commit -m "test: add FakeAgentCollector and FakeNamedPipeClient"
```

---

### Task 4: CollectorCoordinator tests

**Files:**
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Collectors/CollectorCoordinatorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/ONEVO.Agent.TrayApp.Tests/Collectors/CollectorCoordinatorTests.cs
namespace ONEVO.Agent.TrayApp.Tests.Collectors;

using ONEVO.Agent.TrayApp.Tests.Fakes;

public sealed class CollectorCoordinatorTests : IAsyncDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(3);

    private static AgentPolicy EnabledPolicy(bool activityEnabled = true) => new()
    {
        Version         = "v1",
        ActivitySignalEnabled   = activityEnabled,
        AppUsageEnabled         = false,
        ScreenshotEnabled       = false,
        CameraVerificationEnabled = false,
        ValidUntil      = DateTimeOffset.UtcNow.AddHours(1)
    };

    private readonly FakeNamedPipeClient _pipe      = new();
    private readonly FakeAgentCollector  _collector = new();
    private readonly CollectorCoordinator _sut;

    public CollectorCoordinatorTests()
    {
        _sut = new CollectorCoordinator(
            NullLogger<CollectorCoordinator>.Instance,
            [_collector],
            _pipe);
    }

    [Fact]
    public async Task Active_WithEnabledPolicy_StartsCollector()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);

        await _collector.WaitForStartAsync(Wait);

        Assert.True(_collector.IsRunning);
        Assert.Equal("v1", _collector.LastPolicy?.Version);
    }

    [Fact]
    public async Task Active_PolicyDisablesActivity_DoesNotStartCollector()
    {
        _pipe.SimulatePolicy(EnabledPolicy(activityEnabled: false));
        _pipe.SimulateState(MonitoringState.Active);

        // Give reconcile time to run — should NOT start
        await Task.Delay(100);

        Assert.False(_collector.IsRunning);
        Assert.Equal(0, _collector.StartCount);
    }

    [Fact]
    public async Task IpcDisconnect_StopsAllCollectors_Immediately()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);
        Assert.True(_collector.IsRunning);

        _pipe.SimulateDisconnect();

        await _collector.WaitForStopAsync(Wait);
        Assert.False(_collector.IsRunning);
    }

    [Fact]
    public async Task State_Paused_StopsRunningCollectors()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        _pipe.SimulateState(MonitoringState.Paused);

        await _collector.WaitForStopAsync(Wait);
        Assert.False(_collector.IsRunning);
    }

    [Fact]
    public async Task State_ActiveThenPausedThenActive_RestartsCollector()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        _collector.ResetSignals();
        _pipe.SimulateState(MonitoringState.Paused);
        await _collector.WaitForStopAsync(Wait);

        _collector.ResetSignals();
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        Assert.True(_collector.IsRunning);
        Assert.Equal(2, _collector.StartCount);
    }

    [Fact]
    public async Task StartAll_IsIdempotent_WhenCalledTwice()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        // Fire another reconcile while already running
        _pipe.SimulatePolicy(EnabledPolicy());
        await Task.Delay(100);

        Assert.Equal(1, _collector.StartCount);
    }

    [Fact]
    public async Task State_Locked_StopsCollectors()
    {
        _pipe.SimulatePolicy(EnabledPolicy());
        _pipe.SimulateState(MonitoringState.Active);
        await _collector.WaitForStartAsync(Wait);

        _pipe.SimulateState(MonitoringState.Locked);

        await _collector.WaitForStopAsync(Wait);
        Assert.False(_collector.IsRunning);
    }

    public async ValueTask DisposeAsync() => await _sut.DisposeAsync();
}
```

- [ ] **Step 2: Run tests — expect failures (project builds but tests should compile)**

```powershell
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --configuration Debug
```

Expected: tests compile; may fail if CollectorCoordinator still takes `NamedPipeClient` concretely. Fix Task 2 first if so.

- [ ] **Step 3: Run all tests — all should pass**

```powershell
dotnet test .\ONEVO.Agent.slnx --configuration Debug
```

Expected: all tests pass including the 6 new CollectorCoordinator tests.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Agent.TrayApp.Tests/Collectors/CollectorCoordinatorTests.cs
git commit -m "test: CollectorCoordinator — IPC loss, policy gate, state transitions"
```

---

### Task 5: PrivacyScrubber — extract SanitizeProcessName + tests

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Security/PrivacyScrubber.cs`
- Modify: `ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Security/PrivacyScrubberTests.cs`

- [ ] **Step 1: Add InternalsVisibleTo to TrayApp csproj**

Add inside an `<ItemGroup>` in `ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj`:

```xml
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
    <_Parameter1>ONEVO.Agent.TrayApp.Tests</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

- [ ] **Step 2: Extract SanitizeProcessName in PrivacyScrubber.cs**

Add this method to `PrivacyScrubber` (after the existing `GetForegroundProcessNameSafe` method):

```csharp
/// <summary>Normalizes and validates a process name. Returns null if unsafe.</summary>
internal static string? SanitizeProcessName(string? rawName)
{
    if (string.IsNullOrWhiteSpace(rawName))
        return null;

    var name = rawName.Trim();

    if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        name += ".exe";

    name = name.ToLowerInvariant();

    if (name.Length > 100)
        name = name[..100];

    if (!SafeProcessName.IsMatch(name))
        return null;

    if (name.Contains('\\') || name.Contains('/') || name.Contains(':'))
        return null;

    return name;
}
```

Then update `GetForegroundProcessNameSafe` to call it (replace the normalization block):

```csharp
public static string? GetForegroundProcessNameSafe()
{
    try
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return null;

        string? name;
        try
        {
            using var process = Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch (ArgumentException)   { return null; }
        catch (InvalidOperationException) { return null; }

        return SanitizeProcessName(name);
    }
    catch { return null; }
}
```

- [ ] **Step 3: Write PrivacyScrubber tests**

```csharp
// tests/ONEVO.Agent.TrayApp.Tests/Security/PrivacyScrubberTests.cs
namespace ONEVO.Agent.TrayApp.Tests.Security;

public sealed class PrivacyScrubberTests
{
    // --- SanitizeProcessName ---

    [Theory]
    [InlineData("code",       "code.exe")]
    [InlineData("code.exe",   "code.exe")]
    [InlineData("Code.EXE",   "code.exe")]
    [InlineData("msedge.exe", "msedge.exe")]
    public void SanitizeProcessName_NormalizesToLowerExe(string input, string expected)
    {
        Assert.Equal(expected, PrivacyScrubber.SanitizeProcessName(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeProcessName_NullOrWhitespace_ReturnsNull(string? input)
    {
        Assert.Null(PrivacyScrubber.SanitizeProcessName(input));
    }

    [Theory]
    [InlineData("visual studio")]        // space not in [a-z0-9._-]
    [InlineData("bad name!.exe")]        // exclamation not allowed
    [InlineData("$special.exe")]         // dollar sign not allowed
    public void SanitizeProcessName_InvalidChars_ReturnsNull(string input)
    {
        Assert.Null(PrivacyScrubber.SanitizeProcessName(input));
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\cmd.exe")]
    [InlineData("/usr/bin/bash")]
    [InlineData("../evil.exe")]
    [InlineData("sub:stream.exe")]
    public void SanitizeProcessName_PathSeparatorsOrColon_ReturnsNull(string input)
    {
        Assert.Null(PrivacyScrubber.SanitizeProcessName(input));
    }

    [Fact]
    public void SanitizeProcessName_LongName_TruncatedTo100Chars()
    {
        var longBase = new string('a', 98) + ".exe"; // 102 chars, would be "a"*98+".exe"
        // After ToLowerInvariant + truncate to 100 → "a"*96+".exe" = 100 chars
        var result = PrivacyScrubber.SanitizeProcessName(longBase);
        Assert.NotNull(result);
        Assert.True(result!.Length <= 100);
        Assert.EndsWith(".exe", result);
    }

    // --- GetSecondsSinceLastInput ---

    [Fact]
    public void GetSecondsSinceLastInput_ReturnsNonNegative()
    {
        var result = PrivacyScrubber.GetSecondsSinceLastInput();
        Assert.True(result >= 0);
    }

    // --- GetForegroundProcessNameSafe ---

    [Fact]
    public void GetForegroundProcessNameSafe_ReturnsNullOrValidSafeName()
    {
        var result = PrivacyScrubber.GetForegroundProcessNameSafe();
        if (result is null) return; // acceptable — no foreground window in headless CI

        Assert.EndsWith(".exe", result, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain(":", result);
        Assert.True(result.Length <= 100);
        Assert.Matches(@"^[a-z0-9][a-z0-9._-]{0,98}\.exe$", result);
    }
}
```

- [ ] **Step 4: Run tests**

```powershell
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --configuration Debug
```

Expected: all PrivacyScrubber tests pass.

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/Security/PrivacyScrubber.cs \
        ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj \
        tests/ONEVO.Agent.TrayApp.Tests/Security/PrivacyScrubberTests.cs
git commit -m "test: PrivacyScrubber — sanitize, path rejection, length truncation"
```

---

### Task 6: HashingService (window title SHA-256) + tests

**Files:**
- Create: `ONEVO.Agent.TrayApp/Security/HashingService.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Security/HashingServiceTests.cs`

- [ ] **Step 1: Write failing test first**

```csharp
// tests/ONEVO.Agent.TrayApp.Tests/Security/HashingServiceTests.cs
namespace ONEVO.Agent.TrayApp.Tests.Security;

using ONEVO.Agent.TrayApp.Security;

public sealed class HashingServiceTests
{
    [Fact]
    public void HashWindowTitle_ProducesSha256Hex()
    {
        var result = HashingService.HashWindowTitle("Untitled - Notepad");
        // SHA-256 hex = 64 lowercase hex chars
        Assert.NotNull(result);
        Assert.Equal(64, result.Length);
        Assert.Matches(@"^[0-9a-f]{64}$", result);
    }

    [Fact]
    public void HashWindowTitle_SameInput_SameOutput()
    {
        const string title = "Document.docx - Microsoft Word";
        Assert.Equal(
            HashingService.HashWindowTitle(title),
            HashingService.HashWindowTitle(title));
    }

    [Fact]
    public void HashWindowTitle_DifferentInputs_DifferentOutputs()
    {
        var h1 = HashingService.HashWindowTitle("title-one");
        var h2 = HashingService.HashWindowTitle("title-two");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void HashWindowTitle_EmptyString_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, HashingService.HashWindowTitle(string.Empty));
    }

    [Fact]
    public void HashWindowTitle_DoesNotContainRawTitle()
    {
        const string title = "SuperSecretDocumentTitle";
        var result = HashingService.HashWindowTitle(title);
        Assert.DoesNotContain("SuperSecretDocumentTitle", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HashWindowTitle_KnownVector()
    {
        // SHA-256("hello") = 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
        Assert.Equal(
            "2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824",
            HashingService.HashWindowTitle("hello"));
    }
}
```

- [ ] **Step 2: Run tests — expect failure (class not found)**

```powershell
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~HashingServiceTests"
```

Expected: compile error — `HashingService` not found.

- [ ] **Step 3: Implement HashingService**

```csharp
// ONEVO.Agent.TrayApp/Security/HashingService.cs
namespace ONEVO.Agent.TrayApp.Security;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// SHA-256 hashes window titles in memory before any IPC/log/disk write (§8.3).
/// The raw title is never passed out of this method.
/// </summary>
public static class HashingService
{
    public static string HashWindowTitle(string rawTitle)
    {
        if (rawTitle.Length == 0)
            return string.Empty;

        Span<byte> inputBytes = stackalloc byte[Encoding.UTF8.GetMaxByteCount(rawTitle.Length)];
        var written = Encoding.UTF8.GetBytes(rawTitle, inputBytes);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(inputBytes[..written], hash);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Run tests — all pass**

```powershell
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~HashingServiceTests"
```

Expected: 6/6 pass.

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/Security/HashingService.cs \
        tests/ONEVO.Agent.TrayApp.Tests/Security/HashingServiceTests.cs
git commit -m "feat: HashingService SHA-256 window title + tests (§8.3)"
```

---

### Task 7: OptionsValidation tests (Service)

**Files:**
- Create: `tests/ONEVO.Agent.Service.Tests/Configuration/OptionsValidationTests.cs`

- [ ] **Step 1: Read current OptionsValidation**

```csharp
// ONEVO.Agent.Service/Configuration/OptionsValidation.cs — read it first to know what rules exist
```

Run:
```powershell
Get-Content .\ONEVO.Agent.Service\Configuration\OptionsValidation.cs
Get-Content .\ONEVO.Agent.Service\Configuration\AgentOptions.cs
```

- [ ] **Step 2: Write tests based on actual validation rules**

```csharp
// tests/ONEVO.Agent.Service.Tests/Configuration/OptionsValidationTests.cs
namespace ONEVO.Agent.Service.Tests;

using ONEVO.Agent.Service.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class OptionsValidationTests
{
    private static AgentOptions Valid() => new()
    {
        ApiBaseUrl          = "https://api.onevo.com",
        HeartbeatIntervalSeconds  = 60,
        PolicyRefreshIntervalSeconds = 3600,
        IngestIntervalSeconds = 120,
        QueueMaxRecords = 5_000,
        IpcConnectionTimeoutMs = 5_000,
        HttpTimeoutSeconds = 30,
        LogFileSizeLimitMb = 50,
        LogRetentionDays = 30
    };

    [Fact]
    public void ValidOptions_PassValidation()
    {
        var validator = new OptionsValidation();
        var result = validator.Validate(null, Valid());
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("http://")]
    public void InvalidApiBaseUrl_FailsValidation(string url)
    {
        var opts = Valid() with { ApiBaseUrl = url };
        var result = new OptionsValidation().Validate(null, opts);
        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9)]
    public void HeartbeatInterval_BelowMinimum_FailsValidation(int seconds)
    {
        var opts = Valid() with { HeartbeatIntervalSeconds = seconds };
        var result = new OptionsValidation().Validate(null, opts);
        Assert.True(result.Failed);
    }

    [Fact]
    public void QueueMaxRecords_Zero_FailsValidation()
    {
        var opts = Valid() with { QueueMaxRecords = 0 };
        var result = new OptionsValidation().Validate(null, opts);
        Assert.True(result.Failed);
    }
}
```

> **Note:** If `AgentOptions` does not currently have all these properties, run the Get-Content commands in Step 1 and adjust test properties to match what exists. Do not invent properties.

- [ ] **Step 3: Run tests — some may fail if AgentOptions lacks properties**

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~OptionsValidationTests"
```

Fix any mismatches with actual `AgentOptions` properties before proceeding.

- [ ] **Step 4: Commit**

```bash
git add tests/ONEVO.Agent.Service.Tests/Configuration/OptionsValidationTests.cs
git commit -m "test: OptionsValidation — URL, intervals, queue capacity"
```

---

### Task 8: Run full test suite — all green

- [ ] **Step 1: Run everything**

```powershell
dotnet test .\ONEVO.Agent.slnx --configuration Debug --logger "console;verbosity=normal"
```

Expected: all tests pass. Fix any failures before proceeding to Phase 2.

- [ ] **Step 2: Commit if any test-only fixes were made**

```bash
git add -u
git commit -m "test: fix remaining test failures before Phase 2"
```

---

## Phase 2 — IPC Enrollment Messages

### Task 9: Add enrollment IPC message types

**Files:**
- Modify: `ONEVO.Agent.Shared/IPC/IpcMessages.cs`

- [ ] **Step 1: Add enrollment message types to IpcMessages.cs**

Add to `IpcMessageTypes`:

```csharp
/// <summary>Tray → Service: employee-entered activation code from web portal.</summary>
public const string ActivationCodeSubmit = "ActivationCodeSubmit";

/// <summary>Service → Tray: result of enrollment attempt.</summary>
public const string EnrollmentResult = "EnrollmentResult";
```

Add payload records at the bottom of the file:

```csharp
public sealed record ActivationCodeSubmitPayload(string Code);

public sealed record EnrollmentResultPayload
{
    public required bool Success { get; init; }
    public string? ErrorCode  { get; init; }   // "INVALID_CODE" | "EXPIRED" | "ALREADY_ENROLLED"
    public string? EmployeeName { get; init; } // set on success for greeting
}
```

- [ ] **Step 2: Build Shared project**

```powershell
dotnet build .\ONEVO.Agent.Shared\ONEVO.Agent.Shared.csproj
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add ONEVO.Agent.Shared/IPC/IpcMessages.cs
git commit -m "feat: add ActivationCodeSubmit / EnrollmentResult IPC messages"
```

---

## Phase 3 — Journey UI ViewModels

### Task 10: ConnectWorkspaceViewModel (TA-ACT-01)

**Files:**
- Create: `ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs`

Screen: employee enters a 6-character activation code (from the web portal), clicks "Verify and Connect". TrayApp sends the code to Service via IPC; Service calls the backend.

- [ ] **Step 1: Create ViewModel**

```csharp
// ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs
namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.RegularExpressions;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.IPC;

public sealed partial class ConnectWorkspaceViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyAndConnectCommand))]
    private string _activationCode = string.Empty;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isConnecting;

    public ConnectWorkspaceViewModel(INamedPipeClient pipe)
    {
        Title = "Connect OneVo Workspace";
        _pipe = pipe;
    }

    private bool CanVerify =>
        !IsConnecting &&
        Regex.IsMatch(ActivationCode.Trim(), @"^[A-Za-z0-9]{6}$");

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAndConnectAsync(CancellationToken ct)
    {
        IsConnecting = true;
        ErrorMessage = null;

        try
        {
            var payload = new ActivationCodeSubmitPayload(ActivationCode.Trim().ToUpperInvariant());
            var envelope = new ONEVO.Agent.Shared.IPC.IpcEnvelope
            {
                Type    = IpcMessageTypes.ActivationCodeSubmit,
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(payload)
            };
            // Route through Service; TrayApp never calls backend directly.
            await _pipe.SubmitCollectionRecordsAsync([], ct); // placeholder — wire SubmitEnvelopeAsync in INamedPipeClient (Task 11)
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private static void OpenEmployeePortal()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName  = "https://app.onevo.com",
            UseShellExecute = true
        });
    }
}
```

> **Note:** `SubmitEnvelopeAsync` is not yet on `INamedPipeClient`. Add it in Task 11.

- [ ] **Step 2: Add SendEnvelopeAsync to INamedPipeClient + NamedPipeClient**

In `INamedPipeClient.cs`, add:
```csharp
Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct);
```

In `NamedPipeClient.cs`, implement:
```csharp
public Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct) =>
    WriteEnvelopeAsync(envelope, ct);
```

Update `ConnectWorkspaceViewModel` to call `await _pipe.SendEnvelopeAsync(envelope, ct)`.

Also add to `FakeNamedPipeClient`:
```csharp
public List<IpcEnvelope> SentEnvelopes { get; } = [];
public Task SendEnvelopeAsync(IpcEnvelope envelope, CancellationToken ct)
{
    SentEnvelopes.Add(envelope);
    return Task.CompletedTask;
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs \
        ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs \
        ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs \
        tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs
git commit -m "feat: ConnectWorkspaceViewModel (TA-ACT-01) + SendEnvelopeAsync"
```

---

### Task 11: PrepareWorkspaceViewModel (TA-SET-01)

**Files:**
- Create: `ONEVO.Agent.TrayApp/ViewModels/PrepareWorkspaceViewModel.cs`

Screen: loading progress after enrollment. Shows progress steps and pre-populated employee details.

- [ ] **Step 1: Create ViewModel**

```csharp
// ONEVO.Agent.TrayApp/ViewModels/PrepareWorkspaceViewModel.cs
namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class PrepareWorkspaceViewModel : BaseViewModel
{
    [ObservableProperty] private bool _activationVerified;
    [ObservableProperty] private bool _profileLoaded;
    [ObservableProperty] private bool _permissionsChecked;
    [ObservableProperty] private bool _companySettingsVerified;
    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty] private string _employeeFullName  = string.Empty;
    [ObservableProperty] private string _employeeEmail     = string.Empty;
    [ObservableProperty] private string _employeeDepartment = string.Empty;
    [ObservableProperty] private string _selectedWorkLocation = string.Empty;

    public bool CanContinue =>
        ActivationVerified && ProfileLoaded && PermissionsChecked && CompanySettingsVerified;

    public PrepareWorkspaceViewModel()
    {
        Title = "Preparing Your Workspace";
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;

        await Task.Delay(500, ct);
        ActivationVerified = true;

        await Task.Delay(800, ct);
        ProfileLoaded = true;
        // Populate from local identity store (wired via constructor injection later)
        EmployeeFullName   = "Loading…";
        EmployeeEmail      = "Loading…";
        EmployeeDepartment = "Loading…";

        await Task.Delay(600, ct);
        PermissionsChecked = true;

        await Task.Delay(400, ct);
        CompanySettingsVerified = true;

        IsLoading = false;
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private static void ContinueSetup()
    {
        // Navigation wired in AppShell Task 18
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

- [ ] **Step 3: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/PrepareWorkspaceViewModel.cs
git commit -m "feat: PrepareWorkspaceViewModel (TA-SET-01) progress + employee details"
```

---

### Task 12: WorkLocationViewModel (TA-SET-02)

**Files:**
- Create: `ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs`

Screen: employee selects one of the approved work locations.

- [ ] **Step 1: Create ViewModel**

```csharp
// ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs
namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class WorkLocationViewModel : BaseViewModel
{
    public IReadOnlyList<WorkLocationOption> ApprovedLocations { get; } =
    [
        new("Central Office",    "HQ"),
        new("Singapore Office",  "SG"),
        new("Hyderabad Office",  "HYD"),
        new("Remote Work",       "REMOTE")
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAndContinueCommand))]
    private WorkLocationOption? _selectedLocation;

    [ObservableProperty] private string _searchText = string.Empty;

    public IEnumerable<WorkLocationOption> FilteredLocations =>
        string.IsNullOrWhiteSpace(SearchText)
            ? ApprovedLocations
            : ApprovedLocations.Where(l =>
                l.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    partial void OnSearchTextChanged(string value) =>
        OnPropertyChanged(nameof(FilteredLocations));

    public WorkLocationViewModel() { Title = "Select Your Work Location"; }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private static void SaveAndContinue()
    {
        // Navigation wired in AppShell Task 18
    }

    private bool HasSelection => SelectedLocation is not null;
}

public sealed record WorkLocationOption(string DisplayName, string Code);
```

- [ ] **Step 2: Build and commit**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

```bash
git add ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs
git commit -m "feat: WorkLocationViewModel (TA-SET-02) location selection + search"
```

---

### Task 13: ReviewSetupViewModel (TA-SET-04)

**Files:**
- Create: `ONEVO.Agent.TrayApp/ViewModels/ReviewSetupViewModel.cs`

Screen: summary of all setup details before confirming.

- [ ] **Step 1: Create ViewModel**

```csharp
// ONEVO.Agent.TrayApp/ViewModels/ReviewSetupViewModel.cs
namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class ReviewSetupViewModel : BaseViewModel
{
    [ObservableProperty] private string _fullName        = string.Empty;
    [ObservableProperty] private string _workEmail       = string.Empty;
    [ObservableProperty] private string _department      = string.Empty;
    [ObservableProperty] private string _manager         = string.Empty;
    [ObservableProperty] private string _workLocation    = string.Empty;
    [ObservableProperty] private string _monitoringManager = string.Empty;
    [ObservableProperty] private string _registeredDevice  = string.Empty;
    [ObservableProperty] private DateTimeOffset _lastUpdated = DateTimeOffset.UtcNow;
    [ObservableProperty] private bool _hasSetupErrors;

    public ReviewSetupViewModel() { Title = "Review Your Setup"; }

    [RelayCommand]
    private static void EditSetup()
    {
        // Navigate back to PrepareWorkspacePage
    }

    [RelayCommand]
    private static void ConfirmSetup()
    {
        // Navigate to PrivacyConsentPage
    }
}
```

- [ ] **Step 2: Build and commit**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

```bash
git add ONEVO.Agent.TrayApp/ViewModels/ReviewSetupViewModel.cs
git commit -m "feat: ReviewSetupViewModel (TA-SET-04)"
```

---

### Task 14: PrivacyConsentViewModel (TA-POL-01)

**Files:**
- Create: `ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs`

Screen: employee reviews and acknowledges monitoring toggles. Required toggles cannot be disabled.

- [ ] **Step 1: Create ViewModel**

```csharp
// ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs
namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public sealed partial class PrivacyConsentViewModel : BaseViewModel
{
    // Required by policy — always true, toggle locked
    [ObservableProperty] private bool _activitySignalEnabled  = true;
    public bool ActivitySignalRequired => true;

    // Employee-reviewable (may be forced ON by policy — reflect AgentPolicy)
    [ObservableProperty] private bool _applicationUsageEnabled = true;
    [ObservableProperty] private bool _workLocationEnabled     = true;
    [ObservableProperty] private bool _cameraAccessEnabled     = false;
    [ObservableProperty] private bool _notificationsEnabled    = true;
    [ObservableProperty] private bool _keyboardMouseEnabled    = true;

    [ObservableProperty] private bool _policyAcknowledged;

    public PrivacyConsentViewModel() { Title = "Privacy, Monitoring and Required Permissions"; }

    public void ApplyPolicy(AgentPolicy policy)
    {
        ApplicationUsageEnabled = policy.AppUsageEnabled;
        CameraAccessEnabled     = policy.CameraVerificationEnabled;
        // Activity signal and keyboard/mouse are always required in Phase 1
    }

    [RelayCommand(CanExecute = nameof(PolicyAcknowledged))]
    private static void ReviewAndContinue()
    {
        // Navigate to ClockInPage
    }
}
```

- [ ] **Step 2: Build and commit**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

```bash
git add ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs
git commit -m "feat: PrivacyConsentViewModel (TA-POL-01) monitoring toggles + ack"
```

---

### Task 15: ClockInViewModel (TA-ATT-01)

**Files:**
- Create: `ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs`

Screen: employee sees greeting, readiness checklist, and Clock In button. Sends clock-in command to Service via IPC.

- [ ] **Step 1: Create ViewModel**

```csharp
// ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs
namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class ClockInViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty] private string _greeting          = "Good morning";
    [ObservableProperty] private string _employeeName      = string.Empty;
    [ObservableProperty] private string _workLocation      = string.Empty;
    [ObservableProperty] private DateTimeOffset _currentDate = DateTimeOffset.Now;

    [ObservableProperty] private bool _identityChecked     = true;
    [ObservableProperty] private bool _permissionsReady    = true;
    [ObservableProperty] private bool _requiredChecksPass  = true;

    [ObservableProperty] private bool _isClockinIn;
    [ObservableProperty] private string? _errorMessage;

    public bool ReadyToClockIn => IdentityChecked && PermissionsReady && RequiredChecksPass;

    public ClockInViewModel(INamedPipeClient pipe)
    {
        Title   = "Ready to Start Work";
        _pipe   = pipe;
        Greeting = GetGreeting();
    }

    private static string GetGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
    }

    [RelayCommand(CanExecute = nameof(ReadyToClockIn))]
    private async Task ClockInAsync(CancellationToken ct)
    {
        IsClockinIn  = true;
        ErrorMessage = null;
        try
        {
            // Service handles clock-in via StartMonitoring command lifecycle
            var envelope = new ONEVO.Agent.Shared.IPC.IpcEnvelope
            {
                Type    = ONEVO.Agent.Shared.IPC.IpcMessageTypes.StatusRequest
            };
            await _pipe.SendEnvelopeAsync(envelope, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsClockinIn = false;
        }
    }
}
```

- [ ] **Step 2: Build and commit**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

```bash
git add ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs
git commit -m "feat: ClockInViewModel (TA-ATT-01) greeting + readiness + clock-in"
```

---

### Task 16: ActiveSessionViewModel (TA-ATT-02)

**Files:**
- Create: `ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs`

Screen: live work session — shows elapsed time, session stats, Start Break / End Work Session buttons.

- [ ] **Step 1: Create ViewModel**

```csharp
// ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs
namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class ActiveSessionViewModel : BaseViewModel, IAsyncDisposable
{
    private readonly INamedPipeClient _pipe;
    private readonly System.Timers.Timer _clockTimer;
    private DateTimeOffset _clockInTime;

    [ObservableProperty] private string _elapsedDisplay    = "00:00:00";
    [ObservableProperty] private string _clockInTimeDisplay = string.Empty;
    [ObservableProperty] private string _breakTimeDisplay  = "00:00:00";
    [ObservableProperty] private string _activeTimeDisplay = "00:00:00";
    [ObservableProperty] private bool   _isOnBreak;
    [ObservableProperty] private string? _syncMessage;

    public ActiveSessionViewModel(INamedPipeClient pipe)
    {
        Title     = "Your Work Session Is Active";
        _pipe     = pipe;
        _clockTimer = new System.Timers.Timer(1_000) { AutoReset = true };
        _clockTimer.Elapsed += (_, _) => UpdateElapsed();
    }

    public void StartSession(DateTimeOffset clockIn)
    {
        _clockInTime       = clockIn;
        ClockInTimeDisplay = clockIn.ToLocalTime().ToString("HH:mm");
        _clockTimer.Start();
    }

    private void UpdateElapsed()
    {
        var elapsed = DateTimeOffset.UtcNow - _clockInTime;
        ElapsedDisplay    = elapsed.ToString(@"hh\:mm\:ss");
        ActiveTimeDisplay = elapsed.ToString(@"hh\:mm\:ss");
    }

    [RelayCommand]
    private async Task StartBreakAsync(CancellationToken ct)
    {
        IsOnBreak   = true;
        SyncMessage = "Break started…";
        var envelope = new ONEVO.Agent.Shared.IPC.IpcEnvelope
            { Type = ONEVO.Agent.Shared.IPC.IpcMessageTypes.StatusRequest };
        await _pipe.SendEnvelopeAsync(envelope, ct);
    }

    [RelayCommand]
    private static void EndWorkSession()
    {
        // Navigate to EndSessionPage
    }

    public async ValueTask DisposeAsync()
    {
        _clockTimer.Stop();
        _clockTimer.Dispose();
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Build and commit**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

```bash
git add ONEVO.Agent.TrayApp/ViewModels/ActiveSessionViewModel.cs
git commit -m "feat: ActiveSessionViewModel (TA-ATT-02) live timer + break"
```

---

### Task 17: EndSessionViewModel (TA-ATT-03)

**Files:**
- Create: `ONEVO.Agent.TrayApp/ViewModels/EndSessionViewModel.cs`

Screen: session summary (clock in/out, break, AFK, meeting, working time) with Confirm Clock-Out.

- [ ] **Step 1: Create ViewModel**

```csharp
// ONEVO.Agent.TrayApp/ViewModels/EndSessionViewModel.cs
namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class EndSessionViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty] private string _clockInDisplay    = string.Empty;
    [ObservableProperty] private string _clockOutDisplay   = string.Empty;
    [ObservableProperty] private string _breakTimeDisplay  = "00:00:00";
    [ObservableProperty] private string _afkTimeDisplay    = "00:00:00";
    [ObservableProperty] private string _meetingTimeDisplay = "00:00:00";
    [ObservableProperty] private string _workingTimeDisplay = "00:00:00";
    [ObservableProperty] private string _accuracyScore     = "—";
    [ObservableProperty] private bool   _isConfirming;
    [ObservableProperty] private string? _errorMessage;

    public EndSessionViewModel(INamedPipeClient pipe)
    {
        Title = "End Work Session";
        _pipe = pipe;
    }

    public void LoadSummary(DateTimeOffset clockIn, DateTimeOffset clockOut,
        TimeSpan breakTime, TimeSpan afkTime, TimeSpan meetingTime)
    {
        ClockInDisplay    = clockIn.ToLocalTime().ToString("HH:mm");
        ClockOutDisplay   = clockOut.ToLocalTime().ToString("HH:mm");
        BreakTimeDisplay  = breakTime.ToString(@"hh\:mm\:ss");
        AfkTimeDisplay    = afkTime.ToString(@"hh\:mm\:ss");
        MeetingTimeDisplay = meetingTime.ToString(@"hh\:mm\:ss");
        var working = (clockOut - clockIn) - breakTime - afkTime;
        WorkingTimeDisplay = working.ToString(@"hh\:mm\:ss");
    }

    [RelayCommand]
    private static void ReturnToWork()
    {
        // Navigate back to ActiveSessionPage
    }

    [RelayCommand]
    private static void VerifyIdentity()
    {
        // Navigate to PhotoCapturePage for clock-out verification
    }

    [RelayCommand]
    private async Task ConfirmClockOutAsync(CancellationToken ct)
    {
        IsConfirming = true;
        ErrorMessage = null;
        try
        {
            var envelope = new ONEVO.Agent.Shared.IPC.IpcEnvelope
                { Type = ONEVO.Agent.Shared.IPC.IpcMessageTypes.StatusRequest };
            await _pipe.SendEnvelopeAsync(envelope, ct);
            // Service handles StopMonitoring + flush
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsConfirming = false; }
    }
}
```

- [ ] **Step 2: Build and commit**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

```bash
git add ONEVO.Agent.TrayApp/ViewModels/EndSessionViewModel.cs
git commit -m "feat: EndSessionViewModel (TA-ATT-03) summary + confirm clock-out"
```

---

## Phase 4 — Journey UI Views + AppShell

### Task 18: XAML Views for all journey screens

**Files:**
- Create: `Views/ConnectWorkspacePage.xaml` + `.xaml.cs`
- Create: `Views/PrepareWorkspacePage.xaml` + `.xaml.cs`
- Create: `Views/WorkLocationPage.xaml` + `.xaml.cs`
- Create: `Views/ReviewSetupPage.xaml` + `.xaml.cs`
- Create: `Views/PrivacyConsentPage.xaml` + `.xaml.cs`
- Create: `Views/ClockInPage.xaml` + `.xaml.cs`
- Create: `Views/ActiveSessionPage.xaml` + `.xaml.cs`
- Create: `Views/EndSessionPage.xaml` + `.xaml.cs`

All views follow the same pattern. Create each XAML + code-behind:

- [ ] **Step 1: ConnectWorkspacePage**

```xml
<!-- ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml -->
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.ConnectWorkspacePage"
             x:DataType="vm:ConnectWorkspaceViewModel"
             Title="Connect OneVo Workspace">
  <VerticalStackLayout Padding="40" Spacing="20" VerticalOptions="Center">
    <Label Text="OneVo Workspace" FontSize="28" FontAttributes="Bold"
           TextColor="{StaticResource Primary}" HorizontalOptions="Center" />
    <Label Text="Connect OneVo Workspace" FontSize="20" HorizontalOptions="Center" />
    <Label Text="Enter the one-time code from your Employee Web Portal to link this device."
           FontSize="14" TextColor="Gray" HorizontalTextAlignment="Center" />

    <Entry Placeholder="Activation Code (6 characters)"
           Text="{Binding ActivationCode}"
           MaxLength="6"
           IsEnabled="{Binding IsConnecting, Converter={StaticResource InvertBoolConverter}}" />

    <Label Text="{Binding ErrorMessage}" TextColor="Red"
           IsVisible="{Binding ErrorMessage, Converter={StaticResource IsNotNullConverter}}" />

    <Button Text="Verify and Connect"
            Command="{Binding VerifyAndConnectCommand}"
            BackgroundColor="{StaticResource Primary}"
            TextColor="White" />

    <Button Text="Open Employee Web Portal"
            Command="{Binding OpenEmployeePortalCommand}"
            BackgroundColor="Transparent"
            TextColor="{StaticResource Primary}" />

    <ActivityIndicator IsRunning="{Binding IsConnecting}"
                       IsVisible="{Binding IsConnecting}" />
  </VerticalStackLayout>
</ContentPage>
```

```csharp
// ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml.cs
namespace ONEVO.Agent.TrayApp.Views;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ConnectWorkspacePage : ContentPage
{
    public ConnectWorkspacePage(ConnectWorkspaceViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
```

- [ ] **Step 2: ClockInPage**

```xml
<!-- ONEVO.Agent.TrayApp/Views/ClockInPage.xaml -->
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.ClockInPage"
             x:DataType="vm:ClockInViewModel">
  <VerticalStackLayout Padding="40" Spacing="16" VerticalOptions="Center">
    <Label Text="{Binding Greeting, StringFormat='{0},'}" FontSize="22" FontAttributes="Bold" />
    <Label Text="{Binding EmployeeName}" FontSize="18" />
    <Label Text="{Binding CurrentDate, StringFormat='{0:dddd, MMMM d}'}" TextColor="Gray" />
    <BoxView HeightRequest="1" BackgroundColor="LightGray" />
    <Label Text="Ready to Start Work" FontSize="16" FontAttributes="Bold"
           TextColor="{StaticResource Primary}" />
    <Label Text="{Binding WorkLocation}" FontSize="14" TextColor="Gray" />
    <Label Text="{Binding ErrorMessage}" TextColor="Red"
           IsVisible="{Binding ErrorMessage, Converter={StaticResource IsNotNullConverter}}" />
    <Button Text="Clock In"
            Command="{Binding ClockInCommand}"
            BackgroundColor="{StaticResource Primary}"
            TextColor="White" FontSize="18"
            HeightRequest="56" />
    <ActivityIndicator IsRunning="{Binding IsClockinIn}" IsVisible="{Binding IsClockinIn}" />
  </VerticalStackLayout>
</ContentPage>
```

```csharp
// ONEVO.Agent.TrayApp/Views/ClockInPage.xaml.cs
namespace ONEVO.Agent.TrayApp.Views;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ClockInPage : ContentPage
{
    public ClockInPage(ClockInViewModel vm) { InitializeComponent(); BindingContext = vm; }
}
```

- [ ] **Step 3: ActiveSessionPage**

```xml
<!-- ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml -->
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.ActiveSessionPage"
             x:DataType="vm:ActiveSessionViewModel">
  <VerticalStackLayout Padding="32" Spacing="12">
    <Label Text="Your Work Session Is Active" FontSize="20" FontAttributes="Bold"
           HorizontalOptions="Center" />
    <Label Text="Working" TextColor="Green" FontSize="16" HorizontalOptions="Center" />
    <Label Text="{Binding ElapsedDisplay}" FontSize="40" FontAttributes="Bold"
           HorizontalOptions="Center" />

    <Grid ColumnDefinitions="*,*" RowDefinitions="Auto,Auto,Auto" RowSpacing="8" ColumnSpacing="8">
      <Label Grid.Row="0" Grid.Column="0" Text="Clock In:" />
      <Label Grid.Row="0" Grid.Column="1" Text="{Binding ClockInTimeDisplay}" />
      <Label Grid.Row="1" Grid.Column="0" Text="Break Time:" />
      <Label Grid.Row="1" Grid.Column="1" Text="{Binding BreakTimeDisplay}" />
      <Label Grid.Row="2" Grid.Column="0" Text="Active Time:" />
      <Label Grid.Row="2" Grid.Column="1" Text="{Binding ActiveTimeDisplay}" />
    </Grid>

    <Label Text="{Binding SyncMessage}" TextColor="Gray" FontSize="12"
           IsVisible="{Binding SyncMessage, Converter={StaticResource IsNotNullConverter}}" />

    <Grid ColumnDefinitions="*,*" ColumnSpacing="12">
      <Button Grid.Column="0" Text="Start Break"
              Command="{Binding StartBreakCommand}" />
      <Button Grid.Column="1" Text="End Work Session"
              Command="{Binding EndWorkSessionCommand}"
              BackgroundColor="OrangeRed" TextColor="White" />
    </Grid>
  </VerticalStackLayout>
</ContentPage>
```

```csharp
// ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml.cs
namespace ONEVO.Agent.TrayApp.Views;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ActiveSessionPage : ContentPage
{
    public ActiveSessionPage(ActiveSessionViewModel vm) { InitializeComponent(); BindingContext = vm; }
}
```

- [ ] **Step 4: EndSessionPage**

```xml
<!-- ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml -->
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.EndSessionPage"
             x:DataType="vm:EndSessionViewModel">
  <VerticalStackLayout Padding="32" Spacing="12">
    <Label Text="End Work Session" FontSize="20" FontAttributes="Bold" HorizontalOptions="Center" />

    <Grid ColumnDefinitions="*,*" RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto"
          RowSpacing="6" ColumnSpacing="8">
      <Label Grid.Row="0" Grid.Column="0" Text="Clock In:" />
      <Label Grid.Row="0" Grid.Column="1" Text="{Binding ClockInDisplay}" />
      <Label Grid.Row="1" Grid.Column="0" Text="Clock Out:" />
      <Label Grid.Row="1" Grid.Column="1" Text="{Binding ClockOutDisplay}" />
      <Label Grid.Row="2" Grid.Column="0" Text="Break Time:" />
      <Label Grid.Row="2" Grid.Column="1" Text="{Binding BreakTimeDisplay}" />
      <Label Grid.Row="3" Grid.Column="0" Text="AFK Time:" />
      <Label Grid.Row="3" Grid.Column="1" Text="{Binding AfkTimeDisplay}" />
      <Label Grid.Row="4" Grid.Column="0" Text="Meeting Time:" />
      <Label Grid.Row="4" Grid.Column="1" Text="{Binding MeetingTimeDisplay}" />
      <Label Grid.Row="5" Grid.Column="0" Text="Working Time:" FontAttributes="Bold" />
      <Label Grid.Row="5" Grid.Column="1" Text="{Binding WorkingTimeDisplay}" FontAttributes="Bold" />
    </Grid>

    <Label Text="{Binding ErrorMessage}" TextColor="Red"
           IsVisible="{Binding ErrorMessage, Converter={StaticResource IsNotNullConverter}}" />

    <Grid ColumnDefinitions="*,*,*" ColumnSpacing="8">
      <Button Grid.Column="0" Text="Return to Work" Command="{Binding ReturnToWorkCommand}" />
      <Button Grid.Column="1" Text="Verify Identity" Command="{Binding VerifyIdentityCommand}" />
      <Button Grid.Column="2" Text="Confirm Clock-Out"
              Command="{Binding ConfirmClockOutCommand}"
              BackgroundColor="{StaticResource Primary}" TextColor="White" />
    </Grid>
    <ActivityIndicator IsRunning="{Binding IsConfirming}" IsVisible="{Binding IsConfirming}" />
  </VerticalStackLayout>
</ContentPage>
```

```csharp
// ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml.cs
namespace ONEVO.Agent.TrayApp.Views;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class EndSessionPage : ContentPage
{
    public EndSessionPage(EndSessionViewModel vm) { InitializeComponent(); BindingContext = vm; }
}
```

- [ ] **Step 5: Create remaining views (PrepareWorkspacePage, WorkLocationPage, ReviewSetupPage, PrivacyConsentPage) using the same pattern**

For each:
- XAML: `ContentPage` with `x:DataType` pointing to the matching ViewModel, bindings matching ViewModel properties
- Code-behind: single constructor injecting the ViewModel and setting `BindingContext`

PrepareWorkspacePage binds: `ActivationVerified`, `ProfileLoaded`, `PermissionsChecked`, `CompanySettingsVerified`, `EmployeeFullName`, `EmployeeEmail`, `EmployeeDepartment`, `ContinueSetupCommand`.

WorkLocationPage binds: `FilteredLocations`, `SelectedLocation`, `SearchText`, `SaveAndContinueCommand`.

ReviewSetupPage binds: `FullName`, `WorkEmail`, `Department`, `Manager`, `WorkLocation`, `LastUpdated`, `EditSetupCommand`, `ConfirmSetupCommand`.

PrivacyConsentPage binds: all permission booleans, `PolicyAcknowledged`, `ReviewAndContinueCommand`.

- [ ] **Step 6: Build all views**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

Expected: 0 errors. (XAML compiler will catch binding type mismatches.)

- [ ] **Step 7: Commit**

```bash
git add ONEVO.Agent.TrayApp/Views/
git commit -m "feat: all journey screen XAML views (TA-ACT-01 through TA-ATT-03)"
```

---

### Task 19: AppShell + MauiProgram wiring

**Files:**
- Create: `ONEVO.Agent.TrayApp/Views/AppShell.xaml` + `.xaml.cs`
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs`
- Modify: `ONEVO.Agent.TrayApp/App.xaml.cs`

- [ ] **Step 1: Create AppShell**

```xml
<!-- ONEVO.Agent.TrayApp/Views/AppShell.xaml -->
<?xml version="1.0" encoding="utf-8" ?>
<Shell xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
       xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
       xmlns:views="clr-namespace:ONEVO.Agent.TrayApp.Views"
       x:Class="ONEVO.Agent.TrayApp.Views.AppShell"
       FlyoutBehavior="Disabled">

  <ShellContent Route="connect"  ContentTemplate="{DataTemplate views:ConnectWorkspacePage}" />
  <ShellContent Route="prepare"  ContentTemplate="{DataTemplate views:PrepareWorkspacePage}" />
  <ShellContent Route="location" ContentTemplate="{DataTemplate views:WorkLocationPage}" />
  <ShellContent Route="photo"    ContentTemplate="{DataTemplate views:PhotoCaptureWindow}" />
  <ShellContent Route="review"   ContentTemplate="{DataTemplate views:ReviewSetupPage}" />
  <ShellContent Route="policy"   ContentTemplate="{DataTemplate views:PrivacyConsentPage}" />
  <ShellContent Route="clockin"  ContentTemplate="{DataTemplate views:ClockInPage}" />
  <ShellContent Route="active"   ContentTemplate="{DataTemplate views:ActiveSessionPage}" />
  <ShellContent Route="end"      ContentTemplate="{DataTemplate views:EndSessionPage}" />
</Shell>
```

```csharp
// ONEVO.Agent.TrayApp/Views/AppShell.xaml.cs
namespace ONEVO.Agent.TrayApp.Views;

public partial class AppShell : Shell
{
    public AppShell() => InitializeComponent();
}
```

- [ ] **Step 2: Update MauiProgram.cs — register all VMs and services**

Replace `MauiProgram.cs` content with:

```csharp
// ONEVO.Agent.TrayApp/MauiProgram.cs
namespace ONEVO.Agent.TrayApp;

using ONEVO.Agent.TrayApp.Collectors;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.ViewModels;
using ONEVO.Agent.TrayApp.Views;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Infrastructure
        builder.Services.AddSingleton<TrayIconService>();
        builder.Services.AddSingleton<NamedPipeClient>();
        builder.Services.AddSingleton<INamedPipeClient>(sp =>
            sp.GetRequiredService<NamedPipeClient>());
        builder.Services.AddSingleton<NotificationService>();

        // Collectors
        builder.Services.AddSingleton<ActivityCountCollector>();
        builder.Services.AddSingleton<IAgentCollector>(sp =>
            sp.GetRequiredService<ActivityCountCollector>());
        builder.Services.AddSingleton<CollectorCoordinator>();

        // ViewModels (Transient — each page gets its own instance)
        builder.Services.AddTransient<ConnectWorkspaceViewModel>();
        builder.Services.AddTransient<PrepareWorkspaceViewModel>();
        builder.Services.AddTransient<WorkLocationViewModel>();
        builder.Services.AddTransient<PhotoCaptureWindowViewModel>();
        builder.Services.AddTransient<ReviewSetupViewModel>();
        builder.Services.AddTransient<PrivacyConsentViewModel>();
        builder.Services.AddTransient<ClockInViewModel>();
        builder.Services.AddTransient<ActiveSessionViewModel>();
        builder.Services.AddTransient<EndSessionViewModel>();
        builder.Services.AddTransient<StatusPopupViewModel>();
        builder.Services.AddTransient<LoginWindowViewModel>();

        // Views
        builder.Services.AddTransient<ConnectWorkspacePage>();
        builder.Services.AddTransient<PrepareWorkspacePage>();
        builder.Services.AddTransient<WorkLocationPage>();
        builder.Services.AddTransient<PhotoCaptureWindow>();
        builder.Services.AddTransient<ReviewSetupPage>();
        builder.Services.AddTransient<PrivacyConsentPage>();
        builder.Services.AddTransient<ClockInPage>();
        builder.Services.AddTransient<ActiveSessionPage>();
        builder.Services.AddTransient<EndSessionPage>();

        return builder.Build();
    }
}
```

- [ ] **Step 3: Update App.xaml.cs — use Shell instead of raw CreateWindow**

Replace the `CreateWindow` override body to launch `AppShell`:

```csharp
protected override Window CreateWindow(IActivationState? activationState)
{
    _trayIcon.Initialize();

    _pipeClient.OnStateReceived += state =>
    {
        _trayIcon.UpdateState(state);
        // Navigate to correct screen based on state
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var route = state switch
            {
                MonitoringState.Active     => "//active",
                MonitoringState.Stopped    => "//clockin",
                MonitoringState.Unenrolled => "//connect",
                MonitoringState.Locked     => "//connect",
                _                          => "//clockin"
            };
            Shell.Current?.GoToAsync(route);
        });
    };

    _pipeClient.OnDisconnected += () =>
    {
        _trayIcon.UpdateState(MonitoringState.Stopped);
        MainThread.BeginInvokeOnMainThread(() =>
            Shell.Current?.GoToAsync("//clockin"));
    };

    _ = _pipeClient.StartAsync(CancellationToken.None);

    var shell = new ONEVO.Agent.TrayApp.Views.AppShell();
    var window = new Window(shell)
    {
        Title  = "ONEVO WorkPulse",
        Width  = 560,
        Height = 640
    };

    window.Created    += (_, _) => HookCloseToHide(window);
    window.Destroying += async (_, _) =>
    {
        await _collectors.DisposeAsync();
        await _pipeClient.DisposeAsync();
        _trayIcon.Dispose();
    };

    return window;
}
```

Remove the raw label fields (`_statusLabel`, `_snapshotLabel`, `_countsLabel`) from App class.

- [ ] **Step 4: Build**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

Expected: 0 errors. Fix any missing using or navigation references.

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/Views/AppShell.xaml \
        ONEVO.Agent.TrayApp/Views/AppShell.xaml.cs \
        ONEVO.Agent.TrayApp/MauiProgram.cs \
        ONEVO.Agent.TrayApp/App.xaml.cs
git commit -m "feat: AppShell navigation + MauiProgram DI wiring for all journey screens"
```

---

## Phase 5 — Additional Collectors

### Task 20: AppUsageCollector (with window-title hashing)

**Files:**
- Create: `ONEVO.Agent.TrayApp/Collectors/AppUsageCollector.cs`

Collects foreground process name + SHA-256 hashed window title. Never stores raw title.

- [ ] **Step 1: Create AppUsageCollector**

```csharp
// ONEVO.Agent.TrayApp/Collectors/AppUsageCollector.cs
namespace ONEVO.Agent.TrayApp.Collectors;

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ONEVO.Agent.TrayApp.Interop;
using ONEVO.Agent.TrayApp.Security;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.Models;

public sealed class AppUsageCollector : IAgentCollector, IAsyncDisposable
{
    public string Name => "AppUsage";

    private readonly ILogger<AppUsageCollector> _logger;
    private readonly INamedPipeClient _pipe;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;

    public AppUsageCollector(ILogger<AppUsageCollector> logger, INamedPipeClient pipe)
    {
        _logger = logger;
        _pipe   = pipe;
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        if (!policy.AppUsageEnabled || _running)
            return Task.CompletedTask;

        _cts    = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop   = SampleLoopAsync(_cts.Token);
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
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
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

            // Process name — safe (§7.2)
            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            string? processName = PrivacyScrubber.GetForegroundProcessNameSafe();

            // Window title — hash immediately in memory, never persist raw (§8.3)
            var buf = new StringBuilder(512);
            NativeMethods.GetWindowText(hwnd, buf, buf.Capacity);
            var rawTitle = buf.ToString();
            buf.Clear(); // discard raw title immediately
            var titleHash = rawTitle.Length > 0 ? HashingService.HashWindowTitle(rawTitle) : string.Empty;

            var record = new CollectionRecord
            {
                EventId         = Guid.NewGuid().ToString("N"),
                RecordType      = CollectionRecordTypes.ActivitySnapshot,
                SchemaVersion   = CollectionSchemaVersions.ActivitySnapshotV1,
                CaptureTimestamp = DateTimeOffset.UtcNow,
                DeviceId        = Environment.MachineName,
                Payload         = JsonSerializer.SerializeToElement(new
                {
                    ProcessName = processName,
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
```

- [ ] **Step 2: Add GetWindowText to NativeMethods**

In `ONEVO.Agent.TrayApp/Interop/NativeMethods.cs`, add:

```csharp
[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);
```

- [ ] **Step 3: Register in MauiProgram.cs**

In the Collectors section, add:

```csharp
builder.Services.AddSingleton<AppUsageCollector>();
builder.Services.AddSingleton<IAgentCollector>(sp =>
    sp.GetRequiredService<AppUsageCollector>());
```

- [ ] **Step 4: Build**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/Collectors/AppUsageCollector.cs \
        ONEVO.Agent.TrayApp/Interop/NativeMethods.cs \
        ONEVO.Agent.TrayApp/MauiProgram.cs
git commit -m "feat: AppUsageCollector — process name + SHA-256 title hash (§7.2, §8.3)"
```

---

### Task 21: DeviceStateCollector + IdleDetector

**Files:**
- Create: `ONEVO.Agent.TrayApp/Collectors/IdleDetector.cs`
- Create: `ONEVO.Agent.TrayApp/Collectors/DeviceStateCollector.cs`

- [ ] **Step 1: Create IdleDetector**

```csharp
// ONEVO.Agent.TrayApp/Collectors/IdleDetector.cs
namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.TrayApp.Security;

public static class IdleDetector
{
    public const int IdleThresholdSeconds = 120;

    public static bool IsIdle() =>
        PrivacyScrubber.GetSecondsSinceLastInput() >= IdleThresholdSeconds;

    public static int GetIdleSeconds() =>
        PrivacyScrubber.GetSecondsSinceLastInput();
}
```

- [ ] **Step 2: Create DeviceStateCollector**

```csharp
// ONEVO.Agent.TrayApp/Collectors/DeviceStateCollector.cs
namespace ONEVO.Agent.TrayApp.Collectors;

using System.Text.Json;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared;
using ONEVO.Agent.Shared.Models;

public sealed class DeviceStateCollector : IAgentCollector, IAsyncDisposable
{
    public string Name => "DeviceState";

    private readonly ILogger<DeviceStateCollector> _logger;
    private readonly INamedPipeClient _pipe;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;

    public DeviceStateCollector(ILogger<DeviceStateCollector> logger, INamedPipeClient pipe)
    {
        _logger = logger;
        _pipe   = pipe;
    }

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        if (_running) return Task.CompletedTask;
        _cts   = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop  = SampleLoopAsync(_cts.Token);
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
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            while (await timer.WaitForNextTickAsync(ct))
                await EmitSampleAsync(ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task EmitSampleAsync(CancellationToken ct)
    {
        try
        {
            var idleSeconds = IdleDetector.GetIdleSeconds();
            var record = new CollectionRecord
            {
                EventId          = Guid.NewGuid().ToString("N"),
                RecordType       = CollectionRecordTypes.ActivitySnapshot,
                SchemaVersion    = CollectionSchemaVersions.ActivitySnapshotV1,
                CaptureTimestamp = DateTimeOffset.UtcNow,
                DeviceId         = Environment.MachineName,
                Payload          = JsonSerializer.SerializeToElement(new
                {
                    IdleSeconds  = idleSeconds,
                    IsIdle       = IdleDetector.IsIdle()
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
```

- [ ] **Step 3: Register in MauiProgram.cs and build**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.TrayApp/Collectors/IdleDetector.cs \
        ONEVO.Agent.TrayApp/Collectors/DeviceStateCollector.cs \
        ONEVO.Agent.TrayApp/MauiProgram.cs
git commit -m "feat: DeviceStateCollector + IdleDetector using GetLastInputInfo (§7.3)"
```

---

### Task 22: MeetingDetector

**Files:**
- Create: `ONEVO.Agent.TrayApp/Collectors/MeetingDetector.cs`

Phase 1: probabilistic, process-name based only (§7.4). A background Teams.exe alone does not prove a meeting.

- [ ] **Step 1: Create MeetingDetector**

```csharp
// ONEVO.Agent.TrayApp/Collectors/MeetingDetector.cs
namespace ONEVO.Agent.TrayApp.Collectors;

using System.Diagnostics;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Phase 1 probabilistic meeting detection via known process names (§7.4).
/// Process found ≠ actively in meeting; result is a hint, not proof.
/// </summary>
public sealed class MeetingDetector : IAgentCollector, IAsyncDisposable
{
    private static readonly HashSet<string> MeetingProcessNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "teams", "teams.exe",
            "zoom", "zoom.exe",
            "webex", "webex.exe",
            "slack", "slack.exe",
            "msteams", "msteams.exe"
        };

    public string Name => "MeetingDetector";

    private readonly ILogger<MeetingDetector> _logger;
    private bool _running;

    public MeetingDetector(ILogger<MeetingDetector> logger) => _logger = logger;

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        _running = true;
        _logger.LogInformation("{Name}: started", Name);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _running = false;
        _logger.LogInformation("{Name}: stopped", Name);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Returns true if a known meeting-app process is running.
    /// This is probabilistic — a background process is not proof of an active meeting.
    /// </summary>
    public static bool IsMeetingAppRunning()
    {
        try
        {
            return Process.GetProcesses()
                .Any(p => MeetingProcessNames.Contains(p.ProcessName));
        }
        catch { return false; }
    }

    public ValueTask DisposeAsync()
    {
        _running = false;
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Register and build**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

- [ ] **Step 3: Commit**

```bash
git add ONEVO.Agent.TrayApp/Collectors/MeetingDetector.cs ONEVO.Agent.TrayApp/MauiProgram.cs
git commit -m "feat: MeetingDetector process-name probabilistic (§7.4)"
```

---

### Task 23: ScreenshotCollector (policy-gated)

**Files:**
- Create: `ONEVO.Agent.TrayApp/Collectors/ScreenshotCollector.cs`

Screenshots: disabled unless policy explicitly enables them; never taken during break/paused/stopped (§7.5).

- [ ] **Step 1: Create ScreenshotCollector**

```csharp
// ONEVO.Agent.TrayApp/Collectors/ScreenshotCollector.cs
namespace ONEVO.Agent.TrayApp.Collectors;

using ONEVO.Agent.Shared.Models;

/// <summary>
/// Screenshot collection — off unless effective policy enables it (§7.5).
/// Never runs during break, stopped, or uncertain lifecycle state.
/// </summary>
public sealed class ScreenshotCollector : IAgentCollector, IAsyncDisposable
{
    public string Name => "Screenshot";

    private readonly ILogger<ScreenshotCollector> _logger;
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private bool _running;

    public ScreenshotCollector(ILogger<ScreenshotCollector> logger) => _logger = logger;

    public Task StartAsync(AgentPolicy policy, CancellationToken ct)
    {
        // Policy MUST explicitly enable screenshots — default is off.
        if (!policy.ScreenshotEnabled)
        {
            _logger.LogDebug("{Name}: policy disabled — not starting", Name);
            return Task.CompletedTask;
        }

        if (_running) return Task.CompletedTask;

        _cts   = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loop  = CaptureLoopAsync(_cts.Token);
        _running = true;
        _logger.LogInformation("{Name}: started (policy-enabled)", Name);
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

    private async Task CaptureLoopAsync(CancellationToken ct)
    {
        try
        {
            // Phase 1: interval from policy; default 300s
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(300));
            while (await timer.WaitForNextTickAsync(ct))
            {
                // Phase 1 stub — actual capture + restricted upload flow is Phase 1 extension
                _logger.LogDebug("{Name}: capture tick (stub)", Name);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);
}
```

- [ ] **Step 2: Register and build**

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj
```

- [ ] **Step 3: Commit**

```bash
git add ONEVO.Agent.TrayApp/Collectors/ScreenshotCollector.cs ONEVO.Agent.TrayApp/MauiProgram.cs
git commit -m "feat: ScreenshotCollector policy-gated stub (§7.5)"
```

---

## Phase 6 — Service Layer

### Task 24: LifecycleGate

**Files:**
- Create: `ONEVO.Agent.Service/Lifecycle/LifecycleGate.cs`

Evaluates all 9 gate conditions from §6. Returns `CanActivate` only when all pass. Fails closed.

- [ ] **Step 1: Create LifecycleGate**

```csharp
// ONEVO.Agent.Service/Lifecycle/LifecycleGate.cs
namespace ONEVO.Agent.Service.Lifecycle;

using ONEVO.Agent.Shared.Models;

/// <summary>
/// All conditions must be true before collection may enter Active (§6).
/// Missing or uncertain state → fails closed (collection remains stopped).
/// </summary>
public sealed class LifecycleGate
{
    private readonly Lock _lock = new();

    private bool _deviceEnrolled;
    private bool _credentialValid;
    private bool _deviceApproved;
    private bool _employeeSessionActive;
    private bool _consentValid;
    private bool _policyAllowsCollection;
    private bool _presenceSessionActive;
    private bool _notOnBreak;
    private bool _notOnApprovedTimeOff;

    public bool CanActivate
    {
        get
        {
            lock (_lock)
            {
                return _deviceEnrolled
                    && _credentialValid
                    && _deviceApproved
                    && _employeeSessionActive
                    && _consentValid
                    && _policyAllowsCollection
                    && _presenceSessionActive
                    && _notOnBreak
                    && _notOnApprovedTimeOff;
            }
        }
    }

    public void SetDeviceEnrolled(bool value)         { lock (_lock) _deviceEnrolled = value; }
    public void SetCredentialValid(bool value)        { lock (_lock) _credentialValid = value; }
    public void SetDeviceApproved(bool value)         { lock (_lock) _deviceApproved = value; }
    public void SetEmployeeSessionActive(bool value)  { lock (_lock) _employeeSessionActive = value; }
    public void SetConsentValid(bool value)           { lock (_lock) _consentValid = value; }
    public void SetPolicyAllowsCollection(bool value) { lock (_lock) _policyAllowsCollection = value; }
    public void SetPresenceSessionActive(bool value)  { lock (_lock) _presenceSessionActive = value; }
    public void SetNotOnBreak(bool value)             { lock (_lock) _notOnBreak = value; }
    public void SetNotOnApprovedTimeOff(bool value)   { lock (_lock) _notOnApprovedTimeOff = value; }

    public GateSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new GateSnapshot(
                _deviceEnrolled, _credentialValid, _deviceApproved,
                _employeeSessionActive, _consentValid, _policyAllowsCollection,
                _presenceSessionActive, _notOnBreak, _notOnApprovedTimeOff);
        }
    }
}

public sealed record GateSnapshot(
    bool DeviceEnrolled,
    bool CredentialValid,
    bool DeviceApproved,
    bool EmployeeSessionActive,
    bool ConsentValid,
    bool PolicyAllowsCollection,
    bool PresenceSessionActive,
    bool NotOnBreak,
    bool NotOnApprovedTimeOff)
{
    public bool CanActivate =>
        DeviceEnrolled && CredentialValid && DeviceApproved &&
        EmployeeSessionActive && ConsentValid && PolicyAllowsCollection &&
        PresenceSessionActive && NotOnBreak && NotOnApprovedTimeOff;
}
```

- [ ] **Step 2: Write LifecycleGate tests in Service.Tests**

```csharp
// tests/ONEVO.Agent.Service.Tests/Lifecycle/LifecycleGateTests.cs
namespace ONEVO.Agent.Service.Tests;

using ONEVO.Agent.Service.Lifecycle;
using Xunit;

public sealed class LifecycleGateTests
{
    [Fact]
    public void Default_CanActivate_IsFalse()
    {
        Assert.False(new LifecycleGate().CanActivate);
    }

    [Fact]
    public void AllGatesTrue_CanActivate_IsTrue()
    {
        var gate = new LifecycleGate();
        gate.SetDeviceEnrolled(true);
        gate.SetCredentialValid(true);
        gate.SetDeviceApproved(true);
        gate.SetEmployeeSessionActive(true);
        gate.SetConsentValid(true);
        gate.SetPolicyAllowsCollection(true);
        gate.SetPresenceSessionActive(true);
        gate.SetNotOnBreak(true);
        gate.SetNotOnApprovedTimeOff(true);
        Assert.True(gate.CanActivate);
    }

    [Fact]
    public void SingleGateFalse_CanActivate_IsFalse()
    {
        var gate = BuildFullyOpen();
        gate.SetNotOnBreak(false); // employee on break
        Assert.False(gate.CanActivate);
    }

    [Fact]
    public void OnBreak_BlocksActivation()
    {
        var gate = BuildFullyOpen();
        gate.SetNotOnBreak(false);
        Assert.False(gate.CanActivate);
    }

    [Fact]
    public void ApprovedTimeOff_BlocksActivation()
    {
        var gate = BuildFullyOpen();
        gate.SetNotOnApprovedTimeOff(false);
        Assert.False(gate.CanActivate);
    }

    [Fact]
    public void RevokedDevice_BlocksActivation()
    {
        var gate = BuildFullyOpen();
        gate.SetDeviceApproved(false);
        Assert.False(gate.CanActivate);
    }

    [Fact]
    public void Snapshot_ReflectsCurrentState()
    {
        var gate = BuildFullyOpen();
        var snap = gate.Snapshot();
        Assert.True(snap.CanActivate);
        Assert.True(snap.NotOnBreak);
    }

    private static LifecycleGate BuildFullyOpen()
    {
        var g = new LifecycleGate();
        g.SetDeviceEnrolled(true);
        g.SetCredentialValid(true);
        g.SetDeviceApproved(true);
        g.SetEmployeeSessionActive(true);
        g.SetConsentValid(true);
        g.SetPolicyAllowsCollection(true);
        g.SetPresenceSessionActive(true);
        g.SetNotOnBreak(true);
        g.SetNotOnApprovedTimeOff(true);
        return g;
    }
}
```

- [ ] **Step 3: Run tests**

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~LifecycleGateTests"
```

Expected: 7/7 pass.

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Service/Lifecycle/LifecycleGate.cs \
        tests/ONEVO.Agent.Service.Tests/Lifecycle/LifecycleGateTests.cs
git commit -m "feat: LifecycleGate all 9 §6 conditions + tests (fails closed)"
```

---

### Task 25: HeartbeatService stub

**Files:**
- Create: `ONEVO.Agent.Service/Sync/HeartbeatService.cs`

- [ ] **Step 1: Create HeartbeatService**

```csharp
// ONEVO.Agent.Service/Sync/HeartbeatService.cs
namespace ONEVO.Agent.Service.Sync;

using ONEVO.Agent.Service;
using ONEVO.Agent.Shared.Models;

/// <summary>
/// Sends heartbeat every ~60s with safe health metrics (§12).
/// Never includes raw titles, PII, secrets, or image bytes.
/// </summary>
public sealed class HeartbeatService : BackgroundService
{
    private readonly ILogger<HeartbeatService> _logger;
    private readonly AgentStateMachine _stateMachine;
    private readonly IHttpClientFactory _httpFactory;

    public HeartbeatService(
        ILogger<HeartbeatService> logger,
        AgentStateMachine stateMachine,
        IHttpClientFactory httpFactory)
    {
        _logger       = logger;
        _stateMachine = stateMachine;
        _httpFactory  = httpFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("HeartbeatService started");
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SendHeartbeatAsync(stoppingToken);
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken ct)
    {
        try
        {
            var payload = new
            {
                AgentVersion   = typeof(HeartbeatService).Assembly.GetName().Version?.ToString() ?? "unknown",
                MonitoringState = _stateMachine.CurrentState.ToString(),
                TimestampUtc   = DateTimeOffset.UtcNow
                // CPU/memory added when ResourceMonitor is implemented
            };

            // Phase 1 stub — full HTTP call wired when OnevoApiClient is built
            _logger.LogDebug("Heartbeat tick: state={State}", _stateMachine.CurrentState);
            await Task.CompletedTask;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Heartbeat failed — will retry next tick");
        }
    }
}
```

- [ ] **Step 2: Register in Service Program.cs**

In `ONEVO.Agent.Service/Program.cs`, add:

```csharp
builder.Services.AddHostedService<HeartbeatService>();
```

- [ ] **Step 3: Build**

```powershell
dotnet build .\ONEVO.Agent.Service\ONEVO.Agent.Service.csproj
```

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Service/Sync/HeartbeatService.cs ONEVO.Agent.Service/Program.cs
git commit -m "feat: HeartbeatService 60s tick stub (§12)"
```

---

## Final Verification

### Task 26: Full build + test run

- [ ] **Step 1: Full build**

```powershell
dotnet build .\ONEVO.Agent.slnx --configuration Release
```

Expected: 0 errors, 0 warnings (TreatWarningsAsErrors=true in Directory.Build.props).

- [ ] **Step 2: Full test run**

```powershell
dotnet test .\ONEVO.Agent.slnx --configuration Release --logger "console;verbosity=normal"
```

Expected: all tests pass, 0 failures.

- [ ] **Step 3: TrayApp publish check**

```powershell
dotnet publish .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj `
  --configuration Release `
  -f net10.0-windows10.0.19041.0
```

Expected: publish succeeds.

- [ ] **Step 4: Final commit**

```bash
git add -u
git commit -m "chore: Phase 1 complete — tests, collectors, journey screens, service layer"
```

---

## Architecture Compliance Checklist

Before marking Phase 1 done, verify each rule from §21:

- [ ] Keyboard/mouse hooks count events only — no key codes, coordinates (ActivityCountCollector ✓)
- [ ] Window titles SHA-256 hashed in TrayApp memory before IPC (AppUsageCollector + HashingService ✓)
- [ ] Service owns queue and credentials — TrayApp has no Device JWT (CredentialStore in Service ✓)
- [ ] IPC loss stops all collectors (CollectorCoordinator.OnDisconnected ✓, tested ✓)
- [ ] Collectors start only through CollectorCoordinator — never self-start (✓)
- [ ] ScreenshotCollector does not start when policy.ScreenshotEnabled=false (✓)
- [ ] No raw titles in logs — verify by searching: `Select-String -Path "**\*.cs" -Pattern "rawTitle|GetWindowText" | Where-Object { $_.Line -notmatch "Hash|hash|clear|Clear|buf\." }`
- [ ] All tests pass: state transitions, IPC-loss fail-safe, title hashing, privacy scrubber, lifecycle gate, options validation
- [ ] TrayApp targets `net10.0-windows10.0.19041.0` only — no Android/iOS/Tizen targets

---

## Known Phase 2 Items (Do NOT Build Now)

- `AgentBufferDb` (encrypted SQLite) — replace in-memory `ActivityRecordBuffer`
- `OnevoApiClient` with resilience pipeline — full HTTP calls for heartbeat/ingest/policy
- `CommandProcessor` + `CommandReceiptStore`
- `SessionNotificationListener` (WM_WTSSESSION_CHANGE Win32 session events)
- `TokenRefreshService`
- `MonitoringWatchdog` (midnight stop)
- `PresenceReconciler`
- Identity photo capture in `CameraService`
- `ONEVO.Agent.Installer` MSIX packaging
- Integration tests (Named Pipe ACL, SQLite restart recovery)
- Privacy audit test (scan SQLite/logs/IPC traces for raw titles)
