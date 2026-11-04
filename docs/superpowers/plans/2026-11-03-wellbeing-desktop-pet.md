# Wellbeing Desktop Pet Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a Windows desktop pet that gives an actionable four-hour continuous-work reminder, starts the existing attendance break safely, and opens the existing WorkPulse tray window when clicked.

**Architecture:** The backend remains the source of truth for four-hour active continuity and pending reminder delivery. The tray adds an acceptance-aware wellbeing presenter, a shared safe break action, and a WinForms transparent overlay controlled by authoritative pipe state. Existing named-pipe lifecycle and backend attendance endpoints are reused so the portal, tray, and pet converge on one break state.

**Tech Stack:** .NET 10, ASP.NET Core/EF Core backend, .NET MAUI WinUI tray app, Windows Forms overlay/NotifyIcon, Windows App SDK notifications, xUnit, FluentAssertions, Moq.

**Spec:** `docs/superpowers/specs/2026-11-03-wellbeing-desktop-pet-design.md`

## Global Constraints

- Continuous active-work threshold is exactly 240 minutes.
- A device-state sample gap greater than 90 seconds resets continuity; exactly 90 seconds remains continuous.
- Backend lookback is five hours.
- The pet is Windows-only, bundled in the existing tray executable, and appears only after enrollment.
- Pet frame contract is 160x160 transparent PNG, `pet_<state>_<frame>.png` for `idle`, `working`, `break_due`, `on_break`, and `offline`.
- Pet/toast break actions bypass confirmation but must execute the existing collector pre-stop/drain sequence before `StartBreak`.
- UI must never enter OnBreak optimistically; only an authoritative lifecycle/status response may do so.
- Backend wellness acknowledgement occurs only after the tray accepts local presentation.
- Existing generic notifications, inactivity prompts, main-window break confirmation, clock-out, and tray-icon behavior must remain unchanged.
- Preserve all pre-existing uncommitted changes in both repositories. The currently dirty tray files include `MauiProgram.cs`, `NotificationService.cs`, `WindowsInactivityPromptService.cs`, and `ActiveSessionViewModel.cs`; patch them surgically and never discard or commit unrelated hunks.

## Review Focus

- A tray crash between IPC broadcast and acceptance must leave the backend notification pending; Task 2 adds the acceptance protocol tests.
- Two near-simultaneous pet/toast clicks must produce one lifecycle command; Task 3 adds concurrency tests.
- Collector drain exceptions must remain best-effort like the existing tray flow, while a rejected lifecycle request must resume eligible collectors; Task 3 tests both paths.
- A manual main-window break while a reminder is visible must remove the bubble and tagged toast; Task 4 tests Paused cleanup.
- A disconnected monitor/DPI change must not strand the pet off-screen; Task 5 tests working-area clamping and persistence.

---

### Task 1: Correct four-hour continuous-work evaluation

**Files:**
- Modify: `../HRMS-Backend-v1/src/ONEVO.Infrastructure/Services/Monitoring/Notifications/WellnessRuleEvaluator.cs`
- Modify: `../HRMS-Backend-v1/src/ONEVO.Infrastructure/Services/Monitoring/Notifications/WellnessRuleEvaluatorJob.cs`
- Modify: `../HRMS-Backend-v1/tests/ONEVO.Tests.Unit/Features/Monitoring/Notifications/WellnessRuleEvaluatorTests.cs`
- Modify: `../HRMS-Backend-v1/tests/ONEVO.Tests.Unit/Services/Monitoring/Notifications/WellnessRuleEvaluatorJobTests.cs`

**Interfaces:**
- Consumes: ordered `IReadOnlyList<DeviceStateSnapshot>` with `CapturedAt` and `IsIdle`.
- Produces: `WellnessRuleEvaluator.BreakReminderThresholdMinutes = 240`, `MaximumContinuousSampleGap = 90 seconds`, and unchanged `WellnessEvaluationResult`.

- [ ] **Step 1: Write failing evaluator tests**

Add tests named `Evaluate_239ContinuousActiveMinutes_DoesNotTrigger`, `Evaluate_240ContinuousActiveMinutes_Triggers`, `Evaluate_Exactly90SecondGap_RemainsContinuous`, `Evaluate_Over90SecondGap_ResetsStreak`, `Evaluate_IdleSample_ResetsFourHourStreak`, and `Evaluate_DeviceChange_ResetsStreak`. Assert both trigger flags and exact `StreakMinutes`.

- [ ] **Step 2: Run the focused evaluator tests and verify failure**

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~WellnessRuleEvaluatorTests`

Expected: tests expecting 240 minutes and timestamp-gap reset fail against the 120-minute/count-only implementation.

- [ ] **Step 3: Implement timestamp-aware continuity**

In `WellnessRuleEvaluator`, set `BreakReminderThresholdMinutes` to `240`, add `public static readonly TimeSpan MaximumContinuousSampleGap = TimeSpan.FromSeconds(90)`, and stop reverse scanning when adjacent `CapturedAt` values differ by more than that value, the idle state changes, or `EmployeeId`/`AgentDeviceId` changes.

- [ ] **Step 4: Write and run the job lookback test**

Update `WellnessRuleEvaluatorJobTests` to seed a 240-minute active streak and assert a `BreakReminder` is created. Expose the lookback as `internal static readonly TimeSpan LookbackWindow = TimeSpan.FromHours(5)` and assert the repository query receives `now - 5 hours`.

Run: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj --filter "FullyQualifiedName~WellnessRuleEvaluator"`

Expected: PASS.

- [ ] **Step 5: Commit the isolated backend change**

Commit only the four Task 1 paths in `HRMS-Backend-v1` with message `feat: evaluate four-hour continuous work streaks`. Do not stage pre-existing backend changes.

### Task 2: Make wellness delivery acceptance-aware

**Files:**
- Modify: `ONEVO.Agent.Shared/IPC/IpcMessages.cs`
- Modify: `ONEVO.Agent.Service/Sync/NotificationPollingService.cs`
- Modify: `ONEVO.Agent.Service/AgentWorker.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/INamedPipeClient.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/NamedPipeClient.cs`
- Modify: `tests/ONEVO.Agent.Service.Tests/Sync/NotificationPollingServiceTests.cs`
- Modify: `tests/ONEVO.Agent.Service.Tests/AgentWorkerGenericNotificationAcceptedTests.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs`
- Create: `tests/ONEVO.Agent.Service.Tests/AgentWorkerWellnessNotificationAcceptedTests.cs`

**Interfaces:**
- Consumes: existing `NotificationPushPayload.NotificationId` and backend `AckNotificationAsync(jwt, id, ct)`.
- Produces: `IpcMessageTypes.WellnessNotificationAccepted`, `WellnessNotificationAcceptedPayload(Guid NotificationId)`, and `INamedPipeClient.AcceptWellnessNotificationAsync(Guid, CancellationToken)`.

- [ ] **Step 1: Write failing service polling tests**

Change the notification polling test to assert `PollOnceAsync` broadcasts `NotificationPush` but does not call `AckNotificationAsync` before tray acceptance, including the case where no tray is connected.

- [ ] **Step 2: Run the polling test and verify it fails**

Run: `dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter FullyQualifiedName~NotificationPollingServiceTests`

Expected: FAIL because the current poller acknowledges immediately.

- [ ] **Step 3: Add the IPC acceptance contract and client method**

Add the message constant/payload and implement `AcceptWellnessNotificationAsync` by sending a one-way envelope. Update the fake client to record accepted notification IDs.

- [ ] **Step 4: Remove eager acknowledgement and handle acceptance in the service**

Delete the poller's immediate ack call. Add an `AgentWorker` dispatch branch that validates the enrolled device context and calls the existing backend acknowledgement only for the accepted notification ID.

- [ ] **Step 5: Test accepted, duplicate, and crash-before-acceptance behavior**

Add worker tests asserting one backend ack for a valid acceptance, harmless handling of a duplicate acceptance, and no ack when only a broadcast occurred.

Run: `dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj --filter "FullyQualifiedName~NotificationPollingServiceTests|FullyQualifiedName~WellnessNotificationAccepted"`

Expected: PASS.

- [ ] **Step 6: Commit the tray/service protocol change**

Commit only Task 2 paths in `HRMS_TrayApp` with message `feat: acknowledge wellness notifications after tray acceptance`.

### Task 3: Add one safe, idempotent direct-break action

**Files:**
- Create: `ONEVO.Agent.TrayApp/Services/BreakActionService.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Services/BreakActionServiceTests.cs`
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs`

**Interfaces:**
- Consumes: `INamedPipeClient.LastKnownStatus`, `ICollectorLifecycleCoordinator`, and `SendLifecycleAsync(LifecycleAction.StartBreak, ct)`.
- Produces: `Task<BreakActionResult> StartBreakAsync(CancellationToken ct)` where result exposes `Success`, `ErrorCode`, and `Message`; concurrent callers share/reject one in-flight action and never send two lifecycle commands.

- [ ] **Step 1: Write failing eligibility and concurrency tests**

Test active/allowed success, disconnected rejection, no active session rejection, `CanStartBreak = false` rejection, two concurrent calls sending one command, and authoritative lifecycle failure preserving failure details.

- [ ] **Step 2: Write the collector-safety tests**

Assert collector pre-stop/drain is attempted before `SendLifecycleAsync`, a local drain exception still permits the lifecycle command, cancellation from the caller stops the action, and a rejected result whose authoritative state is Active calls `ResumeAfterRejectedPauseAsync`.

- [ ] **Step 3: Run focused tests and verify failure**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter FullyQualifiedName~BreakActionServiceTests`

Expected: FAIL because the service/result do not exist.

- [ ] **Step 4: Implement and register `BreakActionService`**

Call `ICollectorLifecycleCoordinator.PrepareForPauseAsync(ct)` before lifecycle send, continue after its non-cancellation exception, and call `ResumeAfterRejectedPauseAsync(ct)` when the returned result is unsuccessful with `State == MonitoringState.Active`. Use an async gate so a pet and toast click cannot race.

- [ ] **Step 5: Run focused and existing active-session tests**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~BreakActionServiceTests|FullyQualifiedName~ActiveSessionViewModelTests"`

Expected: PASS.

- [ ] **Step 6: Commit the isolated break service**

Commit Task 3 files with message `feat: add safe direct break action`; if the required extraction touches a pre-dirty file, keep unrelated hunks unstaged.

### Task 4: Present and route actionable wellbeing reminders

**Files:**
- Create: `ONEVO.Agent.TrayApp/Services/WellbeingActivation.cs`
- Create: `ONEVO.Agent.TrayApp/Services/WellbeingReminderCoordinator.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/NotificationService.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/WindowsInactivityPromptService.cs`
- Modify: `ONEVO.Agent.TrayApp/Collectors/CollectorCoordinator.cs`
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Services/WellbeingActivationTests.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Services/WellbeingReminderCoordinatorTests.cs`

**Interfaces:**
- Consumes: `NotificationPushPayload`, `BreakActionService`, `AcceptWellnessNotificationAsync`, and pipe status/state events.
- Produces: stable activation arguments `kind=wellbeing;action=start_break;notification=<guid>`; `Task HandleAsync(NotificationPushPayload, CancellationToken)`; `Task ConfirmPetPresentationAsync(Guid notificationId, CancellationToken)`; `Task HandleActivationAsync(string arguments, CancellationToken)`; `BreakReminderRaised`/`BreakReminderCleared` coordinator events for the pet; and tagged toast removal on Paused.

- [ ] **Step 1: Write failing activation parser tests**

Cover valid semicolon-delimited arguments, malformed GUID, unknown kind/action, empty arguments, and unrelated generic/long-session arguments.

- [ ] **Step 2: Write failing coordinator tests**

Assert a BreakReminder is claimed once, a successful Windows toast triggers tray acceptance, `ConfirmPetPresentationAsync` triggers acceptance when the toast failed, both surfaces failing leave it unaccepted, repeated surface confirmations send one acceptance, simultaneous activation calls produce one break action, Paused clears the reminder/removes the tagged toast, and a manual break also clears it.

- [ ] **Step 3: Run focused tests and verify failure**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~WellbeingActivationTests|FullyQualifiedName~WellbeingReminderCoordinatorTests"`

Expected: FAIL because the coordinator/parser do not exist.

- [ ] **Step 4: Implement actionable toast and single-callback routing**

Extend `NotificationService` with `bool ShowBreakReminder(NotificationPushPayload, string arguments)` and `Task RemoveBreakReminderAsync(Guid)`. Add a **Take a Break** button and tag the toast with the notification ID. Route wellbeing arguments before the generic/inactivity fallback in the existing single `NotificationInvoked` handler.

- [ ] **Step 5: Move wellness receipt ownership from `CollectorCoordinator`**

Have the new coordinator own `BreakReminder`; keep LongIdleAlert and BreakAllowanceExceeded on the existing info/warning path. Register and eagerly resolve the coordinator before the pipe starts.

- [ ] **Step 6: Run notification regression tests**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~Notification|FullyQualifiedName~Inactivity|FullyQualifiedName~LongSession|FullyQualifiedName~Wellbeing"`

Expected: PASS.

- [ ] **Step 7: Commit the reminder pipeline**

Commit Task 4 paths with message `feat: add actionable wellbeing break reminders`, excluding unrelated pre-existing hunks in the dirty notification files.

### Task 5: Implement testable pet state and position logic

**Files:**
- Create: `ONEVO.Agent.TrayApp/Pet/PetState.cs`
- Create: `ONEVO.Agent.TrayApp/Pet/PetPresentation.cs`
- Create: `ONEVO.Agent.TrayApp/Pet/PetPositionStore.cs`
- Create: `ONEVO.Agent.TrayApp/Pet/PetPositioning.cs`
- Create: `ONEVO.Agent.TrayApp/Pet/PetController.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Pet/PetControllerTests.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Pet/PetPositioningTests.cs`

**Interfaces:**
- Consumes: pipe state/status/disconnect events and coordinator reminder events.
- Produces: `PetState { Hidden, Ready, Working, BreakDue, OnBreak, Disconnected }`, immutable `PetPresentation`, `Show/Hide/Toggle`, `OpenMainWindow`, `StartBreakAsync`, and pure `Clamp(Rectangle desired, IReadOnlyList<Rectangle> workingAreas)`.

- [ ] **Step 1: Write failing state precedence tests**

Cover unenrolled/locked Hidden, enrolled Stopped Ready, Active Working, Active plus reminder BreakDue, Paused OnBreak, disconnected Disconnected, and Paused clearing an outstanding reminder.

- [ ] **Step 2: Write failing position tests**

Cover default bottom-right placement, saved valid position, negative-coordinate monitor, removed-monitor clamp, oversized/corrupt coordinates, and empty screen list fallback.

- [ ] **Step 3: Run focused tests and verify failure**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter FullyQualifiedName~ONEVO.Agent.TrayApp.Tests.Pet`

Expected: FAIL because pet domain types do not exist.

- [ ] **Step 4: Implement pure state/position logic and preference persistence**

Keep Windows Forms types out of `PetController`; convert screen bounds to `System.Drawing.Rectangle` at the overlay boundary. Persist only `PetVisible`, `PetX`, and `PetY` through the existing preference mechanism.

- [ ] **Step 5: Run focused tests**

Run the Task 5 filter again.

Expected: PASS.

- [ ] **Step 6: Commit the pet domain layer**

Commit Task 5 files with message `feat: add desktop pet state controller`.

### Task 6: Build the Windows pet overlay and mascot assets

**Files:**
- Create: `ONEVO.Agent.TrayApp/Pet/PetOverlayForm.cs`
- Create: `ONEVO.Agent.TrayApp/Pet/PetAssetCatalog.cs`
- Create: `ONEVO.Agent.TrayApp/Resources/Pet/pet_idle_00.png`
- Create: `ONEVO.Agent.TrayApp/Resources/Pet/pet_working_00.png`
- Create: `ONEVO.Agent.TrayApp/Resources/Pet/pet_break_due_00.png`
- Create: `ONEVO.Agent.TrayApp/Resources/Pet/pet_on_break_00.png`
- Create: `ONEVO.Agent.TrayApp/Resources/Pet/pet_offline_00.png`
- Modify: `ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Pet/PetAssetCatalogTests.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Pet/PetOverlayContractTests.cs`

**Interfaces:**
- Consumes: `PetController` presentation/events and fixed resource naming contract.
- Produces: a borderless, transparency-keyed, topmost, no-taskbar 160x160 pet with an accessible break bubble, drag behavior, and right-click menu; no second WinForms message loop.

- [ ] **Step 1: Create or generate the five original blue mascot PNGs**

Use one consistent original character, transparent background, 160x160 canvas, and the exact filenames above. Do not copy the screenshot mascot. Validate alpha and dimensions in `PetAssetCatalogTests`. When the overlay actually renders the bubble, call `WellbeingReminderCoordinator.ConfirmPetPresentationAsync(notificationId, ct)` so a failed toast can still cross the acceptance boundary.

- [ ] **Step 2: Write failing overlay contract tests**

Assert constructor/configuration sets borderless, no taskbar, topmost, transparency key, accessible action name, and frame timer disabled while Hidden.

- [ ] **Step 3: Run focused tests and verify failure**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~PetAssetCatalogTests|FullyQualifiedName~PetOverlayContractTests"`

Expected: FAIL before the assets/form exist.

- [ ] **Step 4: Implement overlay rendering and interaction**

Use `PictureBox`, owner-drawn bubble controls, mouse drag capture, and the existing UI message pump. State updates must marshal through the form's synchronization context and must not call `Activate()` unless the employee explicitly opens ONEVO.

- [ ] **Step 5: Package resources and run focused tests**

Embed `Resources/Pet/*.png` with stable logical names in the tray project, dispose replaced images, and rerun the Task 6 filter.

Expected: PASS with no missing-resource failures.

- [ ] **Step 6: Commit overlay and assets**

Commit Task 6 files with message `feat: add floating wellbeing pet overlay`.

### Task 7: Wire app lifetime, tray menu, and foreground navigation

**Files:**
- Modify: `ONEVO.Agent.TrayApp/App.xaml.cs`
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/TrayIconService.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/Services/TrayScreenLayoutContractTests.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Pet/PetLifetimeTests.cs`

**Interfaces:**
- Consumes: `PetController`, `PetOverlayForm`, and `TrayIconService.ShowMainWindow()`.
- Produces: app-lifetime create/show/dispose, **Show Pet/Hide Pet** tray menu item, and pet click foreground plus `//active` navigation for Active/Paused states.

- [ ] **Step 1: Write failing lifetime/menu tests**

Assert one overlay instance per app lifetime, no overlay before enrollment, show/hide menu label follows preference, pet click calls foreground helper, Active/Paused click requests `//active`, and app shutdown disposes overlay/timer/images once.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "FullyQualifiedName~PetLifetimeTests|FullyQualifiedName~TrayScreenLayoutContractTests"`

Expected: FAIL before wiring exists.

- [ ] **Step 3: Register and start the pet with the existing app lifetime**

Create the overlay after the main window and tray icon exist, subscribe before the first status, and dispose it in the existing shutdown path. Keep startup failure non-fatal and logged without message content.

- [ ] **Step 4: Add tray/pet open and visibility commands**

Reuse `ShowMainWindow()` and add main-thread `//active` navigation only when authoritative state is Active/Paused. Use the same controller for the pet and tray context-menu visibility actions.

- [ ] **Step 5: Run focused tests**

Run the Task 7 filter again.

Expected: PASS.

- [ ] **Step 6: Commit integration wiring**

Commit Task 7 paths with message `feat: integrate pet with WorkPulse tray lifetime`, staging only task hunks from pre-dirty files.

### Task 8: End-to-end regression and Windows verification

**Files:**
- Modify: `docs/uat/11-manual-testing-checklist.md`
- Modify: `docs/uat/06-api-integration-report.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: completed backend and tray feature.
- Produces: reproducible automated evidence plus a Windows manual verification record for the pet/reminder/break flow.

- [ ] **Step 1: Run the complete backend unit suite**

Run from `HRMS-Backend-v1`: `dotnet test tests/ONEVO.Tests.Unit/ONEVO.Tests.Unit.csproj`

Expected: PASS with zero failed tests.

- [ ] **Step 2: Run all tray/shared/service unit suites**

Run from `HRMS_TrayApp`:

`dotnet test tests/ONEVO.Agent.Shared.Tests/ONEVO.Agent.Shared.Tests.csproj`

`dotnet test tests/ONEVO.Agent.Service.Tests/ONEVO.Agent.Service.Tests.csproj`

`dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj`

Expected: all PASS with zero failed tests.

- [ ] **Step 3: Build Debug and MSIX packaging inputs**

Run: `dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj -c Debug`

Run the repository's existing MSIX build script in validation mode or produce a Release package on the Windows host. Confirm all five pet resources are present.

Expected: successful build/package with no duplicate WinForms message loop and no missing resource.

- [ ] **Step 4: Perform manual Windows acceptance**

Verify 239/240-minute seeded reminder behavior, pet visibility/drag/restart/multi-monitor, no focus stealing, pet and tray foregrounding, toast and bubble double-click race, manual-break stale-toast removal, offline failure, break allowance rejection, sleep/resume reset, portal On Break synchronization, and clean app exit.

- [ ] **Step 5: Document evidence and residual limitations**

Update the three documentation files with commands/results, screenshots or log identifiers, asset-swap instructions, and any environment-only limitation. Never include notification message text or personal data in logs/evidence.

- [ ] **Step 6: Commit verification documentation**

Commit documentation-only changes with message `docs: verify wellbeing desktop pet flow`.
