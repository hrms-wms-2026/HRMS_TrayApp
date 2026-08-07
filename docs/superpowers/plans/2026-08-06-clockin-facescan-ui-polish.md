# Clock-In Face Scan + UI Polish Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the full clock-in → face scan (in-app camera) → running timer flow, and redesign PrivacyConsentPage and ReviewSetupPage to match the product mockups.

**Architecture:** CameraService replaced with `Windows.Media.Capture.MediaCapture` (no external dialog); PrivacyConsentPage icons switched from emoji to Segoe MDL2 Assets glyphs on blue badges; ReviewSetupPage left panel gets a gradient illustration matching the ClockIn page style; FaceVerificationCompleted persisted to Preferences so the Review page shows the correct "Completed" badge.

**Tech Stack:** .NET MAUI 10, Windows-only (`net10.0-windows10.0.19041.0`), CommunityToolkit.Mvvm, `Windows.Media.Capture.MediaCapture`, Segoe MDL2 Assets (Windows built-in font), MAUI Preferences.

---

## File Map

| File | Change |
|------|--------|
| `ONEVO.Agent.TrayApp/Services/CameraService.cs` | Replace `MediaPicker` → `MediaCapture` (in-process, no external dialog) |
| `ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml` | Replace emoji badges with Segoe MDL2 Assets on blue badge backgrounds |
| `ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml.cs` | Add `OnAppearing` → call `vm.ApplyPolicy(pipe.LastKnownPolicy)` |
| `ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs` | Inject `INamedPipeClient` |
| `ONEVO.Agent.TrayApp/Views/ReviewSetupPage.xaml` | Redesign left panel (gradient illustration), fix FaceVerification badge |
| `ONEVO.Agent.TrayApp/Views/ReviewSetupPage.xaml.cs` | Add `OnAppearing` hookup (already done, verify) |
| `ONEVO.Agent.TrayApp/ViewModels/ReviewSetupViewModel.cs` | Read `onevo.face_verified` from Preferences in `OnAppearing` |
| `ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs` | Save `onevo.face_verified = true` to Preferences after onboarding photo |
| `ONEVO.Agent.TrayApp/MauiProgram.cs` | No change needed (INamedPipeClient already registered) |

---

## Task 1 — Replace CameraService with in-app MediaCapture

**Why:** `MediaPicker.CapturePhotoAsync` opens an external camera app window. The user wants the face scan to stay inside the tray app. `Windows.Media.Capture.MediaCapture.CapturePhotoToStreamAsync` captures a JPEG silently from the webcam without leaving the app.

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Services/CameraService.cs`

- [ ] **Step 1: Replace the implementation**

Replace the entire content of `CameraService.cs` with:

```csharp
namespace ONEVO.Agent.TrayApp.Services;

using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

public sealed class CameraService : ICameraService
{
    public async Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default)
    {
        try
        {
            var capture = new MediaCapture();
            await capture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Video
            });

            using var stream = new InMemoryRandomAccessStream();
            var props = ImageEncodingProperties.CreateJpeg();
            await capture.CapturePhotoToStreamAsync(props, stream);
            capture.Dispose();

            stream.Seek(0);
            var bytes = new byte[stream.Size];
            await stream.AsStreamForRead().ReadExactlyAsync(bytes, 0, bytes.Length, ct);
            return bytes.Length > 0 ? bytes : null;
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```powershell
dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj --configuration Debug -f net10.0-windows10.0.19041.0
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/CameraService.cs
git commit -m "fix(camera): replace MediaPicker with MediaCapture for in-app face scan"
```

---

## Task 2 — PrivacyConsentPage: Professional icon badges + policy wiring

**Why:** Current page uses color emoji (🖥 📍 📷 etc.) which do not respond to `TextColor` and can't be styled white-on-blue. Switching to Segoe MDL2 Assets characters (Windows built-in monochrome font) gives clean, scalable, themeable icons matching the mockup. Also wires `ApplyPolicy()` so toggles reflect the real service policy on page entry.

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml`
- Modify: `ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml.cs`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs`

### 2a — Inject INamedPipeClient into PrivacyConsentViewModel

- [ ] **Step 1: Update PrivacyConsentViewModel constructor**

Replace the entire `PrivacyConsentViewModel.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PrivacyConsentViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    // Always on — required by policy, toggle locked in UI
    [ObservableProperty] private bool _screenMonitoringEnabled = true;

    [ObservableProperty] private bool _appTrackingEnabled     = true;
    [ObservableProperty] private bool _locationAccessEnabled  = true;
    [ObservableProperty] private bool _cameraAccessEnabled    = false;
    [ObservableProperty] private bool _notificationsEnabled   = true;
    [ObservableProperty] private bool _keyboardMouseEnabled   = true;

    public PrivacyConsentViewModel(INamedPipeClient pipe)
    {
        Title = "Allow Required Policies";
        _pipe = pipe;
    }

    public void OnAppearing()
    {
        if (_pipe.LastKnownPolicy is { } policy)
            ApplyPolicy(policy);
    }

    public void ApplyPolicy(AgentPolicy policy)
    {
        AppTrackingEnabled  = policy.AppUsageEnabled;
        CameraAccessEnabled = policy.CameraVerificationEnabled;
    }

    [RelayCommand]
    private async Task AllowAndContinue()
    {
        try { await Shell.Current.GoToAsync("//clockin"); }
        catch { /* unit tests */ }
    }
}
```

### 2b — Wire OnAppearing in code-behind

- [ ] **Step 2: Update PrivacyConsentPage.xaml.cs**

Replace entire content:

```csharp
namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class PrivacyConsentPage : ContentPage
{
    public PrivacyConsentPage()
    {
        InitializeComponent();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (BindingContext is null && Handler?.MauiContext?.Services is { } sp)
            BindingContext = sp.GetRequiredService<PrivacyConsentViewModel>();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PrivacyConsentViewModel vm)
            vm.OnAppearing();
    }
}
```

### 2c — Replace emoji badges with Segoe MDL2 Assets icon badges

**Icon character reference (Segoe MDL2 Assets):**
| Permission | Char | Code |
|------------|------|------|
| Screen Monitoring | Monitor | `&#xE7F4;` |
| Application Tracking | Category/Grid | `&#xE71D;` |
| Location Access | Map Pin | `&#xE81D;` |
| Camera Access | Camera | `&#xE722;` |
| System Notifications | Ringer/Bell | `&#xEA8F;` |
| Keyboard & Mouse | Keyboard | `&#xE765;` |

- [ ] **Step 3: Replace PrivacyConsentPage.xaml**

Replace the entire file with:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.PrivacyConsentPage"
             x:DataType="vm:PrivacyConsentViewModel"
             BackgroundColor="{StaticResource Background}"
             Title="Allow Required Policies">
  <ScrollView>
    <VerticalStackLayout Padding="48,36" Spacing="0"
                         HorizontalOptions="Center" MaximumWidthRequest="600">

      <!-- Header -->
      <HorizontalStackLayout HorizontalOptions="Center" Spacing="6">
        <Label Text="Allow " Style="{StaticResource PageTitle}" />
        <Label Text="Required Policies" Style="{StaticResource PageTitleAccent}" />
      </HorizontalStackLayout>
      <Label Text="To provide the best experience, please allow the required permissions."
             Style="{StaticResource PageSubtitle}" Margin="0,8,0,28" />

      <!-- Policies card -->
      <Border Style="{StaticResource Card}" Margin="0,0,0,24">
        <VerticalStackLayout Spacing="0">

          <!-- Screen Monitoring -->
          <Grid ColumnDefinitions="44,*,Auto" Padding="4,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="38" HeightRequest="38"
                    StrokeShape="RoundRectangle 10" StrokeThickness="0"
                    BackgroundColor="{StaticResource Primary}">
              <Label Text="&#xE7F4;" FontFamily="Segoe MDL2 Assets" FontSize="18"
                     TextColor="White"
                     HorizontalOptions="Center" VerticalOptions="Center" />
            </Border>
            <VerticalStackLayout Grid.Column="1" VerticalOptions="Center">
              <Label Text="Screen Monitoring" FontAttributes="Bold"
                     TextColor="{StaticResource TextPrimary}" FontSize="13" />
              <Label Text="Allows capturing your screen activity for productivity insights."
                     TextColor="{StaticResource TextSecondary}" FontSize="11" />
            </VerticalStackLayout>
            <Switch Grid.Column="2" IsToggled="{Binding ScreenMonitoringEnabled}"
                    IsEnabled="False" VerticalOptions="Center" />
          </Grid>
          <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

          <!-- Application Tracking -->
          <Grid ColumnDefinitions="44,*,Auto" Padding="4,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="38" HeightRequest="38"
                    StrokeShape="RoundRectangle 10" StrokeThickness="0"
                    BackgroundColor="{StaticResource Primary}">
              <Label Text="&#xE71D;" FontFamily="Segoe MDL2 Assets" FontSize="18"
                     TextColor="White"
                     HorizontalOptions="Center" VerticalOptions="Center" />
            </Border>
            <VerticalStackLayout Grid.Column="1" VerticalOptions="Center">
              <Label Text="Application Tracking" FontAttributes="Bold"
                     TextColor="{StaticResource TextPrimary}" FontSize="13" />
              <Label Text="Tracks application usage to help improve your workflow."
                     TextColor="{StaticResource TextSecondary}" FontSize="11" />
            </VerticalStackLayout>
            <Switch Grid.Column="2" IsToggled="{Binding AppTrackingEnabled}"
                    VerticalOptions="Center" />
          </Grid>
          <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

          <!-- Location Access -->
          <Grid ColumnDefinitions="44,*,Auto" Padding="4,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="38" HeightRequest="38"
                    StrokeShape="RoundRectangle 10" StrokeThickness="0"
                    BackgroundColor="{StaticResource Primary}">
              <Label Text="&#xE81D;" FontFamily="Segoe MDL2 Assets" FontSize="18"
                     TextColor="White"
                     HorizontalOptions="Center" VerticalOptions="Center" />
            </Border>
            <VerticalStackLayout Grid.Column="1" VerticalOptions="Center">
              <Label Text="Location Access" FontAttributes="Bold"
                     TextColor="{StaticResource TextPrimary}" FontSize="13" />
              <Label Text="Helps contextualize your work environment."
                     TextColor="{StaticResource TextSecondary}" FontSize="11" />
            </VerticalStackLayout>
            <Switch Grid.Column="2" IsToggled="{Binding LocationAccessEnabled}"
                    VerticalOptions="Center" />
          </Grid>
          <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

          <!-- Camera Access -->
          <Grid ColumnDefinitions="44,*,Auto" Padding="4,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="38" HeightRequest="38"
                    StrokeShape="RoundRectangle 10" StrokeThickness="0"
                    BackgroundColor="{StaticResource Primary}">
              <Label Text="&#xE722;" FontFamily="Segoe MDL2 Assets" FontSize="18"
                     TextColor="White"
                     HorizontalOptions="Center" VerticalOptions="Center" />
            </Border>
            <VerticalStackLayout Grid.Column="1" VerticalOptions="Center">
              <Label Text="Camera Access" FontAttributes="Bold"
                     TextColor="{StaticResource TextPrimary}" FontSize="13" />
              <Label Text="Enables video meetings and identity verification."
                     TextColor="{StaticResource TextSecondary}" FontSize="11" />
            </VerticalStackLayout>
            <Switch Grid.Column="2" IsToggled="{Binding CameraAccessEnabled}"
                    VerticalOptions="Center" />
          </Grid>
          <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

          <!-- System Notifications -->
          <Grid ColumnDefinitions="44,*,Auto" Padding="4,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="38" HeightRequest="38"
                    StrokeShape="RoundRectangle 10" StrokeThickness="0"
                    BackgroundColor="{StaticResource Primary}">
              <Label Text="&#xEA8F;" FontFamily="Segoe MDL2 Assets" FontSize="18"
                     TextColor="White"
                     HorizontalOptions="Center" VerticalOptions="Center" />
            </Border>
            <VerticalStackLayout Grid.Column="1" VerticalOptions="Center">
              <Label Text="System Notifications" FontAttributes="Bold"
                     TextColor="{StaticResource TextPrimary}" FontSize="13" />
              <Label Text="Keeps you informed with important updates and alerts."
                     TextColor="{StaticResource TextSecondary}" FontSize="11" />
            </VerticalStackLayout>
            <Switch Grid.Column="2" IsToggled="{Binding NotificationsEnabled}"
                    VerticalOptions="Center" />
          </Grid>
          <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

          <!-- Keyboard & Mouse Activity -->
          <Grid ColumnDefinitions="44,*,Auto" Padding="4,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="38" HeightRequest="38"
                    StrokeShape="RoundRectangle 10" StrokeThickness="0"
                    BackgroundColor="{StaticResource Primary}">
              <Label Text="&#xE765;" FontFamily="Segoe MDL2 Assets" FontSize="18"
                     TextColor="White"
                     HorizontalOptions="Center" VerticalOptions="Center" />
            </Border>
            <VerticalStackLayout Grid.Column="1" VerticalOptions="Center">
              <Label Text="Keyboard &amp; Mouse Activity" FontAttributes="Bold"
                     TextColor="{StaticResource TextPrimary}" FontSize="13" />
              <Label Text="Helps analyze work patterns to enhance productivity."
                     TextColor="{StaticResource TextSecondary}" FontSize="11" />
            </VerticalStackLayout>
            <Switch Grid.Column="2" IsToggled="{Binding KeyboardMouseEnabled}"
                    IsEnabled="False" VerticalOptions="Center" />
          </Grid>

          <!-- Policy footer note -->
          <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />
          <HorizontalStackLayout Spacing="8" Padding="4,12" HorizontalOptions="Center">
            <Border WidthRequest="22" HeightRequest="22"
                    StrokeShape="RoundRectangle 11" StrokeThickness="0"
                    BackgroundColor="{StaticResource Primary}">
              <Label Text="&#xEA18;" FontFamily="Segoe MDL2 Assets" FontSize="11"
                     TextColor="White"
                     HorizontalOptions="Center" VerticalOptions="Center" />
            </Border>
            <Label Text="Permissions are managed according to your company policy."
                   FontSize="11" TextColor="{StaticResource TextSecondary}"
                   VerticalOptions="Center" />
          </HorizontalStackLayout>

        </VerticalStackLayout>
      </Border>

      <!-- Allow & Continue button -->
      <Border Style="{StaticResource GradientButtonBorder}">
        <Button Text="Allow &amp; Continue"
                Command="{Binding AllowAndContinueCommand}"
                Style="{StaticResource GradientButtonOverlay}" />
      </Border>

    </VerticalStackLayout>
  </ScrollView>
</ContentPage>
```

- [ ] **Step 4: Build to verify**

```powershell
dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj --configuration Debug -f net10.0-windows10.0.19041.0
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml \
        ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml.cs \
        ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs
git commit -m "feat(ui): redesign PrivacyConsentPage with MDL2 icon badges + policy wiring"
```

---

## Task 3 — ReviewSetupPage: Gradient left panel + FaceVerification badge

**Why:** ReviewSetupPage left panel currently shows only a shield emoji. The mockup shows a rich gradient illustration panel (matching the ClockIn page style). Also `FaceVerificationCompleted` is never loaded from storage so it always shows "Pending" even after the user completed the photo step.

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Views/ReviewSetupPage.xaml`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ReviewSetupViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs`

### 3a — Save face_verified preference after onboarding photo

- [ ] **Step 1: Update PhotoCaptureWindowViewModel Continue()**

In `PhotoCaptureWindowViewModel.cs`, add `Preferences.Set("onevo.face_verified", true)` when in the onboarding (non-clockin) context, right before navigating to `//review`. Locate the end of `Continue()` and replace:

```csharp
        try { await Shell.Current.GoToAsync("//review"); }
        catch { /* unit tests */ }
```

with:

```csharp
        Preferences.Set("onevo.face_verified", true);
        try { await Shell.Current.GoToAsync("//review"); }
        catch { /* unit tests */ }
```

### 3b — Read face_verified in ReviewSetupViewModel

- [ ] **Step 2: Update ReviewSetupViewModel.OnAppearing()**

Replace the `OnAppearing()` method:

```csharp
    public void OnAppearing()
    {
        FullName                  = Preferences.Get("onevo.employee_display_name", string.Empty);
        WorkEmail                 = Preferences.Get("onevo.employee_email",         string.Empty);
        EmployeeId                = Preferences.Get("onevo.employee_id",            string.Empty);
        WorkLocation              = Preferences.Get("onevo.work_location_display",  string.Empty);
        FaceVerificationCompleted = Preferences.Get("onevo.face_verified",          false);
    }
```

### 3c — Redesign ReviewSetupPage.xaml

**Segoe MDL2 Assets row icons:**
| Row | Icon | Code |
|-----|------|------|
| Full Name | Person | `&#xE77B;` |
| Email | Mail | `&#xE715;` |
| Employee ID | Contact Info | `&#xE8AE;` |
| Location | Map Pin | `&#xE81D;` |
| Face Verification | Camera | `&#xE722;` |

- [ ] **Step 3: Replace ReviewSetupPage.xaml**

Replace entire file with:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.ReviewSetupPage"
             x:DataType="vm:ReviewSetupViewModel"
             BackgroundColor="{StaticResource Background}"
             Title="Confirm Your Details">

  <Grid ColumnDefinitions="4*,6*">

    <!-- Left: gradient illustration panel -->
    <Border Grid.Column="0" StrokeThickness="1" Stroke="{StaticResource Separator}"
            StrokeShape="RoundRectangle 0" Margin="0">
      <Grid>
        <Grid.Background>
          <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
            <GradientStop Color="#D8E4FF" Offset="0" />
            <GradientStop Color="#E8D8FF" Offset="1" />
          </LinearGradientBrush>
        </Grid.Background>

        <!-- Decorative blobs -->
        <Ellipse WidthRequest="200" HeightRequest="200"
                 HorizontalOptions="Start" VerticalOptions="End"
                 Margin="-70,0,0,30" Opacity="0.25">
          <Ellipse.Fill>
            <RadialGradientBrush Center="0.5,0.5" Radius="0.5">
              <GradientStop Color="#7B3FE4" Offset="0" />
              <GradientStop Color="#7B3FE400" Offset="1" />
            </RadialGradientBrush>
          </Ellipse.Fill>
        </Ellipse>
        <Ellipse WidthRequest="140" HeightRequest="140"
                 HorizontalOptions="End" VerticalOptions="Start"
                 Margin="0,20,-40,0" Opacity="0.20">
          <Ellipse.Fill>
            <RadialGradientBrush Center="0.5,0.5" Radius="0.5">
              <GradientStop Color="#1A5FF7" Offset="0" />
              <GradientStop Color="#1A5FF700" Offset="1" />
            </RadialGradientBrush>
          </Ellipse.Fill>
        </Ellipse>
        <Ellipse WidthRequest="80" HeightRequest="80"
                 HorizontalOptions="Center" VerticalOptions="Center"
                 Margin="60,-80,0,0" Opacity="0.15">
          <Ellipse.Fill>
            <RadialGradientBrush Center="0.5,0.5" Radius="0.5">
              <GradientStop Color="#1A5FF7" Offset="0" />
              <GradientStop Color="#1A5FF700" Offset="1" />
            </RadialGradientBrush>
          </Ellipse.Fill>
        </Ellipse>

        <!-- Shield + person illustration -->
        <VerticalStackLayout VerticalOptions="Center" HorizontalOptions="Center"
                             Spacing="16" Padding="24">

          <!-- Shield icon badge -->
          <Border WidthRequest="96" HeightRequest="96"
                  StrokeShape="RoundRectangle 28" StrokeThickness="0"
                  HorizontalOptions="Center">
            <Border.Background>
              <LinearGradientBrush StartPoint="0,0" EndPoint="1,1">
                <GradientStop Color="#1A5FF7" Offset="0" />
                <GradientStop Color="#7B3FE4" Offset="1" />
              </LinearGradientBrush>
            </Border.Background>
            <Label Text="&#xEA18;" FontFamily="Segoe MDL2 Assets" FontSize="48"
                   TextColor="White"
                   HorizontalOptions="Center" VerticalOptions="Center" />
          </Border>

          <!-- Verified checkmark badge -->
          <Border WidthRequest="40" HeightRequest="40"
                  StrokeShape="RoundRectangle 20" StrokeThickness="2"
                  Stroke="White" BackgroundColor="{StaticResource StatusGreen}"
                  HorizontalOptions="Center" Margin="0,-20,0,0">
            <Label Text="&#xE73E;" FontFamily="Segoe MDL2 Assets" FontSize="20"
                   TextColor="White"
                   HorizontalOptions="Center" VerticalOptions="Center" />
          </Border>

          <Label Text="Review your information&#x0a;before we proceed."
                 FontSize="12" TextColor="{StaticResource TextSecondary}"
                 HorizontalTextAlignment="Center" />
        </VerticalStackLayout>
      </Grid>
    </Border>

    <!-- Right: form panel -->
    <ScrollView Grid.Column="1" BackgroundColor="{StaticResource Background}">
      <VerticalStackLayout Padding="36,36,36,28" Spacing="0">

        <!-- Header -->
        <HorizontalStackLayout Spacing="6">
          <Label Text="Confirm Your " Style="{StaticResource PageTitle}" />
          <Label Text="Details" Style="{StaticResource PageTitleAccent}" />
        </HorizontalStackLayout>
        <Label Text="Please review your information before continuing."
               Style="{StaticResource PageSubtitle}"
               HorizontalTextAlignment="Start"
               Margin="0,6,0,24" />

        <!-- Details card -->
        <Border Style="{StaticResource Card}" Margin="0,0,0,24">
          <VerticalStackLayout Spacing="0">

            <!-- Full Name -->
            <Grid ColumnDefinitions="36,*,Auto" Padding="4,12" ColumnSpacing="12">
              <Border Grid.Column="0" WidthRequest="32" HeightRequest="32"
                      StrokeShape="RoundRectangle 8" StrokeThickness="0"
                      BackgroundColor="{StaticResource Primary}"
                      HorizontalOptions="Center" VerticalOptions="Center">
                <Label Text="&#xE77B;" FontFamily="Segoe MDL2 Assets" FontSize="16"
                       TextColor="White"
                       HorizontalOptions="Center" VerticalOptions="Center" />
              </Border>
              <Label Grid.Column="1" Text="Full Name" Style="{StaticResource FieldLabel}" />
              <Label Grid.Column="2" Text="{Binding FullName}" Style="{StaticResource FieldValue}" />
            </Grid>
            <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

            <!-- Email -->
            <Grid ColumnDefinitions="36,*,Auto" Padding="4,12" ColumnSpacing="12">
              <Border Grid.Column="0" WidthRequest="32" HeightRequest="32"
                      StrokeShape="RoundRectangle 8" StrokeThickness="0"
                      BackgroundColor="{StaticResource Primary}"
                      HorizontalOptions="Center" VerticalOptions="Center">
                <Label Text="&#xE715;" FontFamily="Segoe MDL2 Assets" FontSize="16"
                       TextColor="White"
                       HorizontalOptions="Center" VerticalOptions="Center" />
              </Border>
              <Label Grid.Column="1" Text="Email" Style="{StaticResource FieldLabel}" />
              <Label Grid.Column="2" Text="{Binding WorkEmail}" Style="{StaticResource FieldValue}" />
            </Grid>
            <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

            <!-- Employee ID -->
            <Grid ColumnDefinitions="36,*,Auto" Padding="4,12" ColumnSpacing="12">
              <Border Grid.Column="0" WidthRequest="32" HeightRequest="32"
                      StrokeShape="RoundRectangle 8" StrokeThickness="0"
                      BackgroundColor="{StaticResource Primary}"
                      HorizontalOptions="Center" VerticalOptions="Center">
                <Label Text="&#xE8AE;" FontFamily="Segoe MDL2 Assets" FontSize="16"
                       TextColor="White"
                       HorizontalOptions="Center" VerticalOptions="Center" />
              </Border>
              <Label Grid.Column="1" Text="Employee ID" Style="{StaticResource FieldLabel}" />
              <Label Grid.Column="2" Text="{Binding EmployeeId}" Style="{StaticResource FieldValue}" />
            </Grid>
            <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

            <!-- Location -->
            <Grid ColumnDefinitions="36,*,Auto" Padding="4,12" ColumnSpacing="12">
              <Border Grid.Column="0" WidthRequest="32" HeightRequest="32"
                      StrokeShape="RoundRectangle 8" StrokeThickness="0"
                      BackgroundColor="{StaticResource Primary}"
                      HorizontalOptions="Center" VerticalOptions="Center">
                <Label Text="&#xE81D;" FontFamily="Segoe MDL2 Assets" FontSize="16"
                       TextColor="White"
                       HorizontalOptions="Center" VerticalOptions="Center" />
              </Border>
              <Label Grid.Column="1" Text="Location" Style="{StaticResource FieldLabel}" />
              <Label Grid.Column="2" Text="{Binding WorkLocation}" Style="{StaticResource FieldValue}" />
            </Grid>
            <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

            <!-- Face Verification -->
            <Grid ColumnDefinitions="36,*,Auto" Padding="4,12" ColumnSpacing="12">
              <Border Grid.Column="0" WidthRequest="32" HeightRequest="32"
                      StrokeShape="RoundRectangle 8" StrokeThickness="0"
                      BackgroundColor="{StaticResource Primary}"
                      HorizontalOptions="Center" VerticalOptions="Center">
                <Label Text="&#xE722;" FontFamily="Segoe MDL2 Assets" FontSize="16"
                       TextColor="White"
                       HorizontalOptions="Center" VerticalOptions="Center" />
              </Border>
              <Label Grid.Column="1" Text="Face Verification" Style="{StaticResource FieldLabel}" />

              <!-- Completed badge (green) or Pending (grey) -->
              <Border Grid.Column="2"
                      StrokeShape="RoundRectangle 12" StrokeThickness="0"
                      BackgroundColor="{Binding FaceVerificationCompleted, Converter={StaticResource BoolToColorConverter}}"
                      Padding="10,4" VerticalOptions="Center">
                <Label Text="{Binding FaceVerificationStatusText}"
                       FontSize="12" FontAttributes="Bold" TextColor="White"
                       VerticalOptions="Center" />
              </Border>
            </Grid>

          </VerticalStackLayout>
        </Border>

        <!-- Confirm & Continue button -->
        <Border Style="{StaticResource GradientButtonBorder}" Margin="0,0,0,14">
          <Button Text="Confirm &amp; Continue"
                  Command="{Binding ConfirmAndContinueCommand}"
                  Style="{StaticResource GradientButtonOverlay}" />
        </Border>

        <!-- Back text link -->
        <Button Text="Back"
                Command="{Binding BackCommand}"
                BackgroundColor="Transparent"
                TextColor="{StaticResource Primary}"
                FontSize="14"
                HorizontalOptions="Center" />

      </VerticalStackLayout>
    </ScrollView>
  </Grid>
</ContentPage>
```

### 3d — Add BoolToColorConverter for Face Verification badge

The XAML above uses `BoolToColorConverter` to color the badge green (Completed) or grey (Pending). Add it to `Converters/ValueConverters.cs`.

- [ ] **Step 4: Read current ValueConverters.cs**

Read `ONEVO.Agent.TrayApp/Converters/ValueConverters.cs` to see existing converters.

- [ ] **Step 5: Add BoolToColorConverter**

In `ValueConverters.cs`, add after the last existing converter:

```csharp
public sealed class BoolToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        return value is true
            ? Color.FromArgb("#22C55E")   // StatusGreen
            : Color.FromArgb("#9CA8B6");  // TextMuted
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotImplementedException();
}
```

- [ ] **Step 6: Register BoolToColorConverter in App.xaml or MergedDictionaries**

In `ONEVO.Agent.TrayApp/App.xaml`, add to the `ResourceDictionary`:

```xml
<converters:BoolToColorConverter x:Key="BoolToColorConverter" />
```

Check the existing `xmlns:converters` namespace declaration in `App.xaml` first to confirm the prefix.

- [ ] **Step 7: Build to verify**

```powershell
dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj --configuration Debug -f net10.0-windows10.0.19041.0
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 8: Commit**

```bash
git add ONEVO.Agent.TrayApp/Views/ReviewSetupPage.xaml \
        ONEVO.Agent.TrayApp/ViewModels/ReviewSetupViewModel.cs \
        ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs \
        ONEVO.Agent.TrayApp/Converters/ValueConverters.cs \
        ONEVO.Agent.TrayApp/App.xaml
git commit -m "feat(ui): redesign ReviewSetupPage, fix FaceVerification persistence"
```

---

## Task 4 — Full build, run, and end-to-end flow verification

**Why:** Verify the complete clock-in → face scan → running timer flow works end-to-end with both processes running.

**Files:** No code changes — this is the integration verification step.

- [ ] **Step 1: Kill any existing instances**

```bash
taskkill //F //IM ONEVO.Agent.TrayApp.exe 2>/dev/null
taskkill //F //IM ONEVO.Agent.Service.exe 2>/dev/null
```

- [ ] **Step 2: Build both projects**

```powershell
dotnet build ONEVO.Agent.Service/ONEVO.Agent.Service.csproj --configuration Debug
dotnet build ONEVO.Agent.TrayApp/ONEVO.Agent.TrayApp.csproj --configuration Debug -f net10.0-windows10.0.19041.0
```

Expected: Both succeed with 0 errors.

- [ ] **Step 3: Start the Service**

```bash
"ONEVO.Agent.Service/bin/Debug/net10.0-windows/ONEVO.Agent.Service.exe" &
sleep 2
```

- [ ] **Step 4: Start the TrayApp**

```bash
"ONEVO.Agent.TrayApp/bin/Debug/net10.0-windows10.0.19041.0/win-x64/ONEVO.Agent.TrayApp.exe" &
sleep 4
```

- [ ] **Step 5: Verify IPC connected in boot log**

```bash
tail -10 "$LOCALAPPDATA/ONEVO/Agent/tray-boot.log"
```

Expected: Lines showing `Policy received` and `State=Stopped` (or `Unenrolled`). No `IPC disconnected` after startup.

- [ ] **Step 6: Manual flow test — onboarding**

Walk through: ClockIn page → (if service is in dev mode: click Clock In) → observe face scan page appears → camera capture fires → verify boot log shows `snapshot` entries after clocking in.

- [ ] **Step 7: Verify timer runs after clock-in**

After successful clock-in, the ActiveSessionPage should show:
- `StartTimeDisplay` showing the clock-in time
- `PrimaryTimer` counting up from 00:00:00

Check that `WorkDurationDisplay` is counting up each second.

- [ ] **Step 8: Commit final verification**

```bash
git add -A
git commit -m "feat: complete clock-in face scan and UI polish — full flow verified"
```

---

## Known Constraints

- **MediaCapture camera permission**: On non-MSIX (unpackaged) builds, Windows may show a one-time "Allow this app to access your camera?" prompt. This is expected behavior.
- **Segoe MDL2 Assets**: Available on Windows 10 version 1507+ and all Windows 11 builds. No additional font files needed.
- **FaceVerificationCompleted reset**: When the user re-runs onboarding, `onevo.face_verified` should be cleared. Add `Preferences.Remove("onevo.face_verified")` at the start of the ConnectWorkspace/PrepareWorkspace flow if needed.
- **Clock-in face scan**: `CameraVerificationEnabled` in the local-default policy (service dev mode) is `false`, so the clock-in face scan only triggers if the service explicitly sets this flag in the real policy.
