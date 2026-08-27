# Tray App Single-Screen UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every current OneXso WorkPulse tray app route fit cleanly in one desktop window with no page-level scrolling, while preserving the existing glass OneXso visual direction from the screenshot set.

**Architecture:** Add a small shared layout contract for window metrics, compact XAML resources, and static XAML guard tests. Then update each existing MAUI page in focused groups: shared chrome, finite-list screens, onboarding screens, camera/policy screens, and workday/session screens. Business lifecycle logic remains unchanged unless a UI verification step proves an action is broken.

**Tech Stack:** .NET 10, .NET MAUI Windows `net10.0-windows10.0.19041.0`, WinUI3, CommunityToolkit.Mvvm, xUnit, existing raster assets under `ONEVO.Agent.TrayApp/Resources/Images`.

**Spec:** `docs/superpowers/specs/2026-08-26-tray-app-single-screen-ui-design.md`

## Global Constraints

- Default desktop window is `1024x720`; minimum desktop window is `960x700`.
- No current tray route uses page-level `ScrollView`.
- Finite tray lists do not use scrollable `CollectionView`; use fixed `Grid`, `FlexLayout` with `BindableLayout`, or capped previews.
- Standard primary action height is `48`; the Clock In hero action can be `64`.
- Face capture outer frame is at most `236`.
- Header budget is `44` content height plus at most `6` bottom padding.
- Footer budget is `28` total visual height.
- Use existing `Colors.xaml` brand tokens and raster hero assets.
- Dashboard and daily-summary analytics screenshots remain external dashboard references in this pass.
- Do not change service, IPC, collector, or lifecycle behavior unless manual UI verification exposes a broken action.

---

## File Structure

| File | Responsibility |
|---|---|
| `tests/ONEVO.Agent.TrayApp.Tests/Views/TrayScreenLayoutContractTests.cs` | Static guard tests for the single-screen contract. |
| `ONEVO.Agent.TrayApp/Controls/TrayLayoutMetrics.cs` | Window and layout constants shared by app setup, responsive helper, and tests. |
| `ONEVO.Agent.TrayApp/App.xaml.cs` | Uses shared window metrics for default and minimum size. |
| `ONEVO.Agent.TrayApp/Controls/ResponsiveTwoPane.cs` | Uses the shared wide breakpoint. |
| `ONEVO.Agent.TrayApp/Resources/Styles/Styles.xaml` | Adds compact card/action resources and shared dimension values. |
| `ONEVO.Agent.TrayApp/Controls/AppHeaderBar.xaml` | Compact header chrome used by most pages. |
| `ONEVO.Agent.TrayApp/Controls/FooterStatusBar.xaml` | Compact footer chrome with truncation protection. |
| `ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs` | Adds explicit location selection command for non-scrollable item layouts. |
| `ONEVO.Agent.TrayApp/ViewModels/EndSessionViewModel.cs` | Caps tray top-app preview to four visible items. |
| `ONEVO.Agent.TrayApp/Views/*.xaml` | Page-by-page fit updates for current routes. |

---

### Task 1: Add Static Single-Screen Contract Tests

**Files:**
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Views/TrayScreenLayoutContractTests.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ONEVO.Agent.TrayApp.Tests.csproj` only if the `Views` folder is not auto-included by SDK defaults.

**Interfaces:**
- Consumes: Current source files under `ONEVO.Agent.TrayApp/Views`, `Controls`, and `Resources/Styles`.
- Produces: Tests that fail until the shared metrics and XAML changes land.

- [ ] **Step 1: Create the failing test file**

```csharp
using ONEVO.Agent.TrayApp.Controls;

namespace ONEVO.Agent.TrayApp.Tests.Views;

public sealed class TrayScreenLayoutContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    public static IEnumerable<object[]> PrimaryScreenXamlFiles()
    {
        yield return ["ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/PrepareWorkspacePage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/BiometricEnrollmentPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/ReviewSetupPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/ClockInPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml"];
        yield return ["ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml"];
    }

    [Fact]
    public void WindowMetrics_DefineSingleScreenDesktopCanvas()
    {
        Assert.Equal(1024, TrayLayoutMetrics.DefaultWindowWidth);
        Assert.Equal(720, TrayLayoutMetrics.DefaultWindowHeight);
        Assert.Equal(960, TrayLayoutMetrics.MinimumWindowWidth);
        Assert.Equal(700, TrayLayoutMetrics.MinimumWindowHeight);
        Assert.Equal(900, TrayLayoutMetrics.WideBreakpoint);
    }

    [Theory]
    [MemberData(nameof(PrimaryScreenXamlFiles))]
    public void PrimaryScreens_DoNotUsePageLevelScrollView(string relativePath)
    {
        var xaml = ReadSource(relativePath);
        Assert.DoesNotContain("<ScrollView", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml")]
    [InlineData("ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml")]
    public void FiniteTrayLists_DoNotUseCollectionView(string relativePath)
    {
        var xaml = ReadSource(relativePath);
        Assert.DoesNotContain("<CollectionView", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClockInHeroAction_UsesCompactHeightResource()
    {
        var xaml = ReadSource("ONEVO.Agent.TrayApp/Views/ClockInPage.xaml");
        Assert.DoesNotContain("HeightRequest=\"92\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TrayHeroActionHeight", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void PhotoCaptureFrame_UsesCompactSizeResource()
    {
        var xaml = ReadSource("ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml");
        Assert.DoesNotContain("WidthRequest=\"260\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("TrayFaceFrameOuterSize", xaml, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ONEVO.Agent.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ONEVO.Agent.slnx above the test output directory.");
    }
}
```

- [ ] **Step 2: Run tests and confirm the red state**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter TrayScreenLayoutContractTests -v normal
```

Expected result: fail because `TrayLayoutMetrics` does not exist yet. After Task 2 creates it, the same test group should continue failing on current XAML values until later tasks remove the oversized action/frame and scrollable finite lists.

- [ ] **Step 3: Commit the red tests**

```powershell
git add tests/ONEVO.Agent.TrayApp.Tests/Views/TrayScreenLayoutContractTests.cs
git commit -m "test(ui): add tray single-screen layout contract"
```

---

### Task 2: Add Shared Metrics, Window Size, And Compact Resources

**Files:**
- Create: `ONEVO.Agent.TrayApp/Controls/TrayLayoutMetrics.cs`
- Modify: `ONEVO.Agent.TrayApp/App.xaml.cs`
- Modify: `ONEVO.Agent.TrayApp/Controls/ResponsiveTwoPane.cs`
- Modify: `ONEVO.Agent.TrayApp/Resources/Styles/Styles.xaml`

**Interfaces:**
- Consumes: Task 1 tests.
- Produces: `TrayLayoutMetrics` constants and XAML resources used by page updates.

- [ ] **Step 1: Create `TrayLayoutMetrics.cs`**

```csharp
namespace ONEVO.Agent.TrayApp.Controls;

public static class TrayLayoutMetrics
{
    public const double DefaultWindowWidth = 1024;
    public const double DefaultWindowHeight = 720;
    public const double MinimumWindowWidth = 960;
    public const double MinimumWindowHeight = 700;
    public const double WideBreakpoint = 900;
}
```

- [ ] **Step 2: Update `App.xaml.cs` to use the metrics**

Add this using near the top:

```csharp
using ONEVO.Agent.TrayApp.Controls;
```

Replace the current window values:

```csharp
Width         = 960,
Height        = 700,
MinimumWidth  = 900,
MinimumHeight = 640
```

with:

```csharp
Width         = TrayLayoutMetrics.DefaultWindowWidth,
Height        = TrayLayoutMetrics.DefaultWindowHeight,
MinimumWidth  = TrayLayoutMetrics.MinimumWindowWidth,
MinimumHeight = TrayLayoutMetrics.MinimumWindowHeight
```

- [ ] **Step 3: Update `ResponsiveTwoPane.cs`**

Replace:

```csharp
public const double WideBreakpoint = 860;
```

with:

```csharp
public const double WideBreakpoint = TrayLayoutMetrics.WideBreakpoint;
```

- [ ] **Step 4: Add compact layout resources to `Styles.xaml`**

Add these resources after the opening `ResourceDictionary` line:

```xml
    <x:Double x:Key="TrayActionHeight">48</x:Double>
    <x:Double x:Key="TrayHeroActionHeight">64</x:Double>
    <x:Double x:Key="TrayFaceFrameOuterSize">236</x:Double>
    <x:Double x:Key="TrayFaceFrameRingSize">220</x:Double>
    <x:Double x:Key="TrayFaceFrameInnerSize">204</x:Double>
    <Thickness x:Key="TrayPagePadding">20,10</Thickness>
    <Thickness x:Key="TrayDensePagePadding">20,8</Thickness>
    <Thickness x:Key="TrayCardPadding">12,10</Thickness>
    <Thickness x:Key="TrayDenseCardPadding">10,8</Thickness>
```

Add these styles after the existing `GlassCard` style:

```xml
    <Style x:Key="TrayCompactGlassCard" TargetType="Border" BasedOn="{StaticResource GlassCard}">
        <Setter Property="Padding" Value="{StaticResource TrayCardPadding}" />
        <Setter Property="StrokeShape" Value="RoundRectangle 14" />
    </Style>

    <Style x:Key="TrayDenseGlassCard" TargetType="Border" BasedOn="{StaticResource GlassCard}">
        <Setter Property="Padding" Value="{StaticResource TrayDenseCardPadding}" />
        <Setter Property="StrokeShape" Value="RoundRectangle 12" />
    </Style>

    <Style x:Key="TrayPrimaryActionBorder" TargetType="Border" BasedOn="{StaticResource GradientButtonBorder}">
        <Setter Property="HeightRequest" Value="{StaticResource TrayActionHeight}" />
        <Setter Property="StrokeShape" Value="RoundRectangle 24" />
    </Style>

    <Style x:Key="TrayPrimaryActionOverlay" TargetType="Button" BasedOn="{StaticResource GradientButtonOverlay}">
        <Setter Property="HeightRequest" Value="{StaticResource TrayActionHeight}" />
        <Setter Property="FontSize" Value="14" />
    </Style>
```

- [ ] **Step 5: Run the targeted tests**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter TrayScreenLayoutContractTests -v normal
```

Expected result: `WindowMetrics_DefineSingleScreenDesktopCanvas` passes. Failures remain for pages that still use oversized resources or `CollectionView`.

- [ ] **Step 6: Commit**

```powershell
git add ONEVO.Agent.TrayApp/Controls/TrayLayoutMetrics.cs
git add ONEVO.Agent.TrayApp/App.xaml.cs
git add ONEVO.Agent.TrayApp/Controls/ResponsiveTwoPane.cs
git add ONEVO.Agent.TrayApp/Resources/Styles/Styles.xaml
git commit -m "feat(ui): add tray single-screen layout metrics"
```

---

### Task 3: Compact Shared Header And Footer Chrome

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Controls/AppHeaderBar.xaml`
- Modify: `ONEVO.Agent.TrayApp/Controls/FooterStatusBar.xaml`

**Interfaces:**
- Consumes: `TrayPagePadding`, `TrayDensePagePadding`, and existing `FooterLabel`.
- Produces: Predictable header/footer height for all pages that use these controls.

- [ ] **Step 1: Update `AppHeaderBar.xaml`**

Change the root grid padding and logo sizes:

```xml
<Grid ColumnDefinitions="Auto,*,Auto" Padding="0,0,0,6">
```

```xml
<Border WidthRequest="34" HeightRequest="34"
        StrokeShape="RoundRectangle 10" StrokeThickness="0"
        BackgroundColor="Transparent">
  <Image Source="onexso_x_mark.png"
         Aspect="AspectFit"
         WidthRequest="34" HeightRequest="34"
         HorizontalOptions="Center" VerticalOptions="Center" />
</Border>
```

Protect the brand labels:

```xml
<Label Text="OneXso Workspace" FontSize="13" FontAttributes="Bold"
       TextColor="{StaticResource TextPrimary}"
       LineBreakMode="TailTruncation" MaxLines="1" />
<Label Text="{Binding Source={x:Reference Root}, Path=Subtitle}"
       FontSize="10" TextColor="{StaticResource TextSecondary}"
       LineBreakMode="TailTruncation" MaxLines="1"
       IsVisible="{Binding Source={x:Reference Root}, Path=ShowSubtitle}" />
```

- [ ] **Step 2: Update `FooterStatusBar.xaml`**

Change the root grid padding:

```xml
<Grid ColumnDefinitions="Auto,*,Auto" Padding="0,4,0,0">
```

Protect both footer labels:

```xml
<Label Text="{Binding Source={x:Reference Root}, Path=VersionText}"
       Style="{StaticResource FooterLabel}"
       LineBreakMode="TailTruncation" MaxLines="1" />
```

```xml
<Label Text="{Binding Source={x:Reference Root}, Path=ConnectionLabel}"
       FontSize="11"
       TextColor="{StaticResource TextSecondary}"
       VerticalOptions="Center"
       LineBreakMode="TailTruncation" MaxLines="1" />
```

- [ ] **Step 3: Build**

```powershell
dotnet build ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0
```

Expected result: build succeeds.

- [ ] **Step 4: Commit**

```powershell
git add ONEVO.Agent.TrayApp/Controls/AppHeaderBar.xaml
git add ONEVO.Agent.TrayApp/Controls/FooterStatusBar.xaml
git commit -m "feat(ui): compact tray header and footer chrome"
```

---

### Task 4: Remove Scrollable Finite Lists

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/EndSessionViewModel.cs`
- Modify: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/EndSessionViewModelTests.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml`

**Interfaces:**
- Consumes: Existing `WorkLocationOption`, `SelectedLocation`, `TopApps`, and `SessionDayMetrics`.
- Produces: `SelectLocationCommand` for fixed/flex location cards and a capped four-item top-app preview.

- [ ] **Step 1: Add a failing WorkLocation selection test**

Add to `WorkLocationViewModelTests.cs`:

```csharp
[Fact]
public void SelectLocationCommand_SelectsAndMarksRequestedLocation()
{
    var vm = new WorkLocationViewModel();
    var location = vm.ApprovedLocations.Last();

    vm.SelectLocationCommand.Execute(location);

    Assert.Same(location, vm.SelectedLocation);
    Assert.True(location.IsSelected);
    Assert.All(vm.ApprovedLocations.Where(l => !ReferenceEquals(l, location)), l => Assert.False(l.IsSelected));
}
```

- [ ] **Step 2: Run the WorkLocation red test**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter SelectLocationCommand_SelectsAndMarksRequestedLocation -v normal
```

Expected result: fail because `SelectLocationCommand` is not defined.

- [ ] **Step 3: Add `SelectLocationCommand`**

Add this method to `WorkLocationViewModel.cs` before `SaveAndContinueAsync`:

```csharp
[RelayCommand]
private void SelectLocation(WorkLocationOption? option)
{
    if (option is null)
        return;

    SelectedLocation = option;
}
```

- [ ] **Step 4: Replace `WorkLocationPage.xaml` `CollectionView` with a non-scroll flex layout**

Replace the `CollectionView Grid.Row="3"` block with:

```xml
<FlexLayout Grid.Row="3"
            BindableLayout.ItemsSource="{Binding ApprovedLocations}"
            Direction="Row"
            Wrap="Wrap"
            AlignContent="Start"
            JustifyContent="SpaceBetween"
            Margin="0,0,0,10">
  <BindableLayout.ItemTemplate>
    <DataTemplate x:DataType="vm:WorkLocationOption">
      <Border Style="{StaticResource TrayCompactGlassCard}"
              WidthRequest="250"
              Margin="0,0,8,8">
        <Border.GestureRecognizers>
          <TapGestureRecognizer
            Command="{Binding Source={RelativeSource AncestorType={x:Type ContentPage}}, Path=BindingContext.SelectLocationCommand}"
            CommandParameter="{Binding .}" />
        </Border.GestureRecognizers>
        <Border.Triggers>
          <DataTrigger TargetType="Border" Binding="{Binding IsSelected}" Value="True">
            <Setter Property="Stroke" Value="{StaticResource Primary}" />
            <Setter Property="StrokeThickness" Value="1.6" />
          </DataTrigger>
        </Border.Triggers>
        <Grid ColumnDefinitions="36,*,Auto" ColumnSpacing="10">
          <Border Grid.Column="0" Style="{StaticResource IconTile}"
                  StrokeShape="RoundRectangle 12" WidthRequest="32" HeightRequest="32">
            <Label Text="{StaticResource IconWork}" FontFamily="Segoe MDL2 Assets"
                   FontSize="15" TextColor="{StaticResource Primary}"
                   HorizontalOptions="Center" VerticalOptions="Center">
              <Label.Triggers>
                <DataTrigger TargetType="Label" Binding="{Binding Code}" Value="WFH">
                  <Setter Property="Text" Value="{StaticResource IconPerson}" />
                </DataTrigger>
              </Label.Triggers>
            </Label>
          </Border>
          <VerticalStackLayout Grid.Column="1" VerticalOptions="Center" Spacing="1">
            <Label Text="{Binding DisplayName}"
                   FontAttributes="Bold" TextColor="{StaticResource TextPrimary}" FontSize="13"
                   LineBreakMode="TailTruncation" MaxLines="1" />
            <Label Text="{Binding SubTitle}"
                   TextColor="{StaticResource TextSecondary}" FontSize="11"
                   LineBreakMode="TailTruncation" MaxLines="1" />
          </VerticalStackLayout>
          <Border Grid.Column="2"
                  WidthRequest="22" HeightRequest="22"
                  StrokeShape="RoundRectangle 11"
                  Stroke="{StaticResource SoftOutline}" StrokeThickness="2"
                  BackgroundColor="Transparent"
                  VerticalOptions="Center">
            <Border.Triggers>
              <DataTrigger TargetType="Border" Binding="{Binding IsSelected}" Value="True">
                <Setter Property="BackgroundColor" Value="{StaticResource Primary}" />
                <Setter Property="StrokeThickness" Value="0" />
              </DataTrigger>
            </Border.Triggers>
            <Label Text="{StaticResource IconCheck}" FontFamily="Segoe MDL2 Assets"
                   FontSize="11" TextColor="White"
                   HorizontalOptions="Center" VerticalOptions="Center"
                   IsVisible="{Binding IsSelected}" />
          </Border>
        </Grid>
      </Border>
    </DataTemplate>
  </BindableLayout.ItemTemplate>
</FlexLayout>
```

- [ ] **Step 5: Add a failing EndSession top-app cap test**

Add to `EndSessionViewModelTests.cs`:

```csharp
[Fact]
public void LoadFromSnapshot_CapsTrayTopAppsPreviewAtFour()
{
    var metrics = new SessionDayMetrics();
    metrics.AddAppUsageSample("chrome.exe", TimeSpan.FromMinutes(50));
    metrics.AddAppUsageSample("code.exe", TimeSpan.FromMinutes(40));
    metrics.AddAppUsageSample("teams.exe", TimeSpan.FromMinutes(30));
    metrics.AddAppUsageSample("outlook.exe", TimeSpan.FromMinutes(20));
    metrics.AddAppUsageSample("excel.exe", TimeSpan.FromMinutes(10));

    var vm = new EndSessionViewModel(new FakeNamedPipeClient(), metrics);
    vm.LoadSummary(DateTimeOffset.UtcNow.AddHours(-8), DateTimeOffset.UtcNow, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

    Assert.Equal(4, vm.TopApps.Count);
}
```

Ensure the file has this using:

```csharp
using ONEVO.Agent.TrayApp.Services;
```

- [ ] **Step 6: Run the EndSession red test**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter LoadFromSnapshot_CapsTrayTopAppsPreviewAtFour -v normal
```

Expected result: fail because the current preview asks for five top apps.

- [ ] **Step 7: Cap top apps in `EndSessionViewModel.cs`**

Replace:

```csharp
var top = _dayMetrics.GetTopApps(5);
```

with:

```csharp
var top = _dayMetrics.GetTopApps(4);
```

- [ ] **Step 8: Replace the EndSession top-app `CollectionView`**

Replace the `CollectionView ItemsSource="{Binding TopApps}"` block with:

```xml
<FlexLayout BindableLayout.ItemsSource="{Binding TopApps}"
            Direction="Row"
            Wrap="NoWrap"
            JustifyContent="SpaceBetween"
            HeightRequest="70">
  <BindableLayout.ItemTemplate>
    <DataTemplate x:DataType="vm:TopAppItem">
      <Border Style="{StaticResource TrayDenseGlassCard}" Padding="10,8" WidthRequest="132">
        <VerticalStackLayout Spacing="2" HorizontalOptions="Center">
          <Image Source="{Binding IconSource}"
                 WidthRequest="22" HeightRequest="22"
                 Aspect="AspectFit"
                 HorizontalOptions="Center"
                 IsVisible="{Binding IconSource, Converter={StaticResource IsNotNullConverter}}" />
          <Label Text="{Binding Name}" FontSize="11" FontAttributes="Bold"
                 TextColor="{StaticResource TextPrimary}"
                 HorizontalOptions="Center" HorizontalTextAlignment="Center"
                 LineBreakMode="TailTruncation" MaxLines="1" />
          <Label Text="{Binding Duration}" FontSize="12" FontAttributes="Bold"
                 TextColor="{StaticResource Primary}"
                 HorizontalOptions="Center" />
        </VerticalStackLayout>
      </Border>
    </DataTemplate>
  </BindableLayout.ItemTemplate>
</FlexLayout>
```

- [ ] **Step 9: Run tests**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "WorkLocationViewModelTests|EndSessionViewModelTests|TrayScreenLayoutContractTests" -v normal
```

Expected result: WorkLocation and EndSession tests pass. Contract tests still fail for remaining page resources until Tasks 5-7 complete.

- [ ] **Step 10: Commit**

```powershell
git add ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs
git add ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml
git add ONEVO.Agent.TrayApp/ViewModels/EndSessionViewModel.cs
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/EndSessionViewModelTests.cs
git add ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml
git commit -m "feat(ui): replace tray finite list scrolling with compact previews"
```

---

### Task 5: Compact Onboarding Utility Screens

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml`
- Modify: `ONEVO.Agent.TrayApp/Views/PrepareWorkspacePage.xaml`
- Modify: `ONEVO.Agent.TrayApp/Views/ReviewSetupPage.xaml`

**Interfaces:**
- Consumes: Existing viewmodels and Task 2 compact styles.
- Produces: Connect, Prepare, and Review pages that fit the single-screen contract without logic changes.

- [ ] **Step 1: Update root padding on all three pages**

Use the shared page padding on each root grid:

```xml
<Grid RowDefinitions="Auto,*,Auto" Padding="{StaticResource TrayPagePadding}">
```

Use `TrayDensePagePadding` on `PrepareWorkspacePage.xaml`:

```xml
<Grid RowDefinitions="Auto,*,Auto" Padding="{StaticResource TrayDensePagePadding}">
```

- [ ] **Step 2: Compact page titles**

Where a page manually sets title spans to `FontSize="24"` or page labels inherit `PageTitle` at 26px, set visible page title text to 22px:

```xml
<Span Text="Welcome to " FontSize="22" FontAttributes="Bold"
      TextColor="{StaticResource TextPrimary}" />
<Span Text="OneXso Workspace" FontSize="22" FontAttributes="Bold"
      TextColor="{StaticResource BrandTitle}" />
```

For `PrepareWorkspacePage.xaml` and `ReviewSetupPage.xaml`, use:

```xml
<Label Text="Setting up " Style="{StaticResource PageTitle}" FontSize="22" />
<Label Text="your workspace" Style="{StaticResource PageTitleBrand}" FontSize="22" />
```

- [ ] **Step 3: Replace bulky card styles in these pages**

For repeated form/info/detail cards, replace:

```xml
Style="{StaticResource GlassCard}"
```

with:

```xml
Style="{StaticResource TrayCompactGlassCard}"
```

For small row cards and two-column helper cards, use:

```xml
Style="{StaticResource TrayDenseGlassCard}"
```

- [ ] **Step 4: Compact `PrepareWorkspacePage.xaml` progress ring**

Replace the 96px ring grid:

```xml
<Grid Grid.Column="0" WidthRequest="96" HeightRequest="96"
      VerticalOptions="Center">
```

with:

```xml
<Grid Grid.Column="0" WidthRequest="88" HeightRequest="88"
      VerticalOptions="Center">
```

Inside that ring block, change all matching `96` ring sizes to `88` and the round-rectangle radius from `48` to `44`.

- [ ] **Step 5: Compact setup/detail row padding**

In `PrepareWorkspacePage.xaml` and `ReviewSetupPage.xaml`, replace detail row padding:

```xml
Padding="0,8"
```

with:

```xml
Padding="0,6"
```

Replace 32px review icons with 30px:

```xml
WidthRequest="30" HeightRequest="30"
```

- [ ] **Step 6: Use compact action styles**

Replace bottom CTA borders on these pages:

```xml
Style="{StaticResource GradientButtonBorder}"
```

with:

```xml
Style="{StaticResource TrayPrimaryActionBorder}"
```

Replace overlay button styles:

```xml
Style="{StaticResource GradientButtonOverlay}"
```

with:

```xml
Style="{StaticResource TrayPrimaryActionOverlay}"
```

- [ ] **Step 7: Build**

```powershell
dotnet build ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0
```

Expected result: build succeeds.

- [ ] **Step 8: Commit**

```powershell
git add ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml
git add ONEVO.Agent.TrayApp/Views/PrepareWorkspacePage.xaml
git add ONEVO.Agent.TrayApp/Views/ReviewSetupPage.xaml
git commit -m "feat(ui): compact onboarding pages for single-screen tray window"
```

---

### Task 6: Compact Policy And Camera Screens

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml`
- Modify: `ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml`
- Modify: `ONEVO.Agent.TrayApp/Views/BiometricEnrollmentPage.xaml`

**Interfaces:**
- Consumes: Existing `PrivacyConsentViewModel`, `PhotoCaptureWindowViewModel`, `BiometricEnrollmentViewModel`, camera controls, and compact resources.
- Produces: Policy and camera screens that keep CTA/status visible inside the window.

- [ ] **Step 1: Compact `PrivacyConsentPage.xaml` root and rows**

Change root padding:

```xml
<Grid RowDefinitions="Auto,Auto,*,Auto,Auto" Padding="{StaticResource TrayDensePagePadding}">
```

For each permission row, replace:

```xml
Padding="2,8"
```

with:

```xml
Padding="2,6"
```

Use dense card styling on the permissions card:

```xml
<Border Grid.Row="2" Style="{StaticResource TrayDenseGlassCard}" Padding="14,2"
        HorizontalOptions="Center" MaximumWidthRequest="640"
        VerticalOptions="Fill">
```

Use compact action styles on the Allow & Continue button.

- [ ] **Step 2: Compact policy row copy**

Keep each row to one title and one description. Add `MaxLines="1"` to long descriptions where needed:

```xml
<Label Text="Allows capturing your screen activity for productivity insights."
       TextColor="{StaticResource TextSecondary}" FontSize="11"
       LineBreakMode="TailTruncation" MaxLines="1" />
```

- [ ] **Step 3: Compact `PhotoCaptureWindow.xaml` frame sizes**

Use the shared resources for the circular frame:

```xml
<Grid Grid.Row="2"
      WidthRequest="{StaticResource TrayFaceFrameOuterSize}"
      HeightRequest="{StaticResource TrayFaceFrameOuterSize}"
      HorizontalOptions="Center" VerticalOptions="Center">
```

Replace the nested ring sizes:

```xml
<Ellipse WidthRequest="{StaticResource TrayFaceFrameOuterSize}"
         HeightRequest="{StaticResource TrayFaceFrameOuterSize}" />
<Border WidthRequest="{StaticResource TrayFaceFrameRingSize}"
        HeightRequest="{StaticResource TrayFaceFrameRingSize}"
        StrokeShape="RoundRectangle 110">
<Border WidthRequest="{StaticResource TrayFaceFrameInnerSize}"
        HeightRequest="{StaticResource TrayFaceFrameInnerSize}"
        StrokeShape="RoundRectangle 102">
<Grid WidthRequest="{StaticResource TrayFaceFrameInnerSize}"
      HeightRequest="{StaticResource TrayFaceFrameInnerSize}">
```

- [ ] **Step 4: Compact camera note and action**

Change the trust note card:

```xml
<Border Grid.Row="4" Style="{StaticResource TrayDenseGlassCard}" Margin="0,6,0,6" Padding="10,8">
```

Change the Continue action to:

```xml
<Border Grid.Row="5" Style="{StaticResource TrayPrimaryActionBorder}" MaximumWidthRequest="420"
        HorizontalOptions="Center">
```

and:

```xml
<Button Text="Continue"
        Command="{Binding ContinueCommand}"
        Style="{StaticResource TrayPrimaryActionOverlay}" />
```

- [ ] **Step 5: Keep `BiometricEnrollmentPage.xaml` inside the frame**

Change root padding:

```xml
<Grid RowDefinitions="Auto,*,Auto" Padding="{StaticResource TrayPagePadding}">
```

Constrain the error row so the WebView keeps the star-sized area:

```xml
<Label Grid.Row="0"
       Text="{Binding ErrorMessage}"
       TextColor="#DC2626"
       FontSize="13"
       Margin="0,0,0,8"
       MaxLines="2"
       LineBreakMode="TailTruncation"
       IsVisible="{Binding ErrorMessage, Converter={StaticResource IsNotNullConverter}}" />
```

- [ ] **Step 6: Run contract tests**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter TrayScreenLayoutContractTests -v normal
```

Expected result: `PhotoCaptureFrame_UsesCompactSizeResource` passes. Remaining failures should only be for ClockIn or any page still containing `CollectionView`.

- [ ] **Step 7: Build**

```powershell
dotnet build ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0
```

Expected result: build succeeds.

- [ ] **Step 8: Commit**

```powershell
git add ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml
git add ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml
git add ONEVO.Agent.TrayApp/Views/BiometricEnrollmentPage.xaml
git commit -m "feat(ui): compact policy and camera screens for tray fit"
```

---

### Task 7: Compact Clock-In, Active Session, Break, And End Screens

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Views/ClockInPage.xaml`
- Modify: `ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml`
- Modify: `ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml`

**Interfaces:**
- Consumes: Existing `ClockInViewModel`, `ActiveSessionViewModel`, `EndSessionViewModel`, Task 2 styles, and Task 4 top-app cap.
- Produces: The Ready, Working, On Break, break modal, and Completed states inside the single-screen contract.

- [ ] **Step 1: Compact `ClockInPage.xaml` root and header**

Change root padding:

```xml
<Grid RowDefinitions="Auto,*,Auto,Auto" Padding="{StaticResource TrayDensePagePadding}">
```

Reduce the manual header logo:

```xml
<Image Source="onexso_x_mark.png"
       Aspect="AspectFit"
       WidthRequest="34" HeightRequest="34"
       VerticalOptions="Center" />
```

- [ ] **Step 2: Reduce Clock In hero action**

Replace the `ClockInGlow` height:

```xml
HeightRequest="{StaticResource TrayHeroActionHeight}"
```

Replace the hit button height:

```xml
HeightRequest="{StaticResource TrayHeroActionHeight}"
```

Change the CTA label to:

```xml
<Label Text="CLOCK IN" TextColor="White" FontSize="20" FontAttributes="Bold"
       CharacterSpacing="0" VerticalOptions="Center" />
```

- [ ] **Step 3: Compact ClockIn cards**

Use `TrayCompactGlassCard` for the working status card and `TrayDenseGlassCard` for the three bottom status cards. Use 32px icon tiles in bottom cards:

```xml
<Border Grid.Column="0" Style="{StaticResource TrayDenseGlassCard}" Padding="12,8">
```

- [ ] **Step 4: Compact `ActiveSessionPage.xaml` root**

Change root padding:

```xml
<Grid RowDefinitions="Auto,*,Auto,Auto" Padding="{StaticResource TrayDensePagePadding}">
```

Use `TrayCompactGlassCard` for the status and schedule cards, and `TrayDenseGlassCard` for the summary strip.

- [ ] **Step 5: Compact active action row**

Set all working state action buttons to 44-48px:

```xml
<Button Grid.Column="0"
        Text="Break"
        ImageSource="icon_coffee_white.png"
        Command="{Binding RequestBreakCommand}"
        IsVisible="{Binding IsOnBreak, Converter={StaticResource InvertBoolConverter}}"
        Style="{StaticResource OrangePillButton}"
        HeightRequest="44"
        FontSize="13" />
```

Apply the same `HeightRequest="44"` and `FontSize="13"` pattern to Clock Out and Dashboard. For End Break, change the border and overlay button heights to 48:

```xml
<Border Grid.Column="0" Grid.ColumnSpan="3"
        Style="{StaticResource OrangeGradientButtonBorder}"
        HeightRequest="48"
        IsVisible="{Binding IsOnBreak}">
```

- [ ] **Step 6: Compact the break confirmation overlay**

Use a smaller modal width and remove the text emoji:

```xml
<Border Style="{StaticResource TrayCompactGlassCard}"
        WidthRequest="340"
        HorizontalOptions="Center"
        VerticalOptions="Center"
        Padding="20">
  <VerticalStackLayout Spacing="10">
    <Border WidthRequest="34" HeightRequest="34"
            StrokeShape="RoundRectangle 17"
            BackgroundColor="{StaticResource BreakAccent}"
            StrokeThickness="0"
            HorizontalOptions="Center">
      <Image Source="icon_coffee_white.png"
             WidthRequest="18" HeightRequest="18"
             HorizontalOptions="Center" VerticalOptions="Center" />
    </Border>
```

- [ ] **Step 7: Compact `EndSessionPage.xaml`**

Change root padding:

```xml
<Grid RowDefinitions="Auto,*,Auto" Padding="{StaticResource TrayDensePagePadding}">
```

Use `TrayDenseGlassCard` for metric cards and set the top-app strip card to dense padding:

```xml
<Border Grid.Row="2" Style="{StaticResource TrayDenseGlassCard}" Padding="10,8">
```

Ensure the footer uses the shared compact `FooterStatusBar` and no nested footer grid adds more than 4px top padding.

- [ ] **Step 8: Run contract tests**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter TrayScreenLayoutContractTests -v normal
```

Expected result: all `TrayScreenLayoutContractTests` pass.

- [ ] **Step 9: Build**

```powershell
dotnet build ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0
```

Expected result: build succeeds.

- [ ] **Step 10: Commit**

```powershell
git add ONEVO.Agent.TrayApp/Views/ClockInPage.xaml
git add ONEVO.Agent.TrayApp/Views/ActiveSessionPage.xaml
git add ONEVO.Agent.TrayApp/Views/EndSessionPage.xaml
git commit -m "feat(ui): fit workday session screens into tray canvas"
```

---

### Task 8: Full Test And Visual Verification Pass

**Files:**
- No source changes unless verification exposes a specific defect.

**Interfaces:**
- Consumes: All tasks above.
- Produces: Evidence that the current tray route set fits in one desktop window.

- [ ] **Step 1: Run the full TrayApp test project**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj -v normal
```

Expected result: all tests pass.

- [ ] **Step 2: Build the tray app**

```powershell
dotnet build ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0
```

Expected result: build succeeds.

- [ ] **Step 3: Run the service and tray app for manual QA**

```powershell
dotnet run --project ONEVO.Agent.Service\ONEVO.Agent.Service.csproj -c Debug
```

In a second terminal:

```powershell
dotnet run --project ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -c Debug -f net10.0-windows10.0.19041.0
```

- [ ] **Step 4: Check every current route at default size**

At 1024x720, verify:

```text
connect: activation form, Connect, Open Activation Website, info cards, footer visible
prepare: progress/ready content, Continue, footer visible
location: live location, all location cards, Confirm Location, footer visible
photo: camera ring, status, trust note, Continue, footer visible
enrollment-biometric: WebView fills star area, status/footer visible
review: all details, Confirm & Continue, Back, footer visible
policy: six permissions, policy note, Allow & Continue, footer visible
clockin: Clock In hero action, status cards, footer visible
active working: hero, timers, Break, Clock Out, Dashboard, summary, footer visible
active on break: hero, Break Timer, End Break, summary, footer visible
end: summary metrics, top apps preview, Download Summary, Close App, footer visible
```

- [ ] **Step 5: Check the minimum size**

Resize the window to 960x700 and repeat the route checks. Acceptable behavior: text truncates cleanly where planned. Unacceptable behavior: vertical scrollbar, hidden CTA, clipped footer, overlapping labels, or a modal exceeding the window.

- [ ] **Step 6: Check screenshot-reference alignment**

Compare against the contact sheet and individual images in `C:/Users/user/OneDrive/Pictures/tray app/`. Required alignment:

```text
Overall feel: light glass surface, OneXso cyan/blue/purple brand, calm desktop utility
Ready/Working/Break/Completed: state colors and primary action hierarchy match the reference flow
Onboarding: large raster illustration remains visible where the current route uses a two-pane page
Policy: permission rows remain readable and toggles remain aligned
Clock out: summary is dense enough for one screen and does not create a horizontal top-app scroller
```

- [ ] **Step 7: Commit verification-only notes if any source was adjusted**

If visual QA required small source fixes, commit them:

```powershell
git add ONEVO.Agent.TrayApp tests/ONEVO.Agent.TrayApp.Tests
git commit -m "fix(ui): resolve tray single-screen visual QA issues"
```

If no source changed during QA, record the verification result in the implementation task response instead of creating a commit.

---

## Self-Review

Spec coverage:
- Screenshot inventory is mapped in the spec.
- Current route list is covered by Tasks 4-7.
- Shared single-screen contract is covered by Tasks 1-3.
- No-scroll finite lists are covered by Task 4.
- Build, unit tests, and visual QA are covered by Task 8.

Risk checks:
- The plan avoids rebuilding the external dashboard inside the tray app.
- The plan avoids service and lifecycle rewrites.
- The plan preserves existing raster hero assets and brand palette.
- The plan accounts for the dirty worktree by changing only named UI/test/docs files during execution.

Execution note:
- Start execution from Task 1 in a clean working branch or isolated worktree.
- Each task should be reviewed before the next task begins because XAML fit regressions are easiest to catch in small groups.
