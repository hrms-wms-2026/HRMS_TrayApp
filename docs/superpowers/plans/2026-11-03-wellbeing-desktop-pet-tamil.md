# ONEVO Wellbeing Desktop Pet — முழுமையான செயல்படுத்தும் திட்டம்

## 1. நோக்கம்

ONEVO WorkPulse Tray App-க்குள் Windows Desktop Pet ஒன்றை உருவாக்க வேண்டும்.

இந்த Pet செய்ய வேண்டியவை:

- Employee வேலை செய்யும் போது Desktop-ல் காணப்பட வேண்டும்.
- தொடர்ந்து 4 மணி நேரம் வேலை செய்தால் Break reminder காட்ட வேண்டும்.
- Pet notification-ல் **Break எடுக்கவும்** button இருக்க வேண்டும்.
- Button அழுத்தியவுடன் existing ONEVO attendance break ஆரம்பிக்க வேண்டும்.
- Website, Tray App மற்றும் Pet மூன்றிலும் `On Break` நிலை வர வேண்டும்.
- Pet-ஐ click செய்தால் மறைந்திருக்கும் Tray App முன்னால் திறக்க வேண்டும்.
- Pet-ஐ drag செய்து வேறு இடத்தில் வைக்க முடியும்.
- Pet position மற்றும் visible preference restart ஆன பிறகும் தொடர வேண்டும்.

---

## 2. பயன்படுத்தப்படும் தொழில்நுட்பங்கள்

புதிதாக Electron, WPF அல்லது வேறு பெரிய framework சேர்க்க வேண்டாம்.

Existing project-ல் இருப்பவையே போதும்:

| தொழில்நுட்பம் | பயன்பாடு |
|---|---|
| .NET 10 | Application runtime |
| .NET MAUI | Existing WorkPulse main window |
| Windows Forms | Transparent floating Pet window |
| Microsoft Windows App SDK | Windows notification மற்றும் activation |
| CommunityToolkit.Mvvm | State மற்றும் commands |
| System.Drawing | PNG frames மற்றும் drawing |
| Named Pipes | Tray App மற்றும் Agent Service communication |
| xUnit | Unit tests |
| coverlet.collector | Test coverage |

Windows Forms support ஏற்கனவே project-ல் உள்ளது:

```xml
<FrameworkReference Include="Microsoft.WindowsDesktop.App.WindowsForms" />
```

அடிப்படை Pet build-க்கு புதிய NuGet package தேவையில்லை.

---

## 3. Development environment

தேவையானவை:

- Windows 10/11
- .NET 10 SDK
- Visual Studio அல்லது Rider
- .NET MAUI Windows workload
- Windows App Runtime 1.8

சரிபார்க்க:

```powershell
dotnet --info
dotnet workload list
```

MAUI Windows workload இல்லையென்றால்:

```powershell
dotnet workload install maui-windows
```

Project restore:

```powershell
cd C:\HR2\HRMS_TrayApp
dotnet restore
```

---

## 4. Pet asset திட்டம்

இந்த folder உருவாக்க வேண்டும்:

```text
ONEVO.Agent.TrayApp
└── Resources
    └── Pet
```

முதல் version-க்கு தேவையான படங்கள்:

```text
pet_idle_00.png
pet_working_00.png
pet_break_due_00.png
pet_on_break_00.png
pet_offline_00.png
```

ஒவ்வொரு படமும்:

- `160 × 160` pixels
- Transparent background
- PNG format
- ஒரே Pet character
- குறைந்த file size

Animation சேர்க்கும்போது:

```text
pet_idle_01.png
pet_idle_02.png
pet_working_01.png
pet_working_02.png
```

என்று கூடுதல் frames சேர்க்கலாம்.

Project file-ல் resources embed செய்ய வேண்டும்:

```xml
<ItemGroup>
  <EmbeddedResource
    Include="Resources\Pet\*.png"
    LogicalName="ONEVO.Agent.TrayApp.Pet.%(Filename)%(Extension)" />
</ItemGroup>
```

---

## 5. உருவாக்க வேண்டிய files

```text
ONEVO.Agent.TrayApp
├── Pet
│   ├── PetState.cs
│   ├── PetPresentation.cs
│   ├── PetAssetCatalog.cs
│   ├── PetPositionStore.cs
│   ├── PetPositioning.cs
│   ├── PetController.cs
│   └── PetOverlayForm.cs
│
└── Services
    ├── BreakActionService.cs
    ├── WellbeingActivation.cs
    └── WellbeingReminderCoordinator.cs
```

---

## 6. Pet நிலைகள்

`PetState.cs`:

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

State mapping:

| ONEVO நிலை | Pet நிலை |
|---|---|
| Enrolled + வேலை ஆரம்பிக்கவில்லை | Ready |
| Active session | Working |
| 4 மணி நேர reminder | BreakDue |
| Break ஆரம்பித்தது | OnBreak |
| Agent connection இல்லை | Disconnected |
| Pet மறைக்கப்பட்டது | Hidden |
| Unenrolled அல்லது Locked | Pet காட்ட வேண்டாம் |

`PetPresentation.cs` கீழ்கண்டவற்றை வைத்திருக்க வேண்டும்:

- தற்போதைய Pet state
- காட்ட வேண்டிய image frame
- Bubble title
- Bubble message
- Break button visibility
- Loading state
- Error message

---

## 7. Transparent Pet window

`PetOverlayForm.cs` Windows Forms பயன்படுத்த வேண்டும்.

முக்கிய properties:

```csharp
FormBorderStyle = FormBorderStyle.None;
ShowInTaskbar = false;
TopMost = true;
StartPosition = FormStartPosition.Manual;
BackColor = Color.Magenta;
TransparencyKey = Color.Magenta;
Width = 160;
Height = 160;
```

இதனால்:

- Window border இருக்காது.
- தனி taskbar button வராது.
- Background transparent ஆகும்.
- Pet மற்ற சாதாரண windows-க்கு மேலே தெரியும்.

Pet form-க்குள்:

- `PictureBox`
- Notification bubble
- Break button
- Error label
- `ContextMenuStrip`

இருக்க வேண்டும்.

புதிதாக `Application.Run()` அழைக்கக்கூடாது. Existing MAUI/Windows message loop-க்குள் `PetOverlayForm.Show()` செய்ய வேண்டும்.

---

## 8. Pet animation

`System.Windows.Forms.Timer` பயன்படுத்த வேண்டும்:

```csharp
_animationTimer.Interval = 250;
_animationTimer.Tick += (_, _) => ShowNextFrame();
```

விதிகள்:

- Pet visible என்றால் timer இயங்கலாம்.
- Pet hidden என்றால் timer நிறுத்த வேண்டும்.
- App closing நேரத்தில் timer dispose செய்ய வேண்டும்.
- Image மாற்றும்போது பழைய image object-ஐ dispose செய்ய வேண்டும்.
- ஒரே frames-ஐ ஒவ்வொரு tick-லும் disk/resource-லிருந்து reload செய்யக்கூடாது.

---

## 9. Pet drag மற்றும் position save

Mouse events:

```text
MouseDown → ஆரம்ப mouse position save
MouseMove → Pet location மாற்றுதல்
MouseUp   → Final position save
```

Save செய்ய வேண்டிய values:

```text
PetVisible
PetX
PetY
```

Existing `IPreferencesStore` பயன்படுத்த வேண்டும்.

Restart ஆன பிறகு saved position-ல் Pet வர வேண்டும்.

Saved position தற்போதைய monitors-க்கு வெளியே இருந்தால் nearest working area-க்குள் position clamp செய்ய வேண்டும். Monitor கிடைக்கவில்லை என்றால் primary monitor bottom-right default position பயன்படுத்த வேண்டும்.

---

## 10. PetController

`PetController.cs` மட்டுமே Pet state மற்றும் behavior-ஐ control செய்ய வேண்டும்.

`PetOverlayForm` backend அல்லது Named Pipe-ஐ நேரடியாக access செய்யக்கூடாது.

```text
Named Pipe / Backend Event
           ↓
PetController
           ↓
PetPresentation
           ↓
PetOverlayForm
```

PetController செய்ய வேண்டியவை:

- State/status events subscribe செய்தல்
- Disconnection handle செய்தல்
- Reminder received event handle செய்தல்
- Show/Hide preference handle செய்தல்
- Pet bubble content update செய்தல்
- Break command தொடங்குதல்
- Main tray window திறத்தல்
- Shutdown-ல் events unsubscribe செய்தல்

---

## 11. Pet interaction

### Left-click

Pet click செய்தால்:

```csharp
TrayIconService.ShowMainWindow();
```

Employee Active அல்லது On Break நிலையில் இருந்தால்:

```csharp
await Shell.Current.GoToAsync("//active");
```

இதனால் Tray App:

- Hidden என்றால் show ஆகும்.
- Minimized என்றால் restore ஆகும்.
- மற்ற windows-க்கு முன்னால் வரும்.

### Right-click menu

```text
Open ONEVO
Hide Pet
Exit
```

Tray icon menu-விலும் `Show Pet` அல்லது `Hide Pet` சேர்க்க வேண்டும்.

---

## 12. நான்கு மணி நேர continuous-work கணக்கு

Backend file:

```text
HRMS-Backend-v1/src/ONEVO.Infrastructure/Services/Monitoring/Notifications/
WellnessRuleEvaluator.cs
```

Threshold:

```csharp
public const int BreakReminderThresholdMinutes = 240;
```

Maximum continuous sample gap:

```csharp
public static readonly TimeSpan MaximumContinuousSampleGap =
    TimeSpan.FromSeconds(90);
```

Backend lookback:

```csharp
TimeSpan.FromHours(5)
```

கீழ்கண்டவை ஏற்பட்டால் active streak reset ஆக வேண்டும்:

- Idle sample
- Break
- Clock out
- Computer sleep/shutdown
- Agent disconnection
- 90 seconds-க்கு மேலான sample gap
- Employee அல்லது Agent device மாற்றம்

சோதனைகள்:

- 239 நிமிடங்கள் → reminder இல்லை.
- 240 நிமிடங்கள் → reminder வரும்.
- Exactly 90-second gap → தொடரலாம்.
- 91-second gap → reset.
- நடுவில் idle sample → reset.
- Device மாறினால் → reset.

---

## 13. Notification delivery acknowledgement

Backend notification அனுப்பிய உடனேயே acknowledged என்று கருதக்கூடாது.

சரியான flow:

```text
Backend notification
        ↓
Agent Service
        ↓
Tray App
        ↓
Pet அல்லது Windows notification காட்டப்பட்டது
        ↓
Tray App acceptance message
        ↓
Backend acknowledgement
```

Shared IPC-ல் சேர்க்க வேண்டியது:

```csharp
public const string WellnessNotificationAccepted =
    "WellnessNotificationAccepted";

public sealed record WellnessNotificationAcceptedPayload(
    Guid NotificationId);
```

Tray App மூடப்பட்டிருந்தால் notification backend-ல் pending-ஆக இருக்க வேண்டும். அடுத்த startup-ல் மீண்டும் வழங்கப்பட வேண்டும்.

---

## 14. BreakActionService

`BreakActionService.cs` Pet மற்றும் Windows notification இரண்டுக்கும் பொதுவான Break service.

Break தொடங்குவதற்கு முன் பார்க்க வேண்டியது:

- Agent Service connected-ஆக இருக்கிறதா?
- Employee Active session-ல் இருக்கிறாரா?
- ஏற்கனவே Break-ல் இருக்கிறாரா?
- `CanStartBreak == true` ஆக இருக்கிறதா?
- வேறு Break request நடக்கிறதா?

Flow:

```text
Break button click
        ↓
Concurrent request guard
        ↓
Eligibility check
        ↓
PrepareForPauseAsync()
        ↓
SendLifecycleAsync(StartBreak)
        ↓
Authoritative backend result
```

Lifecycle request:

```csharp
await _pipe.SendLifecycleAsync(
    LifecycleAction.StartBreak,
    cancellationToken);
```

Collector drain:

```csharp
await _collectorLifecycle.PrepareForPauseAsync(cancellationToken);
```

Existing flow போல local drain exception வந்தாலும் employee Break தடுக்கப்படக்கூடாது. Backend Break request reject செய்து authoritative state Active என்று வந்தால்:

```csharp
await _collectorLifecycle.ResumeAfterRejectedPauseAsync(cancellationToken);
```

அழைக்க வேண்டும்.

Backend வெற்றி சொல்வதற்கு முன் Pet-ஐ `OnBreak` என்று மாற்றக்கூடாது.

---

## 15. Pet reminder bubble

4 மணி நேரம் முடிந்தால்:

```text
┌────────────────────────────────────┐
│ ஓய்வு எடுக்க வேண்டிய நேரம்!         │
│ தொடர்ந்து 4 மணி நேரம் வேலை செய்தீர்கள் │
│                                    │
│          [Break எடுக்கவும்]          │
└────────────────────────────────────┘
                   Pet
```

Button click:

- Button disable செய்ய வேண்டும்.
- Loading state காட்ட வேண்டும்.
- `BreakActionService.StartBreakAsync()` call செய்ய வேண்டும்.

Success:

- Bubble close.
- Tagged Windows notification remove.
- Authoritative Paused status வந்ததும் Pet `OnBreak`.

Failure:

- Pet `BreakDue` நிலையிலேயே இருக்க வேண்டும்.
- Button மீண்டும் enable ஆக வேண்டும்.
- Error message காட்ட வேண்டும்.

Pet மற்றும் Windows notification button ஒரே நேரத்தில் அழுத்தப்பட்டாலும் ஒரே Break request மட்டுமே செல்ல வேண்டும்.

---

## 16. Windows notification

Existing `NotificationService`-ல் method சேர்க்க வேண்டும்:

```csharp
bool ShowBreakReminder(
    NotificationPushPayload payload,
    string activationArguments);
```

Notification:

```csharp
new AppNotificationBuilder()
    .AddText("Time for a break")
    .AddText("You have been working continuously for 4 hours.")
    .AddButton(
        new AppNotificationButton("Take a Break")
            .AddArgument("kind", "wellbeing")
            .AddArgument("action", "start_break")
            .AddArgument("notification", notificationId))
    .BuildNotification();
```

Activation arguments:

```text
kind=wellbeing;
action=start_break;
notification=<guid>
```

Existing single `NotificationInvoked` callback மூலமாக route செய்ய வேண்டும். புதிதாக இன்னொரு global notification callback உருவாக்கக்கூடாது.

---

## 17. WellbeingReminderCoordinator

`WellbeingReminderCoordinator.cs` செய்ய வேண்டியவை:

1. `BreakReminder` notification பெறுதல்.
2. Duplicate notification ID தடுத்தல்.
3. Pet-ஐ `BreakDue` நிலைக்கு மாற்றுதல்.
4. Windows notification காட்டுதல்.
5. Pet/Windows button action-ஐ `BreakActionService`-க்கு அனுப்புதல்.
6. Pet அல்லது toast காட்டப்பட்ட பிறகு acceptance அனுப்புதல்.
7. Employee manual-ஆக Break எடுத்தாலும் பழைய bubble/toast remove செய்தல்.
8. Paused state வந்ததும் reminder consume செய்தல்.

தேவையான public methods/events:

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

Pet-க்கு:

```text
BreakReminderRaised
BreakReminderCleared
```

events வழங்க வேண்டும்.

---

## 18. Dependency Injection

`MauiProgram.cs`-ல்:

```csharp
builder.Services.AddSingleton<PetAssetCatalog>();
builder.Services.AddSingleton<PetPositionStore>();
builder.Services.AddSingleton<PetController>();
builder.Services.AddSingleton<PetOverlayForm>();

builder.Services.AddSingleton<BreakActionService>();
builder.Services.AddSingleton<WellbeingReminderCoordinator>();
```

Pipe communication தொடங்குவதற்கு முன் coordinator subscribe ஆக வேண்டும். அதனால் app startup-ல் coordinator eager-ஆக resolve செய்ய வேண்டும்.

---

## 19. App startup மற்றும் shutdown

### Startup

```text
Main tray window உருவாக்குதல்
        ↓
System tray icon உருவாக்குதல்
        ↓
PetController subscribe செய்தல்
        ↓
Wellbeing coordinator subscribe செய்தல்
        ↓
Employee enrollment/status பெறுதல்
        ↓
Pet preference load செய்தல்
        ↓
Pet காட்டுதல்
```

### Shutdown

- Animation timer stop.
- Pet images dispose.
- Pet overlay close.
- Pipe events unsubscribe.
- Notification events unsubscribe.
- Tray icon dispose.
- Pet தனி process போல மீதமிருக்கக்கூடாது.

---

## 20. உருவாக்க வேண்டிய tests

```text
tests/ONEVO.Agent.TrayApp.Tests
├── Pet
│   ├── PetControllerTests.cs
│   ├── PetPositioningTests.cs
│   ├── PetAssetCatalogTests.cs
│   ├── PetOverlayContractTests.cs
│   └── PetLifetimeTests.cs
│
└── Services
    ├── BreakActionServiceTests.cs
    ├── WellbeingActivationTests.cs
    └── WellbeingReminderCoordinatorTests.cs
```

Test செய்ய வேண்டியவை:

- Pet state mapping.
- 239/240-minute boundary.
- Idle மற்றும் missing-sample reset.
- Duplicate Break request prevention.
- Disconnected நிலையில் Break மறுப்பு.
- Break allowance இல்லாதபோது மறுப்பு.
- Backend failure false `OnBreak` காட்டாதது.
- Manual Break பழைய reminder-ஐ remove செய்வது.
- Pet position monitor-க்கு வெளியே போகாதது.
- Notification presentation acceptance.
- App shutdown resources dispose செய்வது.

---

## 21. Build மற்றும் test commands

### Backend unit tests

```powershell
cd C:\HR2\HRMS-Backend-v1
dotnet test .\tests\ONEVO.Tests.Unit\ONEVO.Tests.Unit.csproj
```

### Tray tests

```powershell
cd C:\HR2\HRMS_TrayApp

dotnet test .\tests\ONEVO.Agent.Shared.Tests\ONEVO.Agent.Shared.Tests.csproj
dotnet test .\tests\ONEVO.Agent.Service.Tests\ONEVO.Agent.Service.Tests.csproj
dotnet test .\tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj
```

### Tray build

```powershell
dotnet build .\ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -c Debug
```

### Local run

```powershell
.\scripts\run-all.ps1
```

---

## 22. செயல்படுத்தும் வரிசை

கீழ்கண்ட வரிசையில் செய்ய வேண்டும்:

1. Pet PNG asset contract உருவாக்குதல்.
2. `PetState`, presentation மற்றும் position logic உருவாக்குதல்.
3. Position மற்றும் state unit tests எழுதுதல்.
4. Transparent `PetOverlayForm` உருவாக்குதல்.
5. Pet animation, drag மற்றும் context menu சேர்த்தல்.
6. Backend 4-hour continuity logic மாற்றுதல்.
7. Backend continuity tests pass செய்தல்.
8. Wellness notification acceptance IPC சேர்த்தல்.
9. `BreakActionService` உருவாக்குதல்.
10. Actionable Windows notification சேர்த்தல்.
11. `WellbeingReminderCoordinator` உருவாக்குதல்.
12. Pet reminder bubble-ஐ coordinator-க்கு இணைத்தல்.
13. Pet click-ஐ existing tray foreground logic-க்கு இணைத்தல்.
14. Tray icon-ல் Show/Hide Pet menu சேர்த்தல்.
15. App startup/shutdown lifetime wiring செய்தல்.
16. அனைத்து unit tests-ஐ run செய்தல்.
17. Debug மற்றும் MSIX build சரிபார்த்தல்.
18. Real Windows machine-ல் manual acceptance test செய்தல்.

---

## 23. இறுதி acceptance checklist

- [ ] Desktop-ல் Pet தெரிகிறது.
- [ ] Pet background transparent.
- [ ] Pet மற்ற சாதாரண windows-க்கு மேலே தெரிகிறது.
- [ ] Pet தனி taskbar button உருவாக்கவில்லை.
- [ ] Pet drag செய்ய முடிகிறது.
- [ ] Restart ஆன பிறகு position சரியாக வருகிறது.
- [ ] Monitor மாற்றினாலும் Pet screen-க்கு வெளியே போகவில்லை.
- [ ] Pet click செய்தால் Tray App foreground-ல் வருகிறது.
- [ ] 239 நிமிடங்களுக்கு reminder வரவில்லை.
- [ ] 240 நிமிடங்களுக்கு reminder வருகிறது.
- [ ] Idle/break/sleep/disconnect streak-ஐ reset செய்கிறது.
- [ ] Pet bubble-ல் Break button வருகிறது.
- [ ] Windows notification-லும் Break button வருகிறது.
- [ ] இரண்டு buttons ஒரே நேரத்தில் அழுத்தினாலும் ஒரு request மட்டும் செல்கிறது.
- [ ] Successful Break website-ல் `On Break` ஆகிறது.
- [ ] Tray App `On Break` ஆகிறது.
- [ ] Pet `OnBreak` animation காட்டுகிறது.
- [ ] API fail ஆனால் false `On Break` காட்டவில்லை.
- [ ] Manual Break பழைய reminder-ஐ remove செய்கிறது.
- [ ] App restart duplicate reminder உருவாக்கவில்லை.
- [ ] App exit செய்ததும் Pet முழுமையாக close ஆகிறது.

---

## 24. முக்கியக் குறிப்பு

Pet ஒரு தனி attendance system அல்ல. Pet, Windows notification மற்றும் main Tray App அனைத்தும் existing ONEVO backend மற்றும் `LifecycleAction.StartBreak` flow-ஐ மட்டுமே பயன்படுத்த வேண்டும்.

Website button-ஐ browser automation மூலம் click செய்யக்கூடாது. Backend state update ஆன பிறகு Website, Tray App மற்றும் Pet அனைத்தும் ஒரே authoritative attendance நிலையை காட்ட வேண்டும்.
