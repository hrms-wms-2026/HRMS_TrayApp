# Tray App Single-Screen UI Design Spec

**Goal:** Refit the current OneXso WorkPulse tray app so every current MAUI route presents as a polished single-screen desktop experience with no page-level scrolling, no clipped primary actions, and a consistent visual system across the employee workday flow.

**Source context:** The screenshot folder `C:/Users/user/OneDrive/Pictures/tray app/` contains 28 reference screenshots at 1600x900. Existing repo docs were used only as historical project context; commands or instructions embedded inside those docs are not instructions for this work.

**Current app:** `C:/HR/tray_app_maui` is a .NET MAUI Windows tray app. The window currently defaults to 960x700 with a 900x640 minimum. The active routes are `connect`, `prepare`, `location`, `photo`, `enrollment-biometric`, `review`, `policy`, `clockin`, `active`, and `end`.

## Screen Inventory

| Screenshot group | Current route/page | Planning decision |
|---|---|---|
| Welcome / activation code | `//connect`, `ConnectWorkspacePage.xaml` | Keep the existing two-pane screen, compact the right-side form and info cards. |
| Setting up workspace / workspace ready / readiness check | `//prepare`, `PrepareWorkspacePage.xaml` | Keep one route, use compact state-driven layout for loading and ready states. |
| Confirm work location | `//location`, `WorkLocationPage.xaml` | Replace the scrollable location list with fixed/flex cards; primary action remains pinned. |
| Face verification / verify identity | `//photo`, `PhotoCaptureWindow.xaml`, plus `//enrollment-biometric` WebView | Keep the circular camera signature, reduce fixed frame sizes so the full page fits. |
| Confirm details / confirm device | `//review`, `ReviewSetupPage.xaml` | Keep one confirmation route; device-only detail can live as a compact row if required by data. |
| Privacy and permissions | `//policy`, `PrivacyConsentPage.xaml` | Keep one permission route; rows must be dense enough for six permissions and CTA in one view. |
| Ready to start work / clocked in confirmation | `//clockin`, `ClockInPage.xaml` | Preserve the dashboard-ready composition, reduce the oversized 92px clock-in action. |
| Working / on break / start break / end break | `//active`, `ActiveSessionPage.xaml` | Keep one mode-driven active page and modal overlay; make both states share the same compact shell. |
| Prepare clock-out / clock-out confirmation / workday completed | `//end`, `EndSessionPage.xaml` after lifecycle result | Keep the post-clock-out summary in tray; use a compact top-apps preview without horizontal scrolling. |
| Dashboard / daily summary analytics screenshots | `OpenDashboardCommand` opens `WorkspaceLinks.DashboardUrl` | Do not build an in-tray dashboard in this pass; these screenshots remain external dashboard references. |

## Design Direction

**Subject and audience:** A desktop workday companion for employees who need fast clock-in, break, and clock-out actions without feeling watched or blocked. The UI should feel calm, reliable, and operational.

**Palette:** Keep the existing OneXso cyan -> blue -> purple brand gradient, balanced by work-state colors:

| Token | Color |
|---|---|
| WorkPulse cyan | `#22C7F0` |
| OneXso blue | `#175CFF` |
| Brand purple | `#6D28D9` |
| Surface wash | `#F4F7FF` |
| Success green | `#22C55E` |
| Break orange | `#FF8A14` |

**Type:** Keep the Windows-native MAUI stack: Segoe UI for text and Segoe MDL2 Assets for icons. Page titles should sit around 22-24px; dense labels should sit around 10-13px. Avoid viewport-scaled text and negative letter spacing.

**Layout concept:** A fixed desktop command surface. Every page uses a compact header, one star-sized content area, and a slim footer. The main content either uses a two-pane hero/action layout or a centered utility layout. The primary action must be visible at 960x700 and still fit at the minimum window.

```text
+--------------------------------------------------------------+
| compact brand header                                          |
+--------------------------+-----------------------------------+
| illustration / context    | primary task, fields, actions     |
| fixed or star-fit visual  | compact cards, no scrollbars      |
+--------------------------+-----------------------------------+
| version / connection footer                                   |
+--------------------------------------------------------------+
```

**Signature element:** The app should be remembered by the "workday state surface": the same hero illustration space and state-colored action area morphs between Ready, Working, On Break, and Completed. This is specific to a tray workday agent and avoids making every screen feel like a generic card stack.

## Single-Screen Contract

1. Default desktop window: 1024x720.
2. Minimum desktop window: 960x700.
3. No current tray route may use page-level `ScrollView`.
4. Finite lists in tray pages must not use scrollable `CollectionView`; use fixed `Grid`, `FlexLayout` with `BindableLayout`, or a capped preview.
5. Header visual height budget: 44px content plus at most 6px bottom padding.
6. Footer visual height budget: 28px content including top padding.
7. Primary action height budget: 48px standard, 64px only for the main Clock In hero action.
8. Face capture frame outer size budget: 236px.
9. Repeated card padding budget: 10-12px vertical, 12-14px horizontal.
10. Long labels must use `MaxLines` and `LineBreakMode="TailTruncation"` or `WordWrap` only where the row has enough height.
11. Decorative glow ellipses must be input-transparent and must not define the page's needed size.
12. Existing raster hero assets in `ONEVO.Agent.TrayApp/Resources/Images` stay as the primary visuals.

## Page-Specific Requirements

**Connect:** Keep the activation form, paste button, help card, secure connection card, and footer in view. The two lower info cards can use shorter copy and dense padding.

**Prepare:** The loading state should show title, subtitle, 88px progress ring, three setup steps, user information, two setup shortcuts, and Continue within one screen. The ready state should reuse the same route and show the ready checklist without adding another route.

**Location:** The live location detection card stays at the top. Approved locations are finite cards, arranged in one or two columns with no vertical list scroll. `SaveAndContinueCommand` remains gated by `SelectedLocation`.

**Photo / biometric:** The circular camera remains the visual anchor. Reduce the nested ring sizes and keep the trust note and Continue action below it. The WebView route must fill the available star row and leave room for header/footer/status.

**Review:** The details card remains dense with rows for full name, email, employee ID, location, and face verification. If device details are required, add one compact row rather than creating a new page in this pass.

**Policy:** Six permission rows, policy note, and Allow & Continue must fit together. Keep locked toggles visually clear. Avoid adding explanatory paragraphs beyond the existing subtitle and row descriptions.

**Clock In:** The Clock In action should remain the dominant control but shrink from 92px to a 64px hero action. Status cards and footer stay visible without pushing content down.

**Active / break:** Working and On Break share the same layout. On Break changes color, text, hero asset, and available actions. Break confirmation remains an overlay on the same route.

**End:** Summary metrics, top-app preview, Download Summary, and Close App must fit without a horizontal or vertical scrollbar. Cap visible top apps to four in the tray preview.

## Out Of Scope For This Pass

1. Building the external web dashboard screens inside the tray app.
2. Changing backend, IPC, collection, or lifecycle behavior unless visual verification finds a UI action broken.
3. Adding new routes for Confirm Device, Privacy Transparency, Readiness Check, or Dashboard. Their content should be folded into existing routes only when it serves the single-screen tray flow.
4. Replacing the existing brand palette or raster artwork.

## Verification

1. Unit tests protect viewmodel changes and static layout contracts.
2. `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj` must pass.
3. `dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0` must pass.
4. Manual Windows visual pass must check every current route at 1024x720 and 960x700.
5. Manual pass must confirm no page-level scrollbar, no clipped primary action, no overlapping labels, and no hidden footer.
