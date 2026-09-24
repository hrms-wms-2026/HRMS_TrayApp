# Daily Summary Layout Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adopt three layout ideas from a user-supplied reference mockup into `DailySummaryPage.xaml` — decorative date-pill chevrons, a real-data sparkline in the "Most Productive Period" card, and a screenshot-thumbnail accent bar — while keeping every existing ONEVO color, font, and card style unchanged.

**Architecture:** `SessionDayMetrics` (the in-memory day-metrics singleton already fed by collectors) gains an hourly focus-time bucket alongside its existing per-app totals; `GetHourlyFocusFractions()` exposes those buckets normalized 0–1 for charting. `DailySummaryViewModel` reads that into a new `FocusSparklinePoints` property. A new `SparklineChart` `GraphicsView` control (same pattern as the existing `FractionBar`) renders it. The date-pill chevrons and screenshot accent bar are pure XAML additions with no new bindings.

**Tech Stack:** .NET MAUI (net10.0-windows10.0.19041.0), CommunityToolkit.Mvvm, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-24-daily-summary-layout-refresh-design.md`

---

### Task 1: Hourly focus buckets in `SessionDayMetrics`

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Services/ISessionDayMetrics.cs`
- Modify: `ONEVO.Agent.TrayApp/Services/SessionDayMetrics.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeSessionDayMetrics.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/Services/SessionDayMetricsTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these three tests to the end of the `SessionDayMetricsTests` class in `tests/ONEVO.Agent.TrayApp.Tests/Services/SessionDayMetricsTests.cs`, just before the class's closing `}`:

```csharp
    [Fact]
    public void GetHourlyFocusFractions_EmptyWhenNoSamples()
    {
        var metrics = new SessionDayMetrics();
        Assert.Empty(metrics.GetHourlyFocusFractions());
    }

    [Fact]
    public void AddAppUsageSample_BucketsByHour_NormalizedAgainstBusiestHour()
    {
        var metrics = new SessionDayMetrics();
        var nineAm = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.FromHours(5.5));
        var tenAm = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(5.5));

        metrics.AddAppUsageSample("chrome.exe", TimeSpan.FromMinutes(30), nineAm);
        metrics.AddAppUsageSample("code.exe", TimeSpan.FromMinutes(15), nineAm);
        metrics.AddAppUsageSample("teams.exe", TimeSpan.FromMinutes(20), tenAm);

        var fractions = metrics.GetHourlyFocusFractions();

        Assert.Equal(2, fractions.Count);
        Assert.Equal(1.0, fractions[0]);
        Assert.Equal(20.0 / 45.0, fractions[1], precision: 6);
    }

    [Fact]
    public void ResetDay_ClearsHourlyFocusBuckets()
    {
        var metrics = new SessionDayMetrics();
        metrics.AddAppUsageSample("chrome.exe", TimeSpan.FromMinutes(10), DateTimeOffset.UtcNow);
        metrics.ResetDay();
        Assert.Empty(metrics.GetHourlyFocusFractions());
    }
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "SessionDayMetricsTests"`
Expected: Build error — `'SessionDayMetrics' does not contain a definition for 'GetHourlyFocusFractions'` and no 3-argument overload of `AddAppUsageSample` exists.

- [ ] **Step 3: Add the interface member**

In `ONEVO.Agent.TrayApp/Services/ISessionDayMetrics.cs`, add this line to the `ISessionDayMetrics` interface, right after the existing `GetTopApps` method:

```csharp
    IReadOnlyList<double> GetHourlyFocusFractions();
```

- [ ] **Step 4: Implement hourly bucketing in `SessionDayMetrics`**

In `ONEVO.Agent.TrayApp/Services/SessionDayMetrics.cs`, add a new field next to the existing `_appSeconds` field:

```csharp
    private readonly ConcurrentDictionary<int, TimeSpan> _hourlyFocus = new();
```

Replace the existing `AddAppUsageSample` method:

```csharp
    public void AddAppUsageSample(string processName, TimeSpan sampleWindow)
    {
        if (string.IsNullOrWhiteSpace(processName) || sampleWindow <= TimeSpan.Zero)
            return;

        var key = processName.Trim();
        _appSeconds.AddOrUpdate(key, sampleWindow, (_, prev) => prev + sampleWindow);
    }
```

with:

```csharp
    public void AddAppUsageSample(string processName, TimeSpan sampleWindow) =>
        AddAppUsageSample(processName, sampleWindow, DateTimeOffset.Now);

    /// <summary>Testing seam: lets tests control which hour a sample lands in. Not part of
    /// <see cref="ISessionDayMetrics"/> — production callers always go through the 2-arg overload,
    /// which stamps the current local time.</summary>
    internal void AddAppUsageSample(string processName, TimeSpan sampleWindow, DateTimeOffset at)
    {
        if (string.IsNullOrWhiteSpace(processName) || sampleWindow <= TimeSpan.Zero)
            return;

        var key = processName.Trim();
        _appSeconds.AddOrUpdate(key, sampleWindow, (_, prev) => prev + sampleWindow);
        _hourlyFocus.AddOrUpdate(at.Hour, sampleWindow, (_, prev) => prev + sampleWindow);
    }
```

Add the read method after `GetTopApps`:

```csharp
    public IReadOnlyList<double> GetHourlyFocusFractions()
    {
        if (_hourlyFocus.IsEmpty)
            return [];

        var max = _hourlyFocus.Values.Max(t => t.TotalSeconds);
        if (max <= 0)
            return [];

        return _hourlyFocus
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value.TotalSeconds / max)
            .ToList();
    }
```

In `ResetDay()`, add `_hourlyFocus.Clear();` on its own line, next to the existing `_appSeconds.Clear();`.

- [ ] **Step 5: Implement the interface member on the test fake**

In `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeSessionDayMetrics.cs`, add this method next to `GetTopApps`:

```csharp
    public IReadOnlyList<double> GetHourlyFocusFractions() => [];
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "SessionDayMetricsTests"`
Expected: PASS (all tests in the file, including the 3 new ones).

- [ ] **Step 7: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/ISessionDayMetrics.cs ONEVO.Agent.TrayApp/Services/SessionDayMetrics.cs tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeSessionDayMetrics.cs tests/ONEVO.Agent.TrayApp.Tests/Services/SessionDayMetricsTests.cs
git commit -m "feat(tray): track hourly focus buckets in SessionDayMetrics"
```

---

### Task 2: `FocusSparklinePoints` in `DailySummaryViewModel`

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/DailySummaryViewModel.cs`
- Test: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/DailySummaryViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test to `DailySummaryViewModelTests`, after `OnAppearing_MapsSkippedAttemptAsRedNote`:

```csharp
    [Fact]
    public void OnAppearing_PopulatesFocusSparklinePointsFromHourlyBuckets()
    {
        var metrics = new ONEVO.Agent.TrayApp.Services.SessionDayMetrics();
        var nineAm = new DateTimeOffset(2026, 9, 24, 9, 0, 0, TimeSpan.FromHours(5.5));
        var tenAm = new DateTimeOffset(2026, 9, 24, 10, 0, 0, TimeSpan.FromHours(5.5));
        metrics.AddAppUsageSample("chrome.exe", TimeSpan.FromMinutes(45), nineAm);
        metrics.AddAppUsageSample("teams.exe", TimeSpan.FromMinutes(20), tenAm);

        var vm = new DailySummaryViewModel(new FakeNamedPipeClient(), metrics);
        vm.OnAppearing();

        Assert.Equal(2, vm.FocusSparklinePoints.Count);
        Assert.Equal(1f, vm.FocusSparklinePoints[0]);
        Assert.Equal(20f / 45f, vm.FocusSparklinePoints[1], precision: 5);
    }
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "OnAppearing_PopulatesFocusSparklinePointsFromHourlyBuckets"`
Expected: Build error — `'DailySummaryViewModel' does not contain a definition for 'FocusSparklinePoints'`.

- [ ] **Step 3: Add the observable property**

In `ONEVO.Agent.TrayApp/ViewModels/DailySummaryViewModel.cs`, add this line to the `[ObservableProperty]` block, right after `_appDonutSegments`:

```csharp
    [ObservableProperty] private IReadOnlyList<float> _focusSparklinePoints = [];
```

- [ ] **Step 4: Populate it in `OnAppearing` and `LoadFromSnapshot`**

In `OnAppearing()`, add this line right after the `LoadScreenshots();` call:

```csharp
        FocusSparklinePoints = _dayMetrics.GetHourlyFocusFractions().Select(f => (float)f).ToList();
```

In `LoadFromSnapshot(SessionSnapshot session)`, add the same line right after its `LoadScreenshots();` call:

```csharp
        FocusSparklinePoints = _dayMetrics.GetHourlyFocusFractions().Select(f => (float)f).ToList();
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "DailySummaryViewModelTests"`
Expected: PASS (all tests in the file, including the new one).

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/DailySummaryViewModel.cs tests/ONEVO.Agent.TrayApp.Tests/ViewModels/DailySummaryViewModelTests.cs
git commit -m "feat(tray): expose FocusSparklinePoints on DailySummaryViewModel"
```

---

### Task 3: `SparklineChart` control

**Files:**
- Create: `ONEVO.Agent.TrayApp/Controls/SparklineChart.cs`

- [ ] **Step 1: Create the control**

Create `ONEVO.Agent.TrayApp/Controls/SparklineChart.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.Controls;

/// <summary>Simple polyline sparkline (with a soft area fill) over 0–1 normalized points.
/// No axes, labels, or interactivity — matches <see cref="FractionBar"/>'s minimal drawable pattern.</summary>
public sealed class SparklineChart : GraphicsView
{
    public static readonly BindableProperty PointsProperty =
        BindableProperty.Create(
            nameof(Points),
            typeof(IReadOnlyList<float>),
            typeof(SparklineChart),
            Array.Empty<float>(),
            propertyChanged: static (bindable, _, _) => ((SparklineChart)bindable).Invalidate());

    public static readonly BindableProperty LineColorProperty =
        BindableProperty.Create(
            nameof(LineColor),
            typeof(Color),
            typeof(SparklineChart),
            Color.FromArgb("#22C7F0"),
            propertyChanged: static (bindable, _, _) => ((SparklineChart)bindable).Invalidate());

    public IReadOnlyList<float> Points
    {
        get => (IReadOnlyList<float>)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Color LineColor
    {
        get => (Color)GetValue(LineColorProperty);
        set => SetValue(LineColorProperty, value);
    }

    public SparklineChart()
    {
        Drawable = new Painter(this);
        InputTransparent = true;
    }

    private sealed class Painter(SparklineChart owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var points = owner.Points;
            if (points is null || points.Count < 2 || dirtyRect.Width < 2 || dirtyRect.Height < 2)
                return;

            var stepX = dirtyRect.Width / (points.Count - 1);
            var coords = new PointF[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                var fraction = Math.Clamp(points[i], 0f, 1f);
                var x = dirtyRect.X + stepX * i;
                var y = dirtyRect.Y + dirtyRect.Height * (1 - fraction);
                coords[i] = new PointF(x, y);
            }

            var fillPath = new PathF();
            fillPath.MoveTo(coords[0].X, dirtyRect.Bottom);
            foreach (var point in coords)
                fillPath.LineTo(point.X, point.Y);
            fillPath.LineTo(coords[^1].X, dirtyRect.Bottom);
            fillPath.Close();

            canvas.FillColor = owner.LineColor.WithAlpha(0.15f);
            canvas.FillPath(fillPath);

            var linePath = new PathF();
            linePath.MoveTo(coords[0].X, coords[0].Y);
            for (var i = 1; i < coords.Length; i++)
                linePath.LineTo(coords[i].X, coords[i].Y);

            canvas.StrokeColor = owner.LineColor;
            canvas.StrokeSize = 2f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.DrawPath(linePath);
        }
    }
}
```

No unit tests for this file — pure `IDrawable` rendering code, matching the existing `FractionBar` control's precedent of being untested UI drawing code (per the design spec's Testing section).

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj -c Debug -f net10.0-windows10.0.19041.0`
Expected: Build succeeds with 0 errors. (If it fails with a file-lock error like `MSB3027`/`MSB3021` on `ONEVO.Agent.TrayApp.exe` or one of its DLLs, the tray app is currently running from this same build output — stop the running `ONEVO.Agent.TrayApp` process first, then re-run the build.)

- [ ] **Step 3: Commit**

```bash
git add ONEVO.Agent.TrayApp/Controls/SparklineChart.cs
git commit -m "feat(tray): add SparklineChart control"
```

---

### Task 4: XAML — date pill chevrons, sparkline placement, screenshot accent bar

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Views/DailySummaryPage.xaml`

- [ ] **Step 1: Add decorative chevrons to the date pill**

In `ONEVO.Agent.TrayApp/Views/DailySummaryPage.xaml`, find this block (inside the `Grid Grid.Row="0"`, the date `Border`):

```xml
      <Border Grid.Column="1" Style="{StaticResource TrayCompactGlassCard}" Padding="12,8" VerticalOptions="Center">
        <HorizontalStackLayout Spacing="6">
          <Image Source="icon3d_calendar.png" WidthRequest="16" HeightRequest="16" Aspect="AspectFit" VerticalOptions="Center" />
          <Label Text="{Binding DateDisplay}" FontSize="12" FontAttributes="Bold" TextColor="{StaticResource TextPrimary}" VerticalOptions="Center" />
          <Label Text="·" FontSize="12" TextColor="{StaticResource TextMuted}" VerticalOptions="Center" />
          <Label Text="{Binding WeekdayDisplay}" FontSize="12" TextColor="{StaticResource TextSecondary}" VerticalOptions="Center" />
        </HorizontalStackLayout>
      </Border>
```

Replace it with:

```xml
      <Border Grid.Column="1" Style="{StaticResource TrayCompactGlassCard}" Padding="12,8" VerticalOptions="Center">
        <HorizontalStackLayout Spacing="6">
          <Label Text="{StaticResource IconChevron}" FontFamily="Segoe MDL2 Assets" FontSize="11"
                 TextColor="{StaticResource TextMuted}" VerticalOptions="Center" Rotation="180" />
          <Image Source="icon3d_calendar.png" WidthRequest="16" HeightRequest="16" Aspect="AspectFit" VerticalOptions="Center" />
          <Label Text="{Binding DateDisplay}" FontSize="12" FontAttributes="Bold" TextColor="{StaticResource TextPrimary}" VerticalOptions="Center" />
          <Label Text="·" FontSize="12" TextColor="{StaticResource TextMuted}" VerticalOptions="Center" />
          <Label Text="{Binding WeekdayDisplay}" FontSize="12" TextColor="{StaticResource TextSecondary}" VerticalOptions="Center" />
          <Label Text="{StaticResource IconChevron}" FontFamily="Segoe MDL2 Assets" FontSize="11"
                 TextColor="{StaticResource TextMuted}" VerticalOptions="Center" />
        </HorizontalStackLayout>
      </Border>
```

This reuses the `IconChevron` glyph (`&#xE76C;`) already defined in `ONEVO.Agent.TrayApp/Resources/Styles/Icons.xaml` — rotated 180° for the left arrow. No `Command`, no `GestureRecognizer`: purely visual, per the design spec.

- [ ] **Step 2: Add the sparkline to "Most Productive Period"**

In the same file, add the `controls` XML namespace is already declared at the top (`xmlns:controls="clr-namespace:ONEVO.Agent.TrayApp.Controls"` — confirm it's present; it already is, used by `FractionBar`/`MultiSegmentDonutChart`).

Find this block (the highlight card inside "Work Pattern Insights"):

```xml
          <Border Grid.Row="1" BackgroundColor="{StaticResource StatusGreenSoft}" StrokeThickness="0"
                  StrokeShape="RoundRectangle 12" Padding="10,8">
            <Grid ColumnDefinitions="Auto,*,Auto" ColumnSpacing="8">
              <Image Source="icon3d_clock.png" WidthRequest="28" HeightRequest="28" Aspect="AspectFit" VerticalOptions="Center" />
              <VerticalStackLayout Grid.Column="1" Spacing="0" VerticalOptions="Center">
                <Label Text="Most Productive Period" FontSize="12" TextColor="{StaticResource TextSecondary}" />
                <Label Text="{Binding HighlightWindow}" FontSize="15" FontAttributes="Bold" TextColor="{StaticResource TextPrimary}"
                       LineBreakMode="TailTruncation" MaxLines="1"
                       IsVisible="{Binding HighlightWindow, Converter={StaticResource IsNotNullConverter}}" />
              </VerticalStackLayout>
              <VerticalStackLayout Grid.Column="2" Spacing="0" VerticalOptions="Center">
                <Label Text="{Binding FocusCompactDisplay}" FontSize="16" FontAttributes="Bold"
                       TextColor="{StaticResource StatusGreen}" HorizontalTextAlignment="End" />
                <Label Text="focused" FontSize="11" TextColor="{StaticResource StatusGreen}" HorizontalTextAlignment="End" />
              </VerticalStackLayout>
            </Grid>
          </Border>
```

Replace it with (new `80`-wide column added, sparkline in it, time badge shifted to column 3):

```xml
          <Border Grid.Row="1" BackgroundColor="{StaticResource StatusGreenSoft}" StrokeThickness="0"
                  StrokeShape="RoundRectangle 12" Padding="10,8">
            <Grid ColumnDefinitions="Auto,*,80,Auto" ColumnSpacing="8">
              <Image Source="icon3d_clock.png" WidthRequest="28" HeightRequest="28" Aspect="AspectFit" VerticalOptions="Center" />
              <VerticalStackLayout Grid.Column="1" Spacing="0" VerticalOptions="Center">
                <Label Text="Most Productive Period" FontSize="12" TextColor="{StaticResource TextSecondary}" />
                <Label Text="{Binding HighlightWindow}" FontSize="15" FontAttributes="Bold" TextColor="{StaticResource TextPrimary}"
                       LineBreakMode="TailTruncation" MaxLines="1"
                       IsVisible="{Binding HighlightWindow, Converter={StaticResource IsNotNullConverter}}" />
              </VerticalStackLayout>
              <controls:SparklineChart Grid.Column="2" Points="{Binding FocusSparklinePoints}"
                                       LineColor="{StaticResource StatusGreen}"
                                       WidthRequest="80" HeightRequest="32" VerticalOptions="Center" />
              <VerticalStackLayout Grid.Column="3" Spacing="0" VerticalOptions="Center">
                <Label Text="{Binding FocusCompactDisplay}" FontSize="16" FontAttributes="Bold"
                       TextColor="{StaticResource StatusGreen}" HorizontalTextAlignment="End" />
                <Label Text="focused" FontSize="11" TextColor="{StaticResource StatusGreen}" HorizontalTextAlignment="End" />
              </VerticalStackLayout>
            </Grid>
          </Border>
```

- [ ] **Step 3: Add the accent bar to screenshot thumbnails**

Find this block (the screenshot `DataTemplate`'s image `Border`):

```xml
                  <Border StrokeShape="RoundRectangle 8" HeightRequest="68" Stroke="{StaticResource Separator}" StrokeThickness="1">
                    <Border.Triggers>
                      <DataTrigger TargetType="Border" Binding="{Binding IsSkipped}" Value="True">
                        <Setter Property="BackgroundColor" Value="{StaticResource StatusRedSoft}" />
                        <Setter Property="Stroke" Value="{StaticResource StatusRed}" />
                      </DataTrigger>
                    </Border.Triggers>
                    <Grid>
                      <Image Source="{Binding Preview}"
                             Aspect="AspectFill"
                             HeightRequest="68"
                             WidthRequest="132"
                             IsVisible="{Binding IsSkipped, Converter={StaticResource InvertBoolConverter}}" />
                      <VerticalStackLayout IsVisible="{Binding IsSkipped}" Padding="8" Spacing="0" VerticalOptions="Center">
                        <Label Text="Screenshot skipped" FontSize="11" FontAttributes="Bold"
                               TextColor="{StaticResource StatusRed}" HorizontalTextAlignment="Center" LineBreakMode="WordWrap" />
                      </VerticalStackLayout>
                    </Grid>
                  </Border>
```

Replace it with (one new `BoxView`, added last so it draws on top, clipped to the `Border`'s rounded corners like the existing `Image` already is):

```xml
                  <Border StrokeShape="RoundRectangle 8" HeightRequest="68" Stroke="{StaticResource Separator}" StrokeThickness="1">
                    <Border.Triggers>
                      <DataTrigger TargetType="Border" Binding="{Binding IsSkipped}" Value="True">
                        <Setter Property="BackgroundColor" Value="{StaticResource StatusRedSoft}" />
                        <Setter Property="Stroke" Value="{StaticResource StatusRed}" />
                      </DataTrigger>
                    </Border.Triggers>
                    <Grid>
                      <Image Source="{Binding Preview}"
                             Aspect="AspectFill"
                             HeightRequest="68"
                             WidthRequest="132"
                             IsVisible="{Binding IsSkipped, Converter={StaticResource InvertBoolConverter}}" />
                      <VerticalStackLayout IsVisible="{Binding IsSkipped}" Padding="8" Spacing="0" VerticalOptions="Center">
                        <Label Text="Screenshot skipped" FontSize="11" FontAttributes="Bold"
                               TextColor="{StaticResource StatusRed}" HorizontalTextAlignment="Center" LineBreakMode="WordWrap" />
                      </VerticalStackLayout>
                      <BoxView IsVisible="{Binding IsSkipped, Converter={StaticResource InvertBoolConverter}}"
                               BackgroundColor="{StaticResource PrimaryGradientStart}"
                               HeightRequest="4" VerticalOptions="Start" HorizontalOptions="Fill" />
                    </Grid>
                  </Border>
```

- [ ] **Step 4: Verify the layout contract test still passes**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj --filter "DailySummaryPage_FitsOneScreen"`
Expected: PASS. This test does a source-text scan of `DailySummaryPage.xaml` for specific substrings (e.g. `ColumnDefinitions="*,*,*,*"`, `ColumnDefinitions="*,1.15*"`, `FractionBar`, `MultiSegmentDonutChart`) — none of those were touched by the edits above, only the inner `Grid` inside the highlight card and the screenshot template changed.

- [ ] **Step 5: Build to verify the XAML compiles**

Run: `dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj -c Debug -f net10.0-windows10.0.19041.0`
Expected: Build succeeds with 0 errors. (Same file-lock caveat as Task 3, Step 2 — stop the running tray app first if needed.)

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.TrayApp/Views/DailySummaryPage.xaml
git commit -m "feat(tray): add date chevrons, sparkline, and screenshot accent bar to Daily Summary"
```

---

### Task 5: Full test suite and manual verification

**Files:** none (verification only)

- [ ] **Step 1: Run the full TrayApp test suite**

Run: `dotnet test tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj`
Expected: PASS, 0 failures.

- [ ] **Step 2: Stop the currently running tray app (if running)**

Check: `Get-Process -Name ONEVO.Agent.TrayApp -ErrorAction SilentlyContinue`
If a process is returned, stop it: `Stop-Process -Name ONEVO.Agent.TrayApp -Force`

- [ ] **Step 3: Rebuild and relaunch**

Run: `dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj -c Debug -f net10.0-windows10.0.19041.0`
Then launch the built exe directly: `ONEVO.Agent.TrayApp/bin/Debug/net10.0-windows10.0.19041.0/win-x64/ONEVO.Agent.TrayApp.exe`

- [ ] **Step 4: Manually confirm the three visual pieces**

Open the tray app's Daily Summary screen and confirm:
1. The date pill shows a `‹` and `›` on either side of the date (not clickable — that's expected, they're decorative).
2. The "Most Productive Period" card shows a small line-with-fill sparkline. If today has activity in more than one hour bucket, the line should have more than one segment; on a fresh day with a single active hour, the sparkline area renders empty (per Task 3's `points.Count < 2` guard) rather than a flat line.
3. Non-skipped screenshot thumbnails in the Screenshots strip show a thin colored bar along their top edge; skipped ones keep their existing red-note styling with no added bar.

No code changes in this task — if something looks wrong, go back to the relevant task above and fix it there, then re-run this task's steps.
