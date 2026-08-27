# Agent Consumes Admin-Configurable Idle Threshold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the hardcoded `Constants.InactivityThresholdSeconds` (currently `120`) with the per-tenant `idle_threshold_minutes` value the backend now resolves and returns on `AgentPolicy` (see `2026-08-19-admin-configurable-idle-threshold.md` in `HRMS-Backend-v1`, a prerequisite for this plan), so an HR admin's configured minutes actually change TrayApp behavior instead of being silently ignored.

**Architecture:** `AgentPolicy` (Shared, already flows through `OnevoApiClient` → `PolicyCache`/`PolicySyncService` → Named Pipe `PolicyPush` → `CollectorCoordinator.StartAsync` unchanged) gains one new field, `IdleThresholdMinutes`, with an in-record default of `5` so every existing `new AgentPolicy { ... }` fixture that doesn't set it keeps compiling and behaving sanely. `InactivityScreenshotCollector` captures the effective threshold (in seconds) once in `StartAsync` instead of reading the shared `Constants` value on every tick, and derives the Allow/Skip response window as 90% of that threshold (same ratio the old fixed `120s`/`108s` pair used) instead of a second fixed constant. `WindowsInactivityPromptService`'s notification body text is derived from the actual `idleFor` passed into `PromptAsync` per call, so it can never drift out of sync with whatever threshold actually fired it. `InactivityEvidenceHandler` (Service) reads the same field off `PolicyCache.Current` for its `idle_too_short` server-side sanity check. `Constants.InactivityThresholdSeconds`/`InactivityPromptExpirySeconds` are deleted once nothing references them.

**Tech Stack:** C# / .NET 10, .NET MAUI (Windows), ONEVO.Agent.Service (Windows Service), xUnit.

**Prerequisite:** The backend plan `2026-08-19-admin-configurable-idle-threshold.md` must already be deployed (adds `idle_threshold_minutes` to `TrayAgentPolicyDto`) before Task 3 of this plan is meaningful — Tasks 1-2 (adding the field to `AgentPolicy` with a safe default, and updating the collector/prompt service to read from policy instead of `Constants`) are self-contained and can be built first.

---

## File Structure

| File | Responsibility |
|---|---|
| `ONEVO.Agent.Shared/Models/AgentPolicy.cs` | Add `int IdleThresholdMinutes` with default `5` (modify) |
| `ONEVO.Agent.Shared/Constants.cs` | Remove `InactivityThresholdSeconds`/`InactivityPromptExpirySeconds` (modify) |
| `ONEVO.Agent.Service/Api/OnevoApiClient.cs` | Map `idle_threshold_minutes` from the backend payload (modify) |
| `ONEVO.Agent.TrayApp/Collectors/InactivityScreenshotCollector.cs` | Read threshold from policy, not `Constants` (modify) |
| `ONEVO.Agent.TrayApp/Services/WindowsInactivityPromptService.cs` | Derive notification body from `idleFor` (modify) |
| `ONEVO.Agent.Service/Buffer/InactivityEvidenceHandler.cs` | Read threshold from `PolicyCache.Current`, not `Constants` (modify) |
| `tests/ONEVO.Agent.TrayApp.Tests/Collectors/InactivityScreenshotCollectorTests.cs` | Set `IdleThresholdMinutes` explicitly, add a threshold-change test (modify) |
| `tests/ONEVO.Agent.TrayApp.Tests/Services/WindowsInactivityPromptServiceTests.cs` | **Create** — notification body text test (this class has no existing test file; it is a thin Windows App SDK wrapper, so this test only covers the pure body-text derivation, extracted into a small internal static method) |

---

### Task 1: `AgentPolicy` gains `IdleThresholdMinutes`

**Files:**
- Modify: `ONEVO.Agent.Shared/Models/AgentPolicy.cs`

- [ ] **Step 1: Add the field with a safe default**

Replace the whole file with:

```csharp
namespace ONEVO.Agent.Shared.Models;

public sealed record AgentPolicy
{
    public required string Version { get; init; }
    public bool ActivitySignalEnabled { get; init; }
    public bool AppUsageEnabled { get; init; }
    public bool ScreenshotEnabled { get; init; }
    public bool CameraVerificationEnabled { get; init; }
    public bool InactivityScreenshotEnabled { get; init; }

    /// <summary>
    /// Minutes of continuous mouse/keyboard inactivity before the "Activity check" screenshot
    /// prompt fires. Defaults to 5 so every existing test/local-default fixture that constructs
    /// an AgentPolicy without setting this explicitly keeps a sane, non-zero value (0 would mean
    /// "prompt on every poll tick", which is not a safe default for anything).
    /// </summary>
    public int IdleThresholdMinutes { get; init; } = 5;

    public DateTimeOffset ValidUntil { get; init; }
}
```

- [ ] **Step 2: Build to confirm no compile errors**

Run: `dotnet build ONEVO.Agent.Shared/ONEVO.Agent.Shared.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Run the full Shared test suite to confirm nothing broke**

Run: `dotnet test tests/ONEVO.Agent.Shared.Tests/ONEVO.Agent.Shared.Tests.csproj`
Expected: all tests still pass (the new field's in-record default means every existing `new AgentPolicy { ... }` fixture across the whole solution keeps compiling and behaving as before).

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Shared/Models/AgentPolicy.cs
git commit -m "feat: add IdleThresholdMinutes to AgentPolicy with a safe default"
```

---

### Task 2: `InactivityScreenshotCollector` reads threshold from policy

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Collectors/InactivityScreenshotCollector.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/Collectors/InactivityScreenshotCollectorTests.cs`

- [ ] **Step 1: Write the failing test**

In `InactivityScreenshotCollectorTests.cs`, update the `EnabledPolicy` helper to keep the existing 120s-boundary tests passing unchanged (2 minutes × 60 = 120 seconds — the exact values every existing test already uses):

```csharp
    private static AgentPolicy EnabledPolicy(DateTimeOffset? validUntil = null) => new()
    {
        Version = "v1",
        ActivitySignalEnabled = true,
        AppUsageEnabled = false,
        ScreenshotEnabled = true,
        InactivityScreenshotEnabled = true,
        CameraVerificationEnabled = false,
        IdleThresholdMinutes = 2,
        ValidUntil = validUntil ?? DateTimeOffset.UtcNow.AddHours(1)
    };
```

Then add this new test after `Prompts_once_per_threshold_bucket`:

```csharp
    [Fact]
    public async Task Prompts_at_the_policys_configured_threshold_not_a_hardcoded_one()
    {
        var tenMinutePolicy = EnabledPolicy() with { IdleThresholdMinutes = 10 };
        await _sut.StartAsync(tenMinutePolicy, default);

        // 120s (the old hardcoded default, and still the value every other test in this file
        // uses) must NOT fire a prompt once the collector is configured for 10 minutes.
        await _sut.EvaluateAsync(120, DateTimeOffset.Parse("2026-08-10T01:05:00Z"), default);
        Assert.Equal(0, _prompt.RequestCount);

        // The policy's actual configured threshold (600s) must fire it.
        await _sut.EvaluateAsync(600, DateTimeOffset.Parse("2026-08-10T01:10:00Z"), default);
        Assert.Equal(1, _prompt.RequestCount);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~Prompts_at_the_policys_configured_threshold_not_a_hardcoded_one"`
Expected: FAIL — with today's code every policy is evaluated against the hardcoded `Constants.InactivityThresholdSeconds` (120), so idleSeconds=120 fires a prompt regardless of `IdleThresholdMinutes`, making `_prompt.RequestCount` already `1` at the first assertion.

- [ ] **Step 3: Capture the threshold in `StartAsync` and use it instead of `Constants`**

In `InactivityScreenshotCollector.cs`, add a new field next to the other `_gate`-guarded fields:

```csharp
    private bool _started;
    private bool _running;
    private AgentPolicy? _policy;
    private int _idleThresholdSeconds;
    private int _promptExpirySeconds;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
```

In `StartAsync`, inside the `lock (_gate) { ... }` block, add the two new assignments right after `_policy = policy;`:

```csharp
            _started = true;
            _running = true;
            _policy = policy;
            _idleThresholdSeconds = policy.IdleThresholdMinutes * 60;
            // Same 90% ratio the old fixed 120s/108s pair used: the expiry window must end
            // before the next bucket boundary, or a slow-to-respond employee could see two
            // prompts stack while still inside a single continuous idle period.
            _promptExpirySeconds = (int)(_idleThresholdSeconds * 0.9);
            _lastPromptedBucket = 0;
            _lastIdleSeconds = 0;
```

In `EvaluateAsync`, replace:

```csharp
                var bucket = idleSeconds / Constants.InactivityThresholdSeconds;
```

with:

```csharp
                var bucket = idleSeconds / _idleThresholdSeconds;
```

In `RunPromptWorkflowAsync`, replace:

```csharp
                decision = await _promptService.PromptAsync(
                        attemptId, idleFor, TimeSpan.FromSeconds(Constants.InactivityPromptExpirySeconds), ct)
                    .ConfigureAwait(false);
```

with:

```csharp
                decision = await _promptService.PromptAsync(
                        attemptId, idleFor, TimeSpan.FromSeconds(_promptExpirySeconds), ct)
                    .ConfigureAwait(false);
```

`_idleThresholdSeconds`/`_promptExpirySeconds` are only ever read from `EvaluateAsync`/`RunPromptWorkflowAsync`, both of which already only run while `_running` is true (set under the same `_gate` lock as the two new fields in `StartAsync`), so no extra locking is needed beyond what already guards `_policy`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~InactivityScreenshotCollectorTests"`
Expected: PASS — all 18 tests (the 17 pre-existing ones, now driven by the explicit `IdleThresholdMinutes = 2` in `EnabledPolicy`, plus the new one).

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/Collectors/InactivityScreenshotCollector.cs tests/ONEVO.Agent.TrayApp.Tests/Collectors/InactivityScreenshotCollectorTests.cs
git commit -m "feat: InactivityScreenshotCollector reads idle threshold from policy"
```

---

### Task 3: Notification body text derived from the actual firing threshold

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Services/WindowsInactivityPromptService.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Services/WindowsInactivityPromptServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `WindowsInactivityPromptServiceTests.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.Tests.Services;

using ONEVO.Agent.TrayApp.Services;

public sealed class WindowsInactivityPromptServiceTests
{
    [Theory]
    [InlineData(120, "No keyboard or mouse activity was detected for 2 minutes. Allow a screenshot of all connected monitors?")]
    [InlineData(600, "No keyboard or mouse activity was detected for 10 minutes. Allow a screenshot of all connected monitors?")]
    [InlineData(121, "No keyboard or mouse activity was detected for 2 minutes. Allow a screenshot of all connected monitors?")]
    public void BuildNotificationBody_UsesActualIdleMinutes(int idleSeconds, string expected)
    {
        var actual = WindowsInactivityPromptService.BuildNotificationBody(TimeSpan.FromSeconds(idleSeconds));

        Assert.Equal(expected, actual);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~WindowsInactivityPromptServiceTests"`
Expected: FAIL to compile — `WindowsInactivityPromptService.BuildNotificationBody` does not exist.

- [ ] **Step 3: Extract a testable static method and use it from `Show`**

In `WindowsInactivityPromptService.cs`, replace:

```csharp
    private const string NotificationTitle = "Activity check";

    // Derived from Constants.InactivityThresholdSeconds so the copy can never drift out of sync
    // with the actual trigger threshold again (it previously hardcoded "5 minutes" as a literal
    // string, which went stale the moment the threshold was changed to 2 minutes).
    private static readonly string NotificationBody =
        $"No keyboard or mouse activity was detected for {Constants.InactivityThresholdSeconds / 60} minutes. Allow a screenshot of all connected monitors?";
```

with:

```csharp
    private const string NotificationTitle = "Activity check";

    /// <summary>
    /// Derived from the actual idle duration that triggered this specific prompt (floor'd to
    /// whole minutes) rather than any shared constant or cached policy value, so the copy can
    /// never drift out of sync with the real per-tenant configured threshold — it previously
    /// hardcoded "5 minutes" as a literal string, which went stale the moment an admin (or a
    /// test) configured a different threshold.
    /// </summary>
    internal static string BuildNotificationBody(TimeSpan idleFor) =>
        $"No keyboard or mouse activity was detected for {(int)idleFor.TotalMinutes} minutes. Allow a screenshot of all connected monitors?";
```

Then replace the `Show` method's signature and body — replace:

```csharp
    private static void Show(Guid attemptId, TimeSpan expiresIn)
    {
        var attempt = attemptId.ToString();

        var notification = new AppNotificationBuilder()
            .AddText(NotificationTitle)
            .AddText(NotificationBody)
```

with:

```csharp
    private static void Show(Guid attemptId, TimeSpan expiresIn, TimeSpan idleFor)
    {
        var attempt = attemptId.ToString();

        var notification = new AppNotificationBuilder()
            .AddText(NotificationTitle)
            .AddText(BuildNotificationBody(idleFor))
```

Then update the two call sites. `PromptAsync` already receives `idleFor` as a parameter — replace:

```csharp
        try
        {
            Show(attemptId, expiresIn);
            BootLog($"Show() succeeded for attempt {attemptId}");
        }
```

with:

```csharp
        try
        {
            Show(attemptId, expiresIn, idleFor);
            BootLog($"Show() succeeded for attempt {attemptId}");
        }
```

- [ ] **Step 4: Remove the now-unused `using ONEVO.Agent.Shared;`**

`Constants` is no longer referenced in this file. Remove the line `using ONEVO.Agent.Shared;` from the top of `WindowsInactivityPromptService.cs` (it will otherwise produce an unused-using warning, or a compile error if the project treats warnings as errors).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~WindowsInactivityPromptServiceTests"`
Expected: PASS — all three `[InlineData]` cases, including the `121` → floors to `2` case (matching `InactivityScreenshotCollector`'s bucket math, which only ever calls `PromptAsync` with `idleFor >= threshold`, never below it).

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/WindowsInactivityPromptService.cs tests/ONEVO.Agent.TrayApp.Tests/Services/WindowsInactivityPromptServiceTests.cs
git commit -m "feat: derive inactivity notification body from actual idle duration"
```

---

### Task 4: Service-side `idle_too_short` check reads policy, not `Constants`

**Files:**
- Modify: `ONEVO.Agent.Service/Buffer/InactivityEvidenceHandler.cs`

- [ ] **Step 1: Replace the hardcoded threshold with the synced policy's value**

In `InactivityEvidenceHandler.cs`, replace:

```csharp
        if (start.Attempt.IdleDurationSeconds < Constants.InactivityThresholdSeconds)
            return new EvidenceTransferAckPayload(start.Attempt.AttemptId, false, "idle_too_short");
```

with:

```csharp
        if (start.Attempt.IdleDurationSeconds < _policyCache.Current.IdleThresholdMinutes * 60)
            return new EvidenceTransferAckPayload(start.Attempt.AttemptId, false, "idle_too_short");
```

`_policyCache` is already an injected constructor dependency of this class (see the existing `PolicyCache _policyCache` field) and `PolicyCache.Current` already returns the live `AgentPolicy` — the same one the TrayApp-side collector in Task 2 reads `IdleThresholdMinutes` from, kept in sync by the same `PolicyPush` IPC message, so this validates against exactly the threshold the TrayApp was actually running under when it started that attempt.

- [ ] **Step 2: Build to confirm no compile errors**

Run: `dotnet build ONEVO.Agent.Service/ONEVO.Agent.Service.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Run the Service test suite**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj`
Expected: all tests pass (no existing test exercises this exact rejection path — confirmed by `grep -rn "idle_too_short" tests/` returning no matches — so this is a behavior-only change with no test fixtures to update).

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Service/Buffer/InactivityEvidenceHandler.cs
git commit -m "feat: validate idle_too_short against the synced policy threshold, not a hardcoded constant"
```

---

### Task 5: Map the backend's `idle_threshold_minutes` field

**Files:**
- Modify: `ONEVO.Agent.Service/Api/OnevoApiClient.cs`

> Requires the backend plan (`2026-08-19-admin-configurable-idle-threshold.md`) to already be deployed — `TrayAgentPolicyDto` must already return `idle_threshold_minutes` in its JSON response, or `payload.IdleThresholdMinutes` below will always deserialize to `0` (System.Text.Json defaults missing int fields to `0`, not the record's C# default, because this payload type is deserialized from JSON — the in-record `= 5` default from Task 1 only applies to `new AgentPolicy { ... }` object-initializer construction, not JSON deserialization of `TrayAgentPolicyPayload`).

- [ ] **Step 1: Add the field to the wire-format payload record**

In `OnevoApiClient.cs`, replace:

```csharp
public sealed record TrayAgentPolicyPayload(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("activity_signal_enabled")] bool ActivitySignalEnabled,
    [property: JsonPropertyName("app_usage_enabled")] bool AppUsageEnabled,
    [property: JsonPropertyName("screenshot_enabled")] bool ScreenshotEnabled,
    [property: JsonPropertyName("inactivity_screenshot_enabled")] bool InactivityScreenshotEnabled,
    [property: JsonPropertyName("camera_verification_enabled")] bool CameraVerificationEnabled,
    [property: JsonPropertyName("valid_until")] DateTimeOffset ValidUntil);
```

with:

```csharp
public sealed record TrayAgentPolicyPayload(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("activity_signal_enabled")] bool ActivitySignalEnabled,
    [property: JsonPropertyName("app_usage_enabled")] bool AppUsageEnabled,
    [property: JsonPropertyName("screenshot_enabled")] bool ScreenshotEnabled,
    [property: JsonPropertyName("inactivity_screenshot_enabled")] bool InactivityScreenshotEnabled,
    [property: JsonPropertyName("camera_verification_enabled")] bool CameraVerificationEnabled,
    [property: JsonPropertyName("idle_threshold_minutes")] int IdleThresholdMinutes,
    [property: JsonPropertyName("valid_until")] DateTimeOffset ValidUntil);
```

- [ ] **Step 2: Map it into the `AgentPolicy` construction**

Replace:

```csharp
        var policy = new AgentPolicy
        {
            Version = payload.Version,
            ActivitySignalEnabled = payload.ActivitySignalEnabled,
            AppUsageEnabled = payload.AppUsageEnabled,
            ScreenshotEnabled = payload.ScreenshotEnabled,
            InactivityScreenshotEnabled = payload.InactivityScreenshotEnabled,
            CameraVerificationEnabled = payload.CameraVerificationEnabled,
            ValidUntil = payload.ValidUntil
        };
```

with:

```csharp
        var policy = new AgentPolicy
        {
            Version = payload.Version,
            ActivitySignalEnabled = payload.ActivitySignalEnabled,
            AppUsageEnabled = payload.AppUsageEnabled,
            ScreenshotEnabled = payload.ScreenshotEnabled,
            InactivityScreenshotEnabled = payload.InactivityScreenshotEnabled,
            CameraVerificationEnabled = payload.CameraVerificationEnabled,
            IdleThresholdMinutes = payload.IdleThresholdMinutes,
            ValidUntil = payload.ValidUntil
        };
```

- [ ] **Step 3: Build and run the Service test suite**

Run: `dotnet build ONEVO.Agent.Service/ONEVO.Agent.Service.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

Run: `dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj`
Expected: all tests pass. If any test in `OnevoApiClientTests.cs` (if one exists — check with `grep -rn "TrayAgentPolicyPayload" tests/ONEVO.Agent.Service.Tests/`) hand-constructs a `TrayAgentPolicyPayload` positionally without the new field, it will now fail to compile — add `0` as the new fifth-from-last positional argument (or name the argument `IdleThresholdMinutes: 0`) to fix it, matching whatever value makes that specific test's existing assertions still true (most such tests don't assert on this field at all, so `0` or `5` both work — prefer `5` to match the realistic default used everywhere else in this plan).

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Service/Api/OnevoApiClient.cs
git commit -m "feat: map idle_threshold_minutes from the backend policy response"
```

---

### Task 6: Delete the dead constants

**Files:**
- Modify: `ONEVO.Agent.Shared/Constants.cs`

- [ ] **Step 1: Confirm nothing still references them**

Run: `grep -rn "InactivityThresholdSeconds\|InactivityPromptExpirySeconds" --include="*.cs" .` from the `tray_app_maui` repo root.
Expected: **zero** matches outside `Constants.cs` itself. If Tasks 2-4 above were completed, this should already be true. If anything still matches, stop and finish updating that call site first — do not delete the constants while something still depends on them.

- [ ] **Step 2: Remove the two constants**

In `Constants.cs`, delete these two blocks entirely:

```csharp
    /// <summary>Seconds of continuous no mouse/keyboard input before the inactivity prompt is shown.</summary>
    public const int InactivityThresholdSeconds = 120;

    /// <summary>Seconds the employee has to respond to the Allow/Skip inactivity prompt before it times out.</summary>
    public const int InactivityPromptExpirySeconds = 108;
```

- [ ] **Step 3: Build the whole solution**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)` across `ONEVO.Agent.Shared`, `ONEVO.Agent.Service`, `ONEVO.Agent.TrayApp`, and every test project — confirming Task 6 Step 1's grep really did find every reference.

- [ ] **Step 4: Commit**

```bash
git add ONEVO.Agent.Shared/Constants.cs
git commit -m "chore: remove dead InactivityThresholdSeconds/InactivityPromptExpirySeconds constants"
```

---

### Task 7: Full-suite verification

- [ ] **Step 1: Run every test project**

Run:
```bash
dotnet test tests/ONEVO.Agent.Shared.Tests/ONEVO.Agent.Shared.Tests.csproj
dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj
dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj
```
Expected: all pass, 0 failed, in all three projects.

- [ ] **Step 2: Manual smoke test end-to-end**

With the backend plan already deployed and an admin having set, e.g., `idleThresholdMinutes: 1` for the test tenant via `PUT /api/v1/monitoring/settings`: launch the Service and TrayApp, clock in, leave the mouse/keyboard untouched for 1 minute. Expected: the "Activity check" toast fires at ~60s (not 120s), and its body text reads "...detected for 1 minutes..." (not "2 minutes"). Click Allow — expected: a screenshot is captured and submitted (this exercises the `NotificationActivationRouter` semicolon-separator fix from the current session, unaffected by this plan).
