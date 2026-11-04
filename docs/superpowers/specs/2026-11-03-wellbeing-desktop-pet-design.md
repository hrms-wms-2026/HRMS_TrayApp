# Wellbeing Desktop Pet Design

## Intent

Add a small, animated Windows desktop pet to OneXso WorkPulse. The pet gives an employee a friendly, actionable wellbeing reminder after four hours of genuinely continuous active work. Clicking the reminder's **Take a Break** button starts the existing ONEVO attendance break, and clicking the pet itself brings the existing tray application to the foreground.

The pet is a companion surface for existing WorkPulse capabilities, not a second attendance system. The backend remains the source of truth for continuous-work evaluation and attendance state; the tray app remains the only desktop process the employee installs.

## Goals

- Show a lightweight pet near the bottom-right of the employee's Windows desktop while WorkPulse is running.
- Keep the pet above ordinary windows without adding another taskbar button.
- Allow the employee to drag the pet and remember its last valid on-screen position.
- Open and foreground the existing WorkPulse tray window when the pet is clicked.
- Change the pet's presentation for ready, working, break-due, on-break, disconnected, and hidden states.
- Detect four hours (240 minutes) of continuous active work without counting breaks, idle samples, sleep, shutdown, disconnection, or missing sample gaps as continuous work.
- Show an actionable pet bubble and Windows notification with a **Take a Break** action.
- Start the existing attendance break through the tray-to-service lifecycle pipeline; do not automate a browser click.
- Reflect the resulting break state in the tray, pet, backend, and website through the existing attendance state synchronization.
- Avoid duplicate reminder surfaces for the same backend notification.
- Preserve existing notification, tray-icon, break-confirmation, clock-out, and collector behavior.

## Non-goals

- A general-purpose pet marketplace, pet editor, or downloadable pet catalogue.
- Cross-platform support; this iteration is Windows-only.
- Replacing the system tray icon or the main WorkPulse window with the pet.
- Making the pet a monitoring control or exposing screenshot/activity data through it.
- Simulating clicks in the web portal.
- Adding employee-selectable wellbeing thresholds in this iteration.
- Adding audio, speech, or generative pet behavior.

## Constraints and Product Calls

- The continuous-work threshold is exactly 240 active minutes.
- A gap greater than 90 seconds between one-minute device-state samples breaks continuity.
- An idle sample breaks the active streak.
- The backend evaluates a five-hour lookback so a complete 240-minute streak plus scheduling jitter is available.
- Existing backend notification cooldown behavior remains in place for this iteration; notification IDs provide desktop deduplication.
- The **Take a Break** action is direct: it does not open the existing confirmation overlay first.
- A direct pet/toast break still performs the same best-effort collector pre-stop/drain sequence as the existing confirmed tray break before it sends `LifecycleAction.StartBreak`; direct refers to confirmation UX, not to bypassing lifecycle safety. As in the existing flow, a local drain exception does not deny the employee's break. If the lifecycle request is rejected while authoritative state remains Active, eligible collectors are resumed.
- Direct break start is allowed only when the service reports an active session and the server reports `CanStartBreak = true`.
- A failed or timed-out break request keeps the employee in Working state and shows an error bubble; the client never assumes success.
- `ALREADY_IN_STATE` is reconciled from the returned status and treated as success only when the authoritative state is Paused.
- The default pet is visible after enrollment; an employee may hide it from the pet or tray context menu. The preference persists locally.
- The first release uses a replaceable, bundled blue mascot frame-set created for the feature: 160x160 transparent PNG files named `pet_<state>_<frame>.png`, with states `idle`, `working`, `break_due`, `on_break`, and `offline`. Each state may initially repeat a single frame; adding frames later requires no code change. Animation is timer-driven. This avoids relying on GIF behavior across MAUI/WinForms versions and gives final artwork a fixed drop-in contract.
- The pet overlay is a small WinForms borderless form because the existing Windows tray app already references Windows Forms for `NotifyIcon`. It uses a transparency key, `TopMost`, `ShowInTaskbar = false`, and no activation on show.
- The pet is not click-through: left-click opens WorkPulse and dragging repositions it. It must not steal focus merely because its state or animation changes.
- Multi-monitor position restore clamps the pet to the nearest current working area so disconnecting a monitor cannot strand it off-screen.
- Pet assets are decorative. The actionable button and status text expose accessible names and do not depend on color or animation alone.

## Existing Foundation

The tray application already provides:

- a system tray icon and `TrayIconService.ShowMainWindow()` foreground behavior;
- close-to-tray behavior for the main MAUI window;
- `LifecycleAction.StartBreak` and `LifecycleAction.EndBreak` over the authenticated named pipe;
- authoritative attendance snapshots including `IsOnBreak`, `CanStartBreak`, and break allowance;
- Windows notification registration and a single activation callback;
- backend polling and IPC delivery for monitoring wellness notifications;
- a backend `WellnessRuleEvaluator` and `WellnessRuleEvaluatorJob`.

The implementation extends these paths rather than adding a new executable or attendance API.

## User Experience

### Normal states

- **Ready:** pet is calm when the enrolled employee is not clocked in.
- **Working:** pet shows a subtle work animation while monitoring state is Active.
- **On break:** pet shows a rest/coffee animation while monitoring state is Paused.
- **Disconnected:** pet shows a sleeping/offline presentation when the tray loses the service connection.
- **Hidden:** only the tray icon remains; the employee can restore the pet from the tray context menu.
- **Unenrolled/locked:** the overlay is absent. The normal tray window continues to own setup and recovery.

### Break-due state

When a backend `BreakReminder` notification arrives, the pet shows a bubble above itself:

> Time for a break  
> You've been active for 4 hours. Take a short break.  
> **Take a Break**

The same notification is also presented as an actionable Windows toast so the action remains available when the pet is covered by a full-screen or focus-assist scenario. Both surfaces share one notification ID and one action coordinator; repeated clicks cannot create two break sessions.

### Interaction

- Left-clicking the pet calls the existing foreground helper and navigates the main window to the active-session route when a session is active or paused.
- Pressing **Take a Break** disables the button and sends one start-break request.
- On success, the bubble closes and the pet transitions to On break from the authoritative lifecycle response/status push.
- On failure, the pet stays in Working/Break due, re-enables the button, and displays a concise failure message.
- Right-clicking the pet offers **Open ONEVO**, **Hide Pet**, and **Exit**.
- The tray icon context menu adds **Show Pet** or **Hide Pet** using the same preference/controller.

## Architecture

### Backend continuity and notification

`WellnessRuleEvaluator` scans newest-to-oldest device-state snapshots. It stops at an idle sample, a change in employee/device stream, or a `CapturedAt` gap over 90 seconds. A streak reaches the reminder threshold at 240 samples/minutes. `WellnessRuleEvaluatorJob` requests a five-hour lookback and continues to create the existing `BreakReminder` notification and push signal.

No browser or tray timer decides that four hours elapsed. This prevents clock-in time, sleep time, break time, and disconnected time from being treated as active work.

### Tray reminder coordinator

`WellbeingReminderCoordinator` subscribes to `INamedPipeClient.OnNotificationReceived`. For a `BreakReminder`, it atomically claims the notification ID, updates the pet controller, and asks `NotificationService` to show an actionable Windows toast. Other wellness notification types retain their current warning/info presentation.

Wellness delivery adopts the same acceptance boundary as generic tray delivery: the service does not acknowledge a backend wellness notification merely because it wrote an IPC message. The tray sends `WellnessNotificationAccepted(notificationId)` only after it has claimed the ID and at least one local surface (pet bubble or Windows toast) accepted presentation. The service then calls the existing backend acknowledgement endpoint. If the tray is closed or crashes first, the notification remains pending and is offered again after restart.

`WellbeingActivation` owns the stable notification arguments (`kind=wellbeing;action=start_break;notification=<guid>`). The existing single Windows `NotificationInvoked` subscription routes those arguments to the coordinator.

### Shared break action

`BreakActionService` is the one direct-break entry point for pet and toast actions. It:

1. rejects concurrent calls;
2. validates the last authoritative status is Active with an open session and `CanStartBreak`;
3. runs the existing best-effort `PrepareForPauseAsync` operation used by confirmed tray breaks, continuing on a local drain exception;
4. calls `INamedPipeClient.SendLifecycleAsync(LifecycleAction.StartBreak, ...)`;
5. calls `ResumeAfterRejectedPauseAsync` when the returned authoritative state remains Active after rejection;
6. returns a typed result without changing UI state optimistically;
7. lets the existing status/state events drive the main tray page and pet into Paused.

The existing Active Session confirmation flow remains unchanged in this iteration to avoid mixing the user's explicit tray confirmation behavior with the pet's explicitly direct action.

### Pet overlay

`PetOverlayForm` owns only Windows presentation and pointer events. `PetController` owns state, visibility, bubble content, animation frame selection, position persistence, and commands. It subscribes to pipe state/status/disconnect events and to the wellbeing coordinator. UI updates marshal to the WinForms UI context.

The overlay form is created and disposed with the MAUI app lifetime. It never starts a second `Application.Run` message loop. Animation uses a low-frequency timer and stops while the pet is hidden.

## State Mapping

| Authoritative condition | Pet state |
|---|---|
| Service disconnected | Disconnected |
| Monitoring `Paused` / session `IsOnBreak` | OnBreak |
| Active + outstanding break reminder | BreakDue |
| Monitoring `Active` | Working |
| Enrolled + Stopped | Ready |
| Unenrolled/locked | Hidden |

Disconnected and OnBreak take precedence over an outstanding reminder. Entering Paused from any source, including a manual main-tray break, consumes the outstanding reminder, removes its tagged Windows toast, and closes the pet bubble so a stale action cannot remain clickable. A later reminder requires a new backend notification ID.

## Data and Privacy

- Pet preferences contain only visibility and screen coordinates in the existing local preference store.
- Notification titles/messages are not written to logs because they may contain employee-specific text.
- Logs record notification IDs, action names, state transitions, and result/error codes only.
- The pet does not expose activity details, screenshots, application names, or location.

## Failure Handling

- If the pet overlay cannot be created, the tray and Windows toast continue to work.
- If the Windows toast cannot be displayed, the pet bubble remains actionable.
- If the service is unavailable, break action is disabled and no lifecycle command is sent.
- If the backend rejects the break because allowance is exhausted, the pet shows the returned message and the next authoritative status disables the action.
- If the app restarts before tray acceptance, the backend notification remains pending and is re-offered. After tray acceptance, the local shown-ID store prevents repeated presentation during the same local retention window while the backend acknowledgement completes.
- If a monitor is removed or DPI changes, the next show/drag completion clamps the pet inside a current working area.

## Verification

- Backend unit tests cover 239/240 active minutes, idle interruption, 91-second missing-sample gap, and exact 90-second tolerance.
- Tray unit tests cover activation parsing, duplicate click suppression, disconnected/allowance rejection, lifecycle success/failure, and pet state precedence.
- Overlay logic tests cover position clamping and drag persistence without requiring a visible desktop.
- Existing tray, service, and backend unit suites remain green.
- Manual Windows verification covers animation, no focus stealing, pet/tray foregrounding, multi-monitor positioning, toast button activation, website state synchronization, restart, sleep/resume, and MSIX asset packaging.

## Acceptance Criteria

1. No reminder is generated at 239 continuous active minutes.
2. One `BreakReminder` is generated at 240 continuous active minutes.
3. Idle, break, sleep, disconnect, or a sample gap over 90 seconds resets continuity.
4. The pet displays the break-due bubble for a delivered break reminder.
5. The pet and Windows toast actions converge on one idempotent break request.
6. A successful action produces authoritative Paused/On break state in tray, pet, backend, and portal.
7. A failed action never shows a false On break state.
8. Clicking the pet opens/restores and foregrounds the WorkPulse window.
9. Hiding, showing, dragging, restarting, and changing monitors preserve a usable pet position and preference.
10. Existing tray-icon, main-window break confirmation, clock-out, generic notification, and inactivity-prompt behavior is unchanged.
