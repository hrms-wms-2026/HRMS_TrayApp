# Daily Summary Layout Refresh — Design Spec

**Goal:** Bring `DailySummaryPage.xaml` closer to a reference mockup the user supplied, adopting three layout ideas from it while keeping ONEVO's existing brand colors, typography, and card styles unchanged.

**Source context:** User supplied a reference screenshot of a "Daily Summary" dashboard (light blue-lavender background, 4 KPI cards, insights + app-usage split, screenshot strip, footer actions). The current `DailySummaryPage.xaml` is already structurally very close to it (same sections, same card layout). Scope is limited to the three concrete gaps identified against the reference — this is not a palette or component-system overhaul.

**Out of scope (explicitly deferred by the user):**
- Functional date navigation (browsing a previous day's summary). The `‹ ›` arrows are visual only; this page continues to show only the current/last session, sourced from `ISessionDayMetrics`/`_pipe.LastKnownStatus` as it does today.
- Any new color palette, font, or spacing system — all three pieces below reuse existing ONEVO style resources (`SummaryCard`, `KpiLabel`, brand colors from `Styles/Colors.xaml`, etc.).
- Per-app-colored screenshot accents. The tray does not record which app was in the foreground for a given activity-check screenshot, so accents use a single brand color rather than fabricated per-app colors.

## 1. Date pill chevrons (visual only)

`DailySummaryPage.xaml`'s header date `Border` (around `DateDisplay`/`WeekdayDisplay`) gains a `‹` and `›` glyph either side of the date text, inside the same `TrayCompactGlassCard` pill. No `Command`, no `GestureRecognizer`, no new ViewModel state — purely two more `Label`/`Image` glyphs in the existing `HorizontalStackLayout`, styled muted (`TextMuted`) so they read as decorative rather than an active control.

## 2. Real-data sparkline in "Most Productive Period"

**Data (Approach A — client-side hourly buckets, no backend/IPC change):**

- `ISessionDayMetrics` gains:
  - `void AddAppUsageSample(string processName, TimeSpan sampleWindow)` — unchanged signature, but the implementation additionally buckets `sampleWindow` into an hour-of-day bucket (`DateTimeOffset.Now.Hour`, local time) alongside the existing per-app total.
  - `IReadOnlyList<double> GetHourlyFocusFractions()` — returns one entry per hour that has any recorded activity today (chronological, from the first active hour to the last), each value normalized 0–1 against that day's busiest hour. Empty day → empty list.
- `SessionDayMetrics` stores this as a `ConcurrentDictionary<int, TimeSpan>` (hour → accumulated active time), reset in `ResetDay()` alongside the other per-day state.
- `DailySummaryViewModel` gets a new `[ObservableProperty] IReadOnlyList<float> _focusSparklinePoints`, computed in `OnAppearing()`/`ApplyDerived()` from `_dayMetrics.GetHourlyFocusFractions()`.

**Rendering — new `SparklineChart` control** (`Controls/SparklineChart.cs`), following the existing `FractionBar` pattern (a `GraphicsView` + `IDrawable` painter, `BindableProperty`s for `Points` and `LineColor`):
- Draws a simple polyline through the normalized points across the control's width.
- A soft fill (same line color at low opacity) beneath the line down to the baseline, matching the reference's subtle area fill.
- No axes, labels, gridlines, or interactivity — a pure sparkline.
- Fewer than 2 points (e.g. a day with only one active hour so far) → draw nothing (empty canvas), not a flat/degenerate line.

**Placement:** inside the existing "Most Productive Period" highlight `Border` in `DailySummaryPage.xaml`'s Row 2 left card, to the right of the existing text column — mirrors the reference's layout without changing that card's height or the page's row structure.

## 3. Screenshot accent bar

Each screenshot thumbnail in the horizontal `BindableLayout` strip (Row 3) gets a 4px accent strip along its top edge, inside the existing bordered tile:
- Allowed (`IsSkipped == false`) screenshots: brand primary color (`PrimaryGradientStart`, already used elsewhere on this page for the Active Time card).
- Skipped screenshots: keep the existing red treatment (`StatusRed`) — already visually an "accent," no change needed there.
- Implemented as a thin `BoxView`/`Border` pinned to the top of the existing thumbnail `Grid`, not a change to the image or skipped-note layout underneath.

## Testing

- `SparklineChart`: no unit tests (pure `IDrawable` rendering, matches `FractionBar`'s precedent of being untested UI drawing code).
- `SessionDayMetrics`: extend `tests/ONEVO.Agent.TrayApp.Tests/Services/SessionDayMetricsTests.cs` to cover `GetHourlyFocusFractions()` — buckets correctly by hour, normalizes against the busiest hour, empty when no samples, resets on `ResetDay()`.
- `FakeSessionDayMetrics` (`tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeSessionDayMetrics.cs`) must implement the new `GetHourlyFocusFractions()` interface member — this is the other `ISessionDayMetrics` implementer besides the real class.
- `DailySummaryViewModel`: extend existing view model tests to assert `FocusSparklinePoints` is populated from `GetHourlyFocusFractions()` on `OnAppearing()`.
- Manual: rebuild + relaunch the tray app, open Daily Summary, confirm the three visual pieces render without layout shift/clipping at the page's existing fixed window size.
