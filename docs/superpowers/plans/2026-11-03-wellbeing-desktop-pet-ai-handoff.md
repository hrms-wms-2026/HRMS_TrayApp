# AI Implementation Handoff — ONEVO Wellbeing Desktop Pet

## AI-க்கு கொடுக்க வேண்டிய முக்கிய instruction

நீ `C:\HR2` workspace-ல் இருக்கும் ONEVO HRMS system-க்கு Windows Desktop Pet feature implement செய்யும் senior .NET engineer.

இந்த document-ஐ முழுமையாகப் படித்த பிறகு மட்டுமே code மாற்றங்களைத் தொடங்கு. Existing architecture-ஐ reuse செய். தேவையில்லாமல் புதிய framework அல்லது தனி executable உருவாக்காதே. ஒவ்வொரு task-ஐயும் test-first முறையில் முடித்து, verification pass ஆன பிறகே அடுத்த task-க்கு செல்.

## இறுதி நோக்கம்

Existing `HRMS_TrayApp` application-க்குள் ஒரு lightweight Windows Desktop Pet உருவாக்க வேண்டும்.

Employee தொடர்ந்து 240 நிமிடங்கள் active-ஆக வேலை செய்தால்:

1. Backend `BreakReminder` உருவாக்க வேண்டும்.
2. Tray App notification-ஐப் பெற வேண்டும்.
3. Desktop Pet `BreakDue` நிலைக்கு மாற வேண்டும்.
4. Pet-க்கு மேலே actionable reminder bubble தோன்ற வேண்டும்.
5. Windows actionable notification-மும் தோன்ற வேண்டும்.
6. இரண்டிலும் **Take a Break** action இருக்க வேண்டும்.
7. Action click செய்தால் existing safe attendance break flow பயன்படுத்த வேண்டும்.
8. Backend success வந்த பிறகு Website, Tray App, Pet மூன்றும் `On Break` காட்ட வேண்டும்.
9. Pet click செய்தால் existing WorkPulse main window foreground-ல் திறக்க வேண்டும்.

## Architecture boundary (onevo-agent-architecture)

இந்த feature **Phase 1** scope-க்குள் உள்ளது: புதிய data collection இல்லை, புதிய backend route இல்லை, புதிய process இல்லை. Pet ஒரு UI surface மட்டுமே.

Rulebook: `C:\HR\ONEVO_Agent_Architecture_Flow_Folder_Structure.md` (§4 trust boundaries, §8 privacy, §15 logging, §16 performance, §20 checklist, §21 accept/reject). Repo `C:\HR2\HRMS_TrayApp` — skill-ல் உள்ள `C:\HR\tray_app_maui` path பழையது; folder/protocol-க்கு shipped code தான் source of truth.

| Concern | Owner | இந்த feature-ல் |
|---|---|---|
| Continuous-work evaluation, notification create | Backend (`WellnessRuleEvaluatorJob`) | 240-minute time-based streak |
| Backend HTTP (pending / ack) | `ONEVO.Agent.Service/Api/` + `Sync/NotificationPollingService` | Existing `/api/v1/monitoring/tray/notifications/{id}/ack` route reuse. புதிய route இல்லை, `/api/v1/agent/*` இல்லை |
| Monitoring state / break | `AgentStateMachine` (Service மட்டும்) | Tray `LifecycleCommand(StartBreak)` அனுப்பும்; state-ஐ Tray set செய்யாது |
| IPC contract | `ONEVO.Agent.Shared/IPC/IpcMessages.cs` | `WellnessNotificationAccepted` constant + payload மட்டும் |
| Toast, Pet overlay, activation parsing | `ONEVO.Agent.TrayApp/Services/` | Pet → `Services/Pet/` (`Services/Diagnostics/` போல sub-folder) |

Hard rules:

- TrayApp backend-ஐ நேரடியாக call செய்யக்கூடாது. Ack எப்போதும் Service வழியாக (Device JWT Service-ல் மட்டும், DPAPI).
- Pet state = Service அனுப்பும் `StatusResponse` / `LifecycleResultPayload` மட்டுமே. Local guess இல்லை.
- Pet எந்த activity data, app name, screenshot, location, employee name/email-ஐயும் காட்டவோ log செய்யவோ கூடாது (§8, §15).
- Activation arguments-ல் ids மட்டும் (`kind`, `action`, `notification`) — PII இல்லை. Payload-ல் இருந்து URL build செய்யக்கூடாது.
- IPC message `Constants.MaxMessageLengthBytes` (64 KiB) bound-க்குள், existing authenticated pipe வழியாக. `IpcProtocolVersion.Current` (`"1.0"`) மாற்றக்கூடாது — additive message type மட்டும்; Service மற்றும் Tray ஒரே installer-ல் ship ஆகின்றன.
- `TreatWarningsAsErrors` on — எந்த warning-ம் build fail ஆக்கும்.

## முதலில் படிக்க வேண்டிய existing files

### Tray application

- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\MauiProgram.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\App.xaml.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\Services\TrayIconService.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\Services\NotificationService.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\Services\WindowsInactivityPromptService.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\Services\INamedPipeClient.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\Services\NamedPipeClient.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\Collectors\ICollectorLifecycleCoordinator.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\Collectors\CollectorCoordinator.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\ViewModels\ActiveSessionViewModel.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.Shared\IPC\IpcMessages.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.Service\Sync\NotificationPollingService.cs`
- `C:\HR2\HRMS_TrayApp\ONEVO.Agent.Service\AgentWorker.cs` (குறிப்பாக `HandleGenericNotificationAcceptedAsync`)

### Copy செய்ய வேண்டிய existing pattern (generic notification delivery)

Wellness acceptance protocol புதிதாக design செய்யாதே — இந்த shipped pattern-ஐ அப்படியே mirror செய்:

- `ONEVO.Agent.TrayApp\Services\GenericNotificationPresenter.cs` — claim → show → `SendEnvelopeAsync(…Accepted)`; show fail ஆனால் claim remove.
- `ONEVO.Agent.TrayApp\Services\ShownNotificationLog.cs` — bounded, on-disk shown-id log (tray restart-க்கு பிறகும் duplicate toast இல்லை).
- `ONEVO.Agent.TrayApp\Services\GenericNotificationActivation.cs`, `LongSessionActivation.cs` — `kind=…;…` activation parse pattern.
- `ONEVO.Agent.TrayApp\Collectors\CollectorCoordinator.cs` — `OnWellnessNotificationReceived` இப்போது BreakReminder-க்கும் `ShowInfo` toast காட்டுகிறது (Task 5-ல் மாற்ற வேண்டும்).
- `ONEVO.Agent.TrayApp\ViewModels\ActiveSessionViewModel.cs` — `ConfirmStartBreakAsync` / `RunLifecycleWithPreStopAsync` (BreakActionService இதே sequence-ஐப் பின்பற்ற வேண்டும்).
- `tests\ONEVO.Agent.Service.Tests\AgentWorkerGenericNotificationAcceptedTests.cs`
- `tests\ONEVO.Agent.Service.Tests\Sync\NotificationPollingServiceTests.cs`
- `tests\ONEVO.Agent.Service.Tests\AgentWorkerBreakRaceTests.cs`
- `tests\ONEVO.Agent.TrayApp.Tests\Notifications\GenericNotificationPresenterTests.cs`
- `tests\ONEVO.Agent.TrayApp.Tests\Fakes\FakeNamedPipeClient.cs`, `FakePreferencesStore.cs`

### Backend

- `C:\HR2\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Monitoring\Notifications\WellnessRuleEvaluator.cs`
- `C:\HR2\HRMS-Backend-v1\src\ONEVO.Infrastructure\Services\Monitoring\Notifications\WellnessRuleEvaluatorJob.cs`
- `C:\HR2\HRMS-Backend-v1\src\ONEVO.Domain\Features\Monitoring\DeviceState\Entities\DeviceStateSnapshot.cs`
- `C:\HR2\HRMS-Backend-v1\src\ONEVO.Application\Features\TimeAttendance\Commands\StartBreak\StartBreakCommandHandler.cs` (read only — மாற்றம் தேவையில்லை)
- `C:\HR2\HRMS-Backend-v1\src\ONEVO.Api\Controllers\Tenant\Monitoring\Notifications\TrayNotificationsController.cs` (read only — existing `[Authorize(Policy = "TrayDevicePolicy")]` ack route reuse)
- `C:\HR2\HRMS-Backend-v1\tests\ONEVO.Tests.Unit\Features\Monitoring\Notifications\WellnessRuleEvaluatorTests.cs`
- `C:\HR2\HRMS-Backend-v1\tests\ONEVO.Tests.Unit\Services\Monitoring\Notifications\WellnessRuleEvaluatorJobTests.cs`

## மாற்றங்களுக்கு முன் பாதுகாப்பு விதிகள்

1. இரண்டு repositories-லும் முதலில் `git status --short` run செய்.
2. Existing uncommitted changes user-க்கு சொந்தமானவை; அவற்றை reset, checkout அல்லது overwrite செய்யாதே.
3. குறிப்பாக கீழ்கண்ட Tray files ஏற்கனவே மாற்றப்பட்டிருக்கலாம்:
   - `MauiProgram.cs`
   - `NotificationService.cs`
   - `WindowsInactivityPromptService.cs`
   - `ActiveSessionViewModel.cs`
4. கீழ்கண்ட Backend files-லும் existing changes இருக்கலாம்:
   - `StartBreakCommandHandler.cs`
   - `EndBreakCommandHandler.cs`
   - attendance status files
5. தேவையான lines மட்டும் surgical patch செய்.
6. User changes-ஐ commit-க்குள் சேர்க்காதே.
7. `git reset --hard`, `git checkout --`, recursive delete போன்ற destructive commands பயன்படுத்தாதே.
8. 2026-11-03 நிலவரம்:
   - Tray repo branch `feature/daily-summary-tasks`. `MauiProgram.cs`, `NotificationService.cs`, `WindowsInactivityPromptService.cs`, `ActiveSessionViewModel.cs` modified; `LongSessionActivation.cs`, `LongSessionReminder.cs` untracked. இந்த plan-ன் routing priority (long session first) அந்த uncommitted வேலையைச் சார்ந்துள்ளது.
   - Backend repo branch `fix/dapi-seeder-skip-today` — இந்த feature-க்கு தொடர்பில்லாத branch.
9. அதனால்: user முதலில் தன் long-session வேலையை commit செய்து, இரண்டு repos-லும் இந்த feature-க்கு தனி branch உருவாக்கும் வரை **AI commit செய்யக்கூடாது**. Interactive `git add -p` இந்த environment-ல் இல்லை, so ஒரே file-ல் user + feature changes கலந்தால் file-wise commit பிரிக்க முடியாது.
10. Service tests real `%ProgramData%\ONEVO\Agent` credentials-ஐ அழிக்கக்கூடும் — branch-ல் PR #71 (`4302bbe`, AUD-017 test isolation) இருக்கிறதா என்று `git merge-base --is-ancestor 4302bbe HEAD` மூலம் உறுதி செய்த பிறகே Service tests run செய்.

## Technical constraints

- Target framework: `net10.0-windows10.0.19041.0`.
- Pet Windows-only feature.
- Existing MAUI app process-க்குள் Pet இயங்க வேண்டும்.
- தனி Pet executable உருவாக்காதே.
- இரண்டாவது `Application.Run()` அல்லது இரண்டாவது WinForms message loop உருவாக்காதே.
- Electron, WPF, Avalonia, SkiaSharp, Lottie, React அல்லது WebView Pet சேர்க்காதே.
- Existing Windows Forms framework reference-ஐ reuse செய்.
- Existing Windows App SDK notification registration-ஐ reuse செய்.
- Existing Named Pipe மற்றும் `LifecycleAction.StartBreak` flow-ஐ reuse செய்.
- Website button-ஐ browser automation மூலம் click செய்யாதே.
- UI state-ஐ optimistic-ஆக `OnBreak` என்று மாற்றாதே.
- Backend/lifecycle authoritative response வந்த பிறகே `OnBreak` காட்ட வேண்டும்.
- Existing `FrameworkReference Microsoft.WindowsDesktop.App.WindowsForms` (csproj) மற்றும் `TrayIconService`-ன் `NotifyIcon` thread-ஐ reuse செய். புதிய package reference சேர்க்காதே (`Directory.Packages.props` மாற்றம் இல்லை).
- Pipe events (`OnStatusReceived`, `OnNotificationReceived`) UI thread-ல் வருவதில்லை. Form-ஐ `NotifyIcon` உருவாக்கப்பட்ட அதே UI thread-ல் create செய்; controller → form ஒவ்வொரு update-ம் `BeginInvoke` மூலம் marshal செய்.
- Tray footprint budget (§16): private bytes ≤ 230 MB. Pet images + timer அதைத் தாண்டக்கூடாது; CPU average < 2%.

## Pet asset contract

இந்த folder உருவாக்கு:

```text
C:\HR2\HRMS_TrayApp\ONEVO.Agent.TrayApp\Resources\Pet
```

இந்த exact filenames பயன்படுத்தப்பட வேண்டும்:

```text
pet_idle_00.png
pet_working_00.png
pet_break_due_00.png
pet_on_break_00.png
pet_offline_00.png
```

Asset requirements:

- 160x160 pixels.
- Transparent PNG.
- ஒரே original blue mascot character.
- Screenshot-ல் உள்ள character-ஐ நேரடியாக copy செய்யாதே.
- ஒவ்வொரு state-க்கும் ஆரம்பத்தில் ஒரு frame போதும்.
- Code multiple numbered frames-ஐ பின்னர் ஏற்றக்கூடியதாக இருக்க வேண்டும்.

Project file-ல் embedded resource pattern சேர்க்கவும்:

```xml
<ItemGroup>
  <EmbeddedResource
    Include="Resources\Pet\*.png"
    LogicalName="ONEVO.Agent.TrayApp.Pet.%(Filename)%(Extension)" />
</ItemGroup>
```

## உருவாக்க வேண்டிய புதிய source files

```text
ONEVO.Agent.TrayApp/Services/Pet/PetState.cs
ONEVO.Agent.TrayApp/Services/Pet/PetPresentation.cs
ONEVO.Agent.TrayApp/Services/Pet/PetAssetCatalog.cs
ONEVO.Agent.TrayApp/Services/Pet/PetPositionStore.cs
ONEVO.Agent.TrayApp/Services/Pet/PetPositioning.cs
ONEVO.Agent.TrayApp/Services/Pet/PetController.cs
ONEVO.Agent.TrayApp/Services/Pet/PetOverlayForm.cs

ONEVO.Agent.TrayApp/Services/BreakActionService.cs
ONEVO.Agent.TrayApp/Services/WellbeingActivation.cs
ONEVO.Agent.TrayApp/Services/WellbeingReminderCoordinator.cs

ONEVO.Agent.Shared/IPC/IpcMessages.cs            (modify: constant + payload)
ONEVO.Agent.Service/Sync/NotificationPollingService.cs (modify)
ONEVO.Agent.Service/AgentWorker.cs               (modify: one switch case + handler)
ONEVO.Agent.TrayApp/Collectors/CollectorCoordinator.cs (modify: BreakReminder toast நீக்கம்)
```

Folder காரணம்: architecture-ல் tray icon, notification, pipe client போன்ற interactive-session services `TrayApp/Services/`-ல் உள்ளன (`TrayIconService` கூட WinForms `NotifyIcon` தான்). புதிய top-level `Pet/` folder உருவாக்காதே. Namespace: `ONEVO.Agent.TrayApp.Services.Pet`. `Platforms/Android|iOS|MacCatalyst|Tizen` சேர்க்காதே. `Service/` project-ல் எந்த Pet/UI code-ம் வரக்கூடாது (Session 0).

## Task 1 — Backend continuous-work evaluator

### Current code (மாற்றத்துக்கு முன்)

- `BreakReminderThresholdMinutes = 120`, `LongIdleThresholdMinutes = 30`.
- Job `LookbackWindow = 3h`, `BreakReminderCooldown = 2h`.
- Evaluator **sample count**-ஐ minutes ஆக எண்ணுகிறது; gap, device, `now` பார்ப்பதில்லை.
- Tray `DeviceStateCollector` fixed 60-second `SampleWindow`-ல் snapshot அனுப்புகிறது (policy மாற்ற முடியாது) — அதனால் 90-second gap tolerance பாதுகாப்பானது. Break/clock-out/lock-ல் collectors நிற்பதால் gap தானாக streak-ஐ reset செய்யும்.

### தேவையான behavior

- `BreakReminderThresholdMinutes = 240`.
- `MaximumContinuousSampleGap = 90 seconds`.
- Backend lookback = 5 hours (`LookbackWindow`).
- Streak minutes = **elapsed time** (`newest.CapturedAt - oldestInStreak.CapturedAt` + one sample window), sample count அல்ல.
- Newest snapshot-லிருந்து reverse scan செய்யவும்.
- Newest snapshot `now - 90s`-ஐ விடப் பழையதாக இருந்தால் trigger இல்லை (offline buffer late-arriving data பழைய streak-ஐ fire செய்யக்கூடாது).
- கீழ்கண்ட condition வந்தால் streak நிறுத்தவும்:
  - `IsIdle` மாறுதல்;
  - adjacent `CapturedAt` gap 90 seconds-க்கு மேல்;
  - `EmployeeId` மாறுதல்;
  - `AgentDeviceId` மாறுதல்.
- LongIdle (30 minutes) behavior மாறக்கூடாது.
- Cooldown: existing `BreakReminderCooldown = 2h` அப்படியே (design spec: "existing cooldown behavior remains"). அதாவது break எடுக்காமல் தொடர்ந்தால் 240-ல் ஒரு reminder, பிறகு குறைந்தது 2h கழித்து அடுத்தது. Desktop dedupe notification ID மூலம்.
- Notification `Title` / `Message` backend-ல் render ஆகிறது; message-ஐ `"You've been active for 4 hours. Take a short break."` style-க்கு மாற்றலாம் (`StreakMinutes`-ஐ hours/minutes ஆக format). Tenant RLS / admin-mode switching (job-ல் உள்ள `SetAdminMode` + `SwitchToTenantAsync`) மாற்றக்கூடாது.

### முதலில் tests எழுது

Existing `tests/ONEVO.Tests.Unit/Features/Monitoring/Notifications/WellnessRuleEvaluatorTests.cs`-ஐ **update** செய் (120-minute assumptions உள்ள tests இப்போது fail ஆகும் — அவற்றை 240-க்கு மாற்று, delete செய்யாதே):

- `Evaluate_239ContinuousActiveMinutes_DoesNotTrigger`
- `Evaluate_240ContinuousActiveMinutes_Triggers`
- `Evaluate_Exactly90SecondGap_RemainsContinuous`
- `Evaluate_Over90SecondGap_ResetsStreak`
- `Evaluate_IdleSample_ResetsFourHourStreak`
- `Evaluate_DeviceChange_ResetsStreak`
- `Evaluate_EmployeeChange_ResetsStreak`
- `Evaluate_NewestSampleStale_DoesNotTrigger`
- `Evaluate_30IdleMinutes_StillTriggersLongIdle` (regression)

`tests/ONEVO.Tests.Unit/Services/Monitoring/Notifications/WellnessRuleEvaluatorJobTests.cs`-லும் 5h lookback மற்றும் 2h cooldown assertions update/add செய்.

Focused test command:

```powershell
cd C:\HR2\HRMS-Backend-v1
dotnet test .\tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj --filter FullyQualifiedName~WellnessRuleEvaluator
```

Task complete condition: எல்லா focused tests pass ஆக வேண்டும்.

## Task 2 — Wellness notification acceptance protocol

Current `NotificationPollingService.PollOnceAsync` **மூன்று** wellness types-க்கும் (`BreakReminder`, `LongIdleAlert`, `BreakAllowanceExceeded`) broadcast செய்த உடனே `AckNotificationAsync` call செய்கிறது.

**Deferral `BreakReminder`-க்கு மட்டும்.** `LongIdleAlert` மற்றும் `BreakAllowanceExceeded` immediate ack-ஐத் தொடர வேண்டும் — இல்லையென்றால் அவற்றை accept செய்ய tray-ல் யாரும் இல்லாததால் Service ஒவ்வொரு 60s poll-லும் re-broadcast செய்யும். (Design spec எல்லா wellness types-க்கும் என்று சொல்கிறது; இந்த scope குறைப்பு வேண்டுமென்றே.) Type compare: `string.Equals(type, "BreakReminder", StringComparison.OrdinalIgnoreCase)` — backend `NotificationType.BreakReminder` enum name.

Shared IPC-ல் (`ONEVO.Agent.Shared/IPC/IpcMessages.cs`) `GenericNotificationAccepted` அருகில் சேர்க்கவும்:

```csharp
public const string WellnessNotificationAccepted =
    "WellnessNotificationAccepted";

public sealed record WellnessNotificationAcceptedPayload(
    Guid NotificationId);
```

`INamedPipeClient`-ல் **புதிய method சேர்க்காதே.** `GenericNotificationPresenter` செய்வது போல existing `SendEnvelopeAsync(new IpcEnvelope { Type = IpcMessageTypes.WellnessNotificationAccepted, Payload = … })` பயன்படுத்து. (Interface method சேர்த்தால் `FakeNamedPipeClient` உட்பட எல்லா fakes-ம் மாற வேண்டும்.)

Flow:

1. Service pending wellness notification fetch செய்யும்.
2. Service IPC `NotificationPush` broadcast செய்யும்.
3. `BreakReminder` என்றால் Service உடனே backend ack செய்யக்கூடாது; மற்ற types-க்கு இப்போது போலவே ack.
4. Tray Pet அல்லது Windows toast presentation accept செய்த பிறகு `WellnessNotificationAccepted` அனுப்பும்.
5. `AgentWorker` dispatch switch-ல் `case IpcMessageTypes.WellnessNotificationAccepted:` சேர்த்து, `HandleGenericNotificationAcceptedAsync`-ஐ mirror செய்யும் `HandleWellnessNotificationAcceptedAsync`: `JsonException` / null / `Guid.Empty` → silently ignore; Device JWT `CredentialStore`-ல் இருந்து; existing `OnevoApiClient.AckNotificationAsync` (route `AgentApiRoutes.AckTrayNotification`). JWT இல்லை என்றால் ack இல்லை (fail closed).
6. Tray crash ஆனால் notification backend-ல் pending-ஆக இருக்க வேண்டும்; அடுத்த poll-ல் மீண்டும் offer ஆகும், tray `ShownNotificationLog` மூலம் repeat-ஐ suppress செய்யும்.
7. Log-ல் notification id மற்றும் type மட்டும் — Title/Message log செய்யாதே.

Tests:

- `tests/ONEVO.Agent.Service.Tests/Sync/NotificationPollingServiceTests.cs` (existing immediate-ack assertions update):
  - BreakReminder broadcast மட்டும் → ack இல்லை.
  - LongIdleAlert / BreakAllowanceExceeded → முன்பு போல உடனே ack (regression).
- `tests/ONEVO.Agent.Service.Tests/AgentWorkerWellnessNotificationAcceptedTests.cs` (`AgentWorkerGenericNotificationAcceptedTests` pattern):
  - Tray acceptance வந்தால் ஒரு backend ack.
  - Duplicate acceptance harmless.
  - Malformed / empty GUID payload → ack இல்லை, exception இல்லை.
  - Device JWT இல்லை → ack இல்லை.
- Tray closed/crash-before-acceptance என்றால் pending notification இழக்கப்படாது.
- `tests/ONEVO.Agent.Shared.Tests`-ல் payload JSON round-trip.

## Task 3 — BreakActionService

Create:

```text
ONEVO.Agent.TrayApp/Services/BreakActionService.cs
```

Public interface:

```csharp
Task<BreakActionResult> StartBreakAsync(
    CancellationToken cancellationToken);
```

`BreakActionResult` குறைந்தது கீழ்கண்ட fields கொண்டிருக்க வேண்டும்:

```text
Success
ErrorCode
Message
```

Eligibility:

- Pipe/service connected.
- Last known state Active.
- Open session உள்ளது.
- Session `IsOnBreak == false`.
- Session `CanStartBreak == true`.
- வேறு request in-flight இல்லை.

Execution:

1. Async gate acquire செய்.
2. Eligibility validate செய்.
3. `ICollectorLifecycleCoordinator.PrepareForPauseAsync(ct)` call செய்.
4. Non-cancellation drain exception வந்தால் existing flow போல lifecycle request தொடரட்டும்.
5. `_pipe.SendLifecycleAsync(LifecycleAction.StartBreak, ct)` call செய்.
6. Result unsuccessful மற்றும் authoritative state Active என்றால் `ResumeAfterRejectedPauseAsync(ct)` call செய்.
7. Authoritative result-ஐ caller-க்கு return செய்.
8. UI state-ஐ service தானாக மாற்றக்கூடாது.
9. Sequence `ActiveSessionViewModel.ConfirmStartBreakAsync` → `RunLifecycleWithPreStopAsync`-க்கு சமமாக இருக்க வேண்டும். `ActiveSessionViewModel`-ல் user uncommitted changes இருப்பதால் அதை இந்த task-ல் refactor செய்யாதே.
10. Main window Break button-ம் Pet-ம் ஒரே நேரத்தில் click ஆனால் VM gate இந்த service gate-ஐப் பார்க்காது — Service `AgentWorker` race handling (`AgentWorkerBreakRaceTests`) நம்பகமானது. `ClockActionErrorCodes.AlreadyInState` result-ஐ error-ஆகக் காட்டாதே; அடுத்த authoritative status-ஐ render செய்.
11. First authoritative `StatusResponse` வருவதற்கு முன் eligibility = false (fail closed).

Tests:

- Active + allowed success.
- Disconnected rejection.
- No session rejection.
- Already on break rejection.
- Allowance exhausted rejection.
- Two concurrent calls → one lifecycle request.
- Drain attempted before lifecycle.
- Drain non-cancellation exception → lifecycle still sent.
- Cancellation → no unnecessary send.
- Backend rejection with Active state → collectors resume.
- `ALREADY_IN_STATE` → non-error result.
- No status received yet → rejection, no lifecycle send.

## Task 4 — WellbeingActivation

Create:

```text
ONEVO.Agent.TrayApp/Services/WellbeingActivation.cs
```

Stable activation format:

```text
kind=wellbeing;action=start_break;notification=<guid>
```

Parser requirements:

- Never throw.
- Semicolon-delimited pairs parse செய்யவும்.
- Empty/malformed GUID reject செய்யவும்.
- Unknown kind/action reject செய்யவும்.
- Generic notification மற்றும் long-session activation-ஐ தவறாக claim செய்யக்கூடாது.
- `GenericNotificationActivation` / `LongSessionActivation` போல static class: `KindValue = "wellbeing"`, `IsWellbeing(args)`, `TryParse(args, out notificationId)`, `BuildArguments(notificationId)`.

Security (activation arguments = untrusted input):

- Unpackaged app-ல் toast click tray-ஐ args உடன் launch செய்யலாம்; அதனால் args-ல் உள்ள GUID **இந்த process-ல் தற்போது outstanding-ஆக உள்ள reminder ID-உடன் match ஆனால் மட்டுமே** `StartBreak` அனுப்ப வேண்டும்.
- Match இல்லை / cold start / first status இன்னும் வரவில்லை → break அனுப்பாதே; main window மட்டும் திற (fail closed).
- Args-ல் இருந்து URL, file path, அல்லது navigation target எதுவும் build செய்யக்கூடாது.

Tests: `tests/ONEVO.Agent.TrayApp.Tests/Notifications/WellbeingActivationTests.cs`.

## Task 5 — WellbeingReminderCoordinator

Create:

```text
ONEVO.Agent.TrayApp/Services/WellbeingReminderCoordinator.cs
```

Public methods:

```csharp
Task HandleAsync(
    NotificationPushPayload payload,
    CancellationToken cancellationToken);

Task ConfirmPetPresentationAsync(
    Guid notificationId,
    CancellationToken cancellationToken);

Task HandleActivationAsync(
    string arguments,
    CancellationToken cancellationToken);
```

Public events:

```text
BreakReminderRaised
BreakReminderCleared
```

Coordinator responsibilities:

- `BreakReminder` மட்டும் actionable flow-க்கு எடுத்துக்கொள்ளவும்.
- Notification ID-ஐ atomically claim செய்யவும்.
- Duplicate presentation மற்றும் duplicate acceptance தடுக்கவும்.
- Pet event raise செய்யவும்.
- Windows actionable toast காட்டவும்.
- Toast success அல்லது Pet confirmation வந்தால் acceptance அனுப்பவும்.
- Pet/toast activation-ஐ `BreakActionService`-க்கு route செய்யவும்.
- Concurrent clicks-ஐ ஒரே action-ஆகக் கையாளவும்.
- State Paused வந்தால் reminder consume செய்யவும்.
- Manual main-window Break வந்தாலும் bubble மற்றும் tagged toast remove செய்யவும்.
- Consume செய்யும்போது acceptance இன்னும் அனுப்பப்படவில்லை என்றால் `WellnessNotificationAccepted` அனுப்பு — இல்லையென்றால் backend-ல் pending-ஆக இருந்து மீண்டும் offer ஆகும்.

Existing code-உடன் integration:

- Dedupe: புதிய in-memory set மட்டும் போதாது; existing `ShownNotificationLog` singleton-ஐ reuse செய் (wellness மற்றும் generic ids இரண்டும் GUID — collision இல்லை). Claim → show; pet மற்றும் toast இரண்டும் fail ஆனால் `Remove` செய்து pending-ஆக விடு (`GenericNotificationPresenter` pattern).
- Subscribe: `INamedPipeClient.OnNotificationReceived`; `Type == "BreakReminder"` மட்டும் handle.
- **`CollectorCoordinator.OnWellnessNotificationReceived`-ல் `BreakReminder`-ஐ skip செய்** (இப்போது else-branch-ல் `ShowInfo` toast காட்டுகிறது). இல்லையென்றால் ஒரே reminder-க்கு இரண்டு toasts வரும். `LongIdleAlert` / `BreakAllowanceExceeded` behavior மாறக்கூடாது.
- Staleness: `NotificationPushPayload`-ல் `CreatedAt` / `ExpiresAt` இல்லை. Tray பல மணி நேரம் மூடியிருந்து reopen ஆனால் பழைய reminder வரும். இந்த iteration-ல்: current status Active இல்லை (Paused/Stopped) என்றால் present செய்யாமல் accept மட்டும் செய். Payload-ல் `CreatedAt` சேர்ப்பது தனி follow-up (Shared + Service + backend DTO மாற்றம்).
- Handler pipe callback-ல் ஓடுகிறது: never throw; exceptions → `_logger.LogDebug` (id மட்டும்).

Tests: `tests/ONEVO.Agent.TrayApp.Tests/Notifications/WellbeingReminderCoordinatorTests.cs` மற்றும் `tests/ONEVO.Agent.TrayApp.Tests/Collectors/`-ல் "BreakReminder no longer toasts from CollectorCoordinator" regression.

## Task 6 — Actionable Windows notification

Existing `NotificationService`-ஐ extend செய்.

Add:

```csharp
bool ShowBreakReminder(
    NotificationPushPayload payload,
    string activationArguments);

Task RemoveBreakReminderAsync(Guid notificationId);
```

Testability-க்கு `IGenericNotificationToast` / `ILongSessionToast` போல `IBreakReminderToast` interface (இதே இரண்டு methods) உருவாக்கி `NotificationService` அதை implement செய்யட்டும்; coordinator interface-ஐ மட்டும் சார்ந்திருக்கட்டும்.

Toast content:

```text
Title:   payload.Title    (backend render செய்தது; fallback "Time for a break")
Message: payload.Message  (fallback "You've been active for 4 hours. Take a short break.")
Button:  Take a Break     (tray-ல் local text)
```

Content server-side render ஆகிறது (architecture: tray content build செய்யாது). Hard-coded "4 hours" fallback-க்கு மட்டும்.

Toast tag notification ID-ஐ பயன்படுத்த வேண்டும்.

Existing `WindowsInactivityPromptService`-ல் இருக்கும் ஒரே `NotificationInvoked` callback-க்கு wellbeing routing சேர்க்கவும். இன்னொரு global callback subscription உருவாக்காதே.

Routing priority:

1. Long session activation.
2. Wellbeing activation.
3. Generic notification activation.
4. Existing inactivity decision routing.

Existing behaviors மாறக்கூடாது.

## Task 7 — Pet domain logic

### PetState

```csharp
public enum PetState
{
    Hidden,
    Ready,
    Working,
    BreakDue,
    OnBreak,
    Disconnected
}
```

### State precedence

1. User preference Hidden → Hidden.
2. Unenrolled/Locked → overlay absent/Hidden.
3. Service disconnected → Disconnected.
4. Paused / `IsOnBreak` → OnBreak.
5. Active + outstanding reminder → BreakDue.
6. Active → Working.
7. Enrolled + Stopped → Ready.

### Positioning

Pure method உருவாக்கவும்:

```csharp
Rectangle Clamp(
    Rectangle desired,
    IReadOnlyList<Rectangle> workingAreas);
```

Handle:

- Primary monitor bottom-right default.
- Negative-coordinate monitors.
- Removed monitor.
- Corrupt/very large saved coordinates.
- No screen list fallback.

Save preferences (existing `IPreferencesStore` வழியாக, existing `onevo.` prefix convention):

```text
onevo.pet_visible
onevo.pet_x
onevo.pet_y
```

இவை device UI preferences — sensitive இல்லை, `SessionPreferenceKeys.All`-ல் சேர்க்காதே (sign-out-க்கு பிறகும் இருக்கலாம்). Employee data, JWT, notification content எதுவும் Preferences-ல் சேமிக்கக்கூடாது. Corrupt value → default (parse never throws).

### Locked / session

Windows session lock / user switch (`IInteractiveSessionState.LockChanged`) → Pet timer stop + hide; unlock-ல் authoritative status அடிப்படையில் மீண்டும் முடிவு.

## Task 8 — PetOverlayForm

Use Windows Forms.

Required form configuration:

```csharp
FormBorderStyle = FormBorderStyle.None;
ShowInTaskbar = false;
TopMost = true;
StartPosition = FormStartPosition.Manual;
BackColor = Color.Magenta;
TransparencyKey = Color.Magenta;
```

Requirements:

- No second message loop.
- No focus stealing during animation/state updates.
- `PictureBox` மூலம் Pet frame காட்டவும்.
- 250–300 ms timer animation பயன்படுத்தவும்.
- Hidden state-ல் timer stop செய்யவும்.
- Drag support சேர்க்கவும்.
- Drag முடிந்ததும் position save செய்யவும்.
- Pet click → `TrayIconService.ShowMainWindow()`.
- Active/Paused என்றால் `//active` navigation request செய்யவும்.
- Right-click menu:
  - Open ONEVO
  - Hide Pet
  - Exit
- BreakDue state-ல் bubble மற்றும் accessible **Take a Break** button காட்டவும்.
- Bubble successfully rendered ஆன பிறகு `ConfirmPetPresentationAsync()` call செய்யவும்.
- App shutdown-ல் timer/images/form dispose செய்யவும்.

No-focus / no-taskbar (`ShowInTaskbar = false` மட்டும் Alt+Tab-ல் இருந்து மறைக்காது):

```csharp
protected override bool ShowWithoutActivation => true;

protected override CreateParams CreateParams
{
    get
    {
        var cp = base.CreateParams;
        cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        return cp;
    }
}
```

Bubble-ல் உள்ள **Take a Break** button click / keyboard (Enter/Space) மட்டும் activate ஆகலாம்.

Evidence screenshots: TopMost Pet inactivity / periodic screenshots-ல் பதிவாகும். Monitoring evidence-ல் UI overlay வரக்கூடாது — `SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE)` (Windows 10 2004 / 19041+; target TFM அதுவே) `OnHandleCreated`-ல் call செய். P/Invoke existing `ONEVO.Agent.TrayApp/Interop/NativeMethods.cs`-ல் சேர். Fail ஆனால் log (debug) மட்டும் — Pet வேலை செய்யட்டும்.

Rendering:

- `TransparencyKey = Magenta` anti-aliased PNG edges-ல் magenta fringe காட்டும் — assets hard-edged alpha (0 அல்லது 255) ஆக export செய், அல்லது load-ல் alpha threshold apply செய்.
- ஒரு state-க்கு ஒரே frame என்றால் animation timer ஓடக்கூடாது (CPU budget).
- Images ஒருமுறை load செய்து cache; ஒவ்வொரு tick-லும் புதிய `Bitmap` உருவாக்காதே.

Navigation: `//active` route ஏற்கனவே உள்ளது (`SetupFlow.Active`, `App.xaml.cs` Active/Paused → `//active`). `SetupFlow.Active` constant-ஐப் பயன்படுத்து; navigation `MainThread`-ல், exceptions catch (`LongSessionReminder` pattern).

## Task 9 — App lifetime மற்றும் DI wiring

`MauiProgram.cs`-ல் register செய்யவும்:

```csharp
builder.Services.AddSingleton<PetAssetCatalog>();
builder.Services.AddSingleton<PetPositionStore>();
builder.Services.AddSingleton<PetController>();
builder.Services.AddSingleton<PetOverlayForm>();
builder.Services.AddSingleton<BreakActionService>();
builder.Services.AddSingleton<WellbeingReminderCoordinator>();
// NotificationService already registered; expose it as IBreakReminderToast:
builder.Services.AddSingleton<IBreakReminderToast>(sp => sp.GetRequiredService<NotificationService>());
```

`ShownNotificationLog` ஏற்கனவே singleton (`MauiProgram.cs`) — மீண்டும் register செய்யாதே. `MauiProgram.cs`-ல் user changes உள்ளன: புதிய lines மட்டும் சேர். `tests/ONEVO.Agent.TrayApp.Tests/MauiProgramTests.cs` DI resolve test-ல் புதிய services சேர்.

`PetOverlayForm` DI-ல் singleton ஆக இருந்தாலும் **handle உருவாக்கம் (`Show`) `TrayIconService.Initialize()` ஓடும் UI thread-ல் மட்டும்**. Background thread-ல் resolve ஆனால் form constructor-ல் WinForms control உருவாக்காதே (lazy create).

Coordinator மற்றும் PetController pipe start ஆகுவதற்கு முன் subscribe ஆக வேண்டும்.

Startup order:

1. Main MAUI window உருவாக்கு.
2. Existing tray icon உருவாக்கு.
3. Wellbeing coordinator resolve/subscribe செய்.
4. PetController resolve/subscribe செய்.
5. First authoritative status வரை Pet state முடிவு செய்யாதே.
6. Enrolled employee என்றால் preference அடிப்படையில் overlay show செய்.

Shutdown:

- Pet form dispose.
- Timer stop/dispose.
- Images dispose.
- Events unsubscribe.
- Tray icon dispose.

`TrayIconService` context menu-வில் `Show Pet` / `Hide Pet` சேர்க்கவும். Pet மற்றும் tray menu இரண்டும் ஒரே controller-ஐ பயன்படுத்த வேண்டும்.

## Task 10 — Tests உருவாக்கவும்

Create:

```text
tests/ONEVO.Agent.TrayApp.Tests/Services/Pet/PetControllerTests.cs
tests/ONEVO.Agent.TrayApp.Tests/Services/Pet/PetPositioningTests.cs
tests/ONEVO.Agent.TrayApp.Tests/Services/Pet/PetAssetCatalogTests.cs
tests/ONEVO.Agent.TrayApp.Tests/Services/Pet/PetOverlayContractTests.cs
tests/ONEVO.Agent.TrayApp.Tests/Services/Pet/PetLifetimeTests.cs

tests/ONEVO.Agent.TrayApp.Tests/Services/BreakActionServiceTests.cs
tests/ONEVO.Agent.TrayApp.Tests/Notifications/WellbeingActivationTests.cs
tests/ONEVO.Agent.TrayApp.Tests/Notifications/WellbeingReminderCoordinatorTests.cs

tests/ONEVO.Agent.Service.Tests/AgentWorkerWellnessNotificationAcceptedTests.cs
tests/ONEVO.Agent.Service.Tests/Sync/NotificationPollingServiceTests.cs   (update)
tests/ONEVO.Agent.Shared.Tests/…                                       (payload round-trip)
```

Existing fakes (`tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeNamedPipeClient.cs`, `FakePreferencesStore.cs`) reuse செய்; புதிய duplicate fakes உருவாக்காதே.

Privacy / security tests (architecture §18.4):

- Pet / coordinator / activation logs-ல் notification Title, Message, employee name, email இல்லை.
- Unknown / mismatched notification GUID activation → `StartBreak` அனுப்பப்படவில்லை.
- Pet preferences-ல் `onevo.pet_*` தவிர வேறு key எழுதப்படவில்லை.

Required regression coverage:

- Existing generic notifications unchanged.
- Existing inactivity prompt unchanged.
- Existing long-session reminder unchanged.
- Existing tray Break confirmation unchanged.
- Existing clock-out flow unchanged.
- Existing tray icon click unchanged.

## ஒவ்வொரு task-க்கான கட்டாய workflow

ஒவ்வொரு task-க்கும் இந்த exact order பின்பற்று:

1. Relevant existing implementation முழுமையாகப் படி.
2. Failing unit test எழுது.
3. Focused test run செய்து expected failure உறுதி செய்.
4. குறைந்தபட்ச implementation எழுது.
5. Focused tests pass செய்.
6. Related regression tests run செய்.
7. Diff review செய்து unrelated changes இல்லை என்று உறுதி செய்.
8. அடுத்த task-க்கு செல்.

## Verification commands

### Backend

```powershell
cd C:\HR2\HRMS-Backend-v1
dotnet test .\tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj
```

### Tray shared tests

```powershell
cd C:\HR2\HRMS_TrayApp
dotnet test .\tests\ONEVO.Agent.Shared.Tests\ONEVO.Agent.Shared.Tests.csproj
```

### Agent service tests

```powershell
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj
```

### Tray application tests

```powershell
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj
```

### Tray build

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -c Debug
```

### Full solution (warnings = errors)

```powershell
dotnet build .\ONEVO.Agent.slnx -c Release
dotnet test  .\ONEVO.Agent.slnx -c Release --no-build
```

Coverage thresholds `coverage-thresholds.json`-ல் உள்ளன — புதிய code அவற்றைக் குறைக்கக்கூடாது.

### Footprint (§16)

```powershell
.\scripts\check-footprint.ps1
```

Budget `footprint-budgets.json`. Pet visible + animating நிலையில் ஒருமுறை, hidden நிலையில் ஒருமுறை அளவிடு. Budget-ஐ measured reason இல்லாமல் உயர்த்தாதே.

### Local runtime

```powershell
.\scripts\run-all.ps1
```

`run-all.ps1` PostgreSQL ஏற்கனவே ஓட வேண்டும்; Backend Swagger `https://localhost:7229/swagger`.

## Manual Windows verification

கீழ்கண்டவற்றை real Windows desktop-ல் verify செய்:

- Pet transparent background.
- Pet தனி taskbar button உருவாக்கவில்லை.
- Pet சாதாரண windows-க்கு மேலே உள்ளது.
- Animation focus steal செய்யவில்லை.
- Drag மற்றும் restart position restore.
- Monitor disconnect/reconnect position clamp.
- Pet click main WorkPulse window-ஐ foreground செய்கிறது.
- 239-minute streak reminder உருவாக்கவில்லை.
- 240-minute streak reminder உருவாக்குகிறது.
- Idle/break/sleep/disconnect streak-ஐ reset செய்கிறது.
- Pet bubble மற்றும் Windows toast இரண்டும் action காட்டுகின்றன.
- Simultaneous clicks ஒரு lifecycle command மட்டும் அனுப்புகின்றன.
- Successful action portal/tray/pet-ல் On Break காட்டுகிறது.
- Failed action false On Break காட்டவில்லை.
- Manual Break stale reminder/toast remove செய்கிறது.
- App exit ஆனதும் Pet முழுமையாக dispose ஆகிறது.
- Pet Alt+Tab-ல் தெரியவில்லை.
- Inactivity / periodic screenshot-ல் Pet பதிவாகவில்லை (`WDA_EXCLUDEFROMCAPTURE`).
- ஒரே BreakReminder-க்கு ஒரே ஒரு toast (CollectorCoordinator duplicate இல்லை).
- LongIdleAlert / BreakAllowanceExceeded toasts முன்பு போலவே வருகின்றன.
- Screen lock → Pet மறைகிறது; unlock → authoritative state-க்கு திரும்புகிறது.

Automation குறிப்பு: Pet default position screen bottom-right. இந்த PC-ல் computer-use clicks screen வலது பக்கம் drop ஆகின்றன — UIA Invoke அல்லது `mouse_event` மூலம் click செய் (memory: tray-app-ui-testing).

## Completion criteria

கீழ்கண்ட அனைத்தும் முடிந்த பிறகே feature complete என்று அறிவிக்கவும்:

1. Backend focused tests pass.
2. Full backend unit suite pass.
3. Shared tests pass.
4. Agent Service tests pass.
5. Tray App tests pass.
6. Debug build pass.
7. Pet assets package-ல் உள்ளன.
8. Manual Windows acceptance முடிந்தது அல்லது environment limitation தெளிவாகப் பதிவு செய்யப்பட்டது.
9. Existing user changes overwrite செய்யப்படவில்லை.
10. Architecture §20 checklist கீழே tick செய்யப்பட்டது.
11. `check-footprint.ps1` budget-க்குள்.
12. Final response-ல் modified files, test commands/results, manual verification status மற்றும் remaining limitations கொடுக்கப்பட்டுள்ளன.

## Architecture §20 checklist (இந்த feature-க்கு)

- [ ] Phase 1 scope — UI companion + existing break lifecycle; புதிய collection இல்லை.
- [ ] Authorization — backend `BreakReminder` notification மட்டுமே trigger; tray தானாக reminder உருவாக்காது.
- [ ] Consent/presence — Pet Active/Paused state-ஐ மட்டும் render; Unenrolled/Locked → hidden.
- [ ] Service/Tray boundary — HTTP + ack Service-ல்; UI Tray-ல்; contract Shared-ல்.
- [ ] Data minimization — IPC payload GUID மட்டும்; logs-ல் content இல்லை.
- [ ] Storage — `onevo.pet_*` UI prefs + existing `ShownNotificationLog` (ids மட்டும், bounded) மட்டும்.
- [ ] Idempotency — notification ID dedupe; duplicate acceptance harmless; `ALREADY_IN_STATE` handled.
- [ ] Device JWT / tenant trust மாற்றம் இல்லை.
- [ ] IPC versioned (`"1.0"` unchanged, additive), bounded, authenticated pipe.
- [ ] Failure → break அனுப்பப்படாது, `OnBreak` காட்டப்படாது (fail closed).
- [ ] CPU/RAM — footprint script pass.
- [ ] Unit + privacy tests சேர்க்கப்பட்டன.
- [ ] Installer/MSIX — Pet PNGs `EmbeddedResource` ஆக assembly-க்குள்; MSIX manifest மாற்றம் இல்லை; `build-msix.ps1` ஒருமுறை verify.

## AI final response format

Implementation முடிந்ததும் கீழ்கண்ட வடிவத்தில் report செய்:

```markdown
## முடிவு
[Feature வேலை செய்கிறதா என்பதன் சுருக்கம்]

## மாற்றப்பட்ட பகுதிகள்
- Backend continuity
- Notification acknowledgement
- Break action
- Pet overlay
- App integration

## Tests
- Command: ...
  Result: PASS/FAIL

## Manual verification
- ...

## மீதமுள்ள வரம்புகள்
- ...
```
