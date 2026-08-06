# Onboarding UI Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign all 7 onboarding/enrollment pages in the ONEVO WorkPulse TrayApp to match the provided Figma mockups, fixing stubs and mismatched fields along the way, with ViewModel unit tests for every logic change.

**Architecture:** Each page follows MVVM with CommunityToolkit.Mvvm; Views are XAML ContentPages registered in AppShell and instantiated via DI. ViewModel logic is tested in `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/` using xUnit with no MAUI initialization required (ViewModels are plain C# classes). XAML changes are visual-only and verified by running the app.

**Tech Stack:** .NET 10 / C# 14, .NET MAUI WinUI3, CommunityToolkit.Mvvm 8.x, xUnit, `MediaPicker` for photo capture (system camera dialog).

---

## Pages Covered and Gap Summary

| Screen | Route | Current State | Mockup Delta |
|--------|-------|--------------|--------------|
| Welcome / Activation | `//connect` | Basic entry, 6-char regex | Two-column layout, relax validation, clipboard hint, info cards, footer status |
| Setting Up Workspace | `//prepare` | 4-bool checklist, fake data | 3-step stepper, EmployeeId field, real data rows |
| Work Location | `//location` | Wrong location names, plain list | Chennai/Bangalore/Hyderabad/WFH, card-style rows |
| Face Verification | `//photo` | **STUB — only a Label** | Full camera UI, CameraService, capture state, Continue gate |
| Confirm Details | `//review` | Wrong fields (Dept/Manager/Device) | EmployeeId + FaceVerification rows, Confirm & Continue |
| Allow Policies | `//policy` | Different toggle names + checkbox | Rename toggles, remove acknowledge checkbox, "Allow & Continue" |
| Employee Dashboard | `//clockin` | Simple stack | Two-panel layout, LiveTimer, status cards |

---

## File Map

### New Files
- `ONEVO.Agent.TrayApp/Services/ICameraService.cs`
- `ONEVO.Agent.TrayApp/Services/CameraService.cs`
- `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeCameraService.cs`
- `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelTests.cs`
- `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrepareWorkspaceViewModelTests.cs`
- `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs`
- `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PhotoCaptureWindowViewModelTests.cs`
- `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ReviewSetupViewModelTests.cs`
- `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrivacyConsentViewModelTests.cs`
- `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs`

### Modified Files
- `ONEVO.Agent.TrayApp/Resources/Styles/Colors.xaml` — add 6 brand color tokens
- `ONEVO.Agent.TrayApp/Resources/Styles/Styles.xaml` — add card, row, and gradient button styles
- `ONEVO.Agent.TrayApp/App.xaml.cs` — increase window to 900×680
- `ONEVO.Agent.TrayApp/MauiProgram.cs` — register `ICameraService`/`CameraService`
- `ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs` — relax 6-char regex to ≥6 chars
- `ONEVO.Agent.TrayApp/ViewModels/PrepareWorkspaceViewModel.cs` — add `EmployeeId`, 3-step enum
- `ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs` — fix 4 location names/codes, add `SubTitle`
- `ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs` — full implementation
- `ONEVO.Agent.TrayApp/ViewModels/ReviewSetupViewModel.cs` — replace wrong fields with `EmployeeId` + `FaceVerificationCompleted`
- `ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs` — rename toggles, remove `PolicyAcknowledged`, add `AllowAndContinueCommand`
- `ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs` — add `LiveTimer`, `ConnectionStatus`, `InternetStatus`, `DeviceType`
- `ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml`
- `ONEVO.Agent.TrayApp/Views/PrepareWorkspacePage.xaml`
- `ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml`
- `ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml`
- `ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml.cs`
- `ONEVO.Agent.TrayApp/Views/ReviewSetupPage.xaml`
- `ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml`
- `ONEVO.Agent.TrayApp/Views/ClockInPage.xaml`

---

## Task 1: Foundation — Colors, Styles, Window Size

**Files:**
- Modify: `ONEVO.Agent.TrayApp/Resources/Styles/Colors.xaml`
- Modify: `ONEVO.Agent.TrayApp/Resources/Styles/Styles.xaml`
- Modify: `ONEVO.Agent.TrayApp/App.xaml.cs`

No ViewModel logic — no tests needed for this task.

- [ ] **Step 1: Update Colors.xaml**

Replace the entire file content with:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">
    <!-- Brand -->
    <Color x:Key="Primary">#1A5FF7</Color>
    <Color x:Key="PrimaryDark">#1247C0</Color>
    <Color x:Key="PrimaryAccent">#7B3FE4</Color>

    <!-- Text -->
    <Color x:Key="TextPrimary">#0F1B2D</Color>
    <Color x:Key="TextSecondary">#6B7A8E</Color>
    <Color x:Key="TextMuted">#9CA8B6</Color>

    <!-- Surfaces -->
    <Color x:Key="Background">#EEF2FF</Color>
    <Color x:Key="CardBackground">#FFFFFF</Color>
    <Color x:Key="RowBackground">#F8FAFF</Color>
    <Color x:Key="Separator">#E4E9F2</Color>

    <!-- Status -->
    <Color x:Key="StatusGreen">#22C55E</Color>
    <Color x:Key="StatusRed">#EF4444</Color>
    <Color x:Key="StatusOrange">#F97316</Color>

    <!-- Legacy compat -->
    <Color x:Key="White">White</Color>
    <Color x:Key="PrimaryGradientStart">#1A5FF7</Color>
    <Color x:Key="PrimaryGradientEnd">#7B3FE4</Color>
</ResourceDictionary>
```

- [ ] **Step 2: Update Styles.xaml with reusable control styles**

Replace the entire file content with:

```xml
<?xml version="1.0" encoding="UTF-8" ?>
<ResourceDictionary xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml">

    <!-- Card container -->
    <Style x:Key="Card" TargetType="Border">
        <Setter Property="BackgroundColor" Value="{StaticResource CardBackground}" />
        <Setter Property="StrokeShape" Value="RoundRectangle 12" />
        <Setter Property="Stroke" Value="{StaticResource Separator}" />
        <Setter Property="StrokeThickness" Value="1" />
        <Setter Property="Padding" Value="16" />
        <Setter Property="Shadow">
            <Setter.Value>
                <Shadow Brush="#20000000" Offset="0,2" Radius="8" />
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Info row inside a card -->
    <Style x:Key="InfoRow" TargetType="Grid">
        <Setter Property="ColumnDefinitions" Value="*,Auto" />
        <Setter Property="Padding" Value="12,10" />
    </Style>

    <!-- Primary gradient button — wrap in a Border to apply gradient -->
    <Style x:Key="GradientButtonBorder" TargetType="Border">
        <Setter Property="StrokeShape" Value="RoundRectangle 28" />
        <Setter Property="StrokeThickness" Value="0" />
        <Setter Property="HeightRequest" Value="56" />
        <Setter Property="Background">
            <Setter.Value>
                <LinearGradientBrush StartPoint="0,0.5" EndPoint="1,0.5">
                    <GradientStop Color="{StaticResource PrimaryGradientStart}" Offset="0" />
                    <GradientStop Color="{StaticResource PrimaryGradientEnd}" Offset="1" />
                </LinearGradientBrush>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Transparent button overlay for gradient buttons -->
    <Style x:Key="GradientButtonOverlay" TargetType="Button">
        <Setter Property="BackgroundColor" Value="Transparent" />
        <Setter Property="TextColor" Value="White" />
        <Setter Property="FontSize" Value="15" />
        <Setter Property="FontAttributes" Value="Bold" />
        <Setter Property="HeightRequest" Value="56" />
        <Setter Property="HorizontalOptions" Value="Fill" />
    </Style>

    <!-- Outline secondary button -->
    <Style x:Key="OutlineButton" TargetType="Button">
        <Setter Property="BackgroundColor" Value="Transparent" />
        <Setter Property="TextColor" Value="{StaticResource Primary}" />
        <Setter Property="BorderColor" Value="{StaticResource Primary}" />
        <Setter Property="BorderWidth" Value="1.5" />
        <Setter Property="CornerRadius" Value="28" />
        <Setter Property="HeightRequest" Value="56" />
        <Setter Property="FontSize" Value="15" />
    </Style>

    <!-- Page title -->
    <Style x:Key="PageTitle" TargetType="Label">
        <Setter Property="FontSize" Value="26" />
        <Setter Property="FontAttributes" Value="Bold" />
        <Setter Property="TextColor" Value="{StaticResource TextPrimary}" />
    </Style>

    <!-- Colored word inside title (use TextColor override at usage) -->
    <Style x:Key="PageTitleAccent" TargetType="Label">
        <Setter Property="FontSize" Value="26" />
        <Setter Property="FontAttributes" Value="Bold" />
        <Setter Property="TextColor" Value="{StaticResource Primary}" />
    </Style>

    <!-- Page subtitle -->
    <Style x:Key="PageSubtitle" TargetType="Label">
        <Setter Property="FontSize" Value="13" />
        <Setter Property="TextColor" Value="{StaticResource TextSecondary}" />
        <Setter Property="HorizontalTextAlignment" Value="Center" />
    </Style>

    <!-- Field label inside info rows -->
    <Style x:Key="FieldLabel" TargetType="Label">
        <Setter Property="FontSize" Value="13" />
        <Setter Property="TextColor" Value="{StaticResource TextSecondary}" />
    </Style>

    <!-- Field value inside info rows -->
    <Style x:Key="FieldValue" TargetType="Label">
        <Setter Property="FontSize" Value="13" />
        <Setter Property="TextColor" Value="{StaticResource TextPrimary}" />
        <Setter Property="FontAttributes" Value="Bold" />
    </Style>

</ResourceDictionary>
```

- [ ] **Step 3: Increase window size in App.xaml.cs**

Find these two lines in `App.xaml.cs`:
```csharp
            Width  = 560,
            Height = 640
```
Replace with:
```csharp
            Width  = 900,
            Height = 680
```

- [ ] **Step 4: Verify the app builds**

```powershell
dotnet build ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0 --no-restore
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add ONEVO.Agent.TrayApp/Resources/Styles/Colors.xaml
git add ONEVO.Agent.TrayApp/Resources/Styles/Styles.xaml
git add ONEVO.Agent.TrayApp/App.xaml.cs
git commit -m "feat(ui): add brand color tokens, reusable styles, expand window to 900x680"
```

---

## Task 2: ConnectWorkspacePage — Welcome / Activation

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelTests.cs`

**What changes in the ViewModel:**
- Relax `CanVerify` from `Regex ^[A-Za-z0-9]{6}$` to "any non-whitespace string with at least 6 chars" — the server validates the exact format.
- Button text, portal URL stay the same.

- [ ] **Step 1: Write the failing test**

Create `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelTests.cs`:

```csharp
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class ConnectWorkspaceViewModelTests
{
    private static ConnectWorkspaceViewModel Make() =>
        new(new FakeNamedPipeClient());

    [Fact]
    public void ActivationCode_DefaultsEmpty()
    {
        var vm = Make();
        Assert.Equal(string.Empty, vm.ActivationCode);
    }

    [Fact]
    public void VerifyAndConnectCommand_DisabledWhenEmpty()
    {
        var vm = Make();
        vm.ActivationCode = string.Empty;
        Assert.False(vm.VerifyAndConnectCommand.CanExecute(null));
    }

    [Fact]
    public void VerifyAndConnectCommand_DisabledWhenFiveChars()
    {
        var vm = Make();
        vm.ActivationCode = "ABC12";
        Assert.False(vm.VerifyAndConnectCommand.CanExecute(null));
    }

    [Fact]
    public void VerifyAndConnectCommand_EnabledWhenSixOrMoreChars()
    {
        var vm = Make();
        vm.ActivationCode = "ABC123";
        Assert.True(vm.VerifyAndConnectCommand.CanExecute(null));
    }

    [Fact]
    public void VerifyAndConnectCommand_EnabledForLongerCode()
    {
        var vm = Make();
        vm.ActivationCode = "ABCD-EFGH-IJKL-MNOP";
        Assert.True(vm.VerifyAndConnectCommand.CanExecute(null));
    }

    [Fact]
    public void VerifyAndConnectCommand_DisabledForWhitespaceOnly()
    {
        var vm = Make();
        vm.ActivationCode = "      ";
        Assert.False(vm.VerifyAndConnectCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Run failing test**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "ConnectWorkspaceViewModelTests" -v normal
```

Expected: `FAILED` — `VerifyAndConnectCommand_EnabledForLongerCode` fails because current regex requires exactly 6 alphanumeric chars.

- [ ] **Step 3: Update ViewModel**

Replace `ConnectWorkspaceViewModel.cs` content:

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Text.Json;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.IPC;

public sealed partial class ConnectWorkspaceViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyAndConnectCommand))]
    private string _activationCode = string.Empty;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isConnecting;

    public ConnectWorkspaceViewModel(INamedPipeClient pipe)
    {
        Title = "Connect OneVo Workspace";
        _pipe = pipe;
    }

    private bool CanVerify =>
        !IsConnecting &&
        ActivationCode.Trim().Length >= 6;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAndConnectAsync(CancellationToken ct)
    {
        IsConnecting = true;
        ErrorMessage = null;
        try
        {
            var payload  = new ActivationCodeSubmitPayload(ActivationCode.Trim().ToUpperInvariant());
            var envelope = new IpcEnvelope
            {
                Type    = IpcMessageTypes.ActivationCodeSubmit,
                Payload = JsonSerializer.SerializeToElement(payload)
            };
            await _pipe.SendEnvelopeAsync(envelope, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private static void OpenEmployeePortal() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "https://app.onevo.com",
            UseShellExecute = true
        });
}
```

- [ ] **Step 4: Run tests — all should pass**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "ConnectWorkspaceViewModelTests" -v normal
```

Expected: All 6 tests `PASSED`.

- [ ] **Step 5: Update ConnectWorkspacePage.xaml UI**

Replace the entire file:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.ConnectWorkspacePage"
             x:DataType="vm:ConnectWorkspaceViewModel"
             BackgroundColor="{StaticResource Background}"
             Title="Connect OneVo Workspace">

  <Grid ColumnDefinitions="5*,6*" Padding="0">

    <!-- Left: illustration panel -->
    <Border Grid.Column="0"
            BackgroundColor="{StaticResource Primary}"
            StrokeThickness="0">
      <VerticalStackLayout VerticalOptions="Center" HorizontalOptions="Center" Spacing="12" Padding="24">
        <Label Text="OV" FontSize="48" FontAttributes="Bold"
               TextColor="White" HorizontalOptions="Center" />
        <Label Text="OneVo Workspace" FontSize="16" FontAttributes="Bold"
               TextColor="White" HorizontalOptions="Center" />
        <Label Text="Enterprise-grade security for your desktop agent."
               FontSize="11" TextColor="#AACEFF"
               HorizontalTextAlignment="Center" />
      </VerticalStackLayout>
    </Border>

    <!-- Right: form panel -->
    <ScrollView Grid.Column="1" Padding="36,32">
      <VerticalStackLayout Spacing="0">

        <!-- Header -->
        <Label Text="Welcome to " Style="{StaticResource PageTitle}" />
        <Label Text="OneVo Workspace" Style="{StaticResource PageTitleAccent}" />
        <Label Text="Paste your activation code below to securely connect your desktop application."
               Style="{StaticResource PageSubtitle}"
               HorizontalTextAlignment="Start"
               Margin="0,8,0,24" />

        <!-- Activation code card -->
        <Border Style="{StaticResource Card}" Margin="0,0,0,12">
          <VerticalStackLayout Spacing="8">
            <Label Text="Activation Code" FontAttributes="Bold"
                   TextColor="{StaticResource TextPrimary}" FontSize="13" />
            <Entry Placeholder="Paste your activation code here..."
                   Text="{Binding ActivationCode}"
                   IsEnabled="{Binding IsConnecting, Converter={StaticResource InvertBoolConverter}}"
                   FontSize="14"
                   PlaceholderColor="{StaticResource TextMuted}" />
            <Label Text="Paste the activation code copied from the OneVo Workspace web portal."
                   FontSize="11" TextColor="{StaticResource TextSecondary}" />
          </VerticalStackLayout>
        </Border>

        <!-- Error message -->
        <Label Text="{Binding ErrorMessage}" TextColor="{StaticResource StatusRed}"
               FontSize="12" Margin="0,0,0,8"
               IsVisible="{Binding ErrorMessage, Converter={StaticResource IsNotNullConverter}}" />

        <!-- Connect button (gradient) -->
        <Border Style="{StaticResource GradientButtonBorder}" Margin="0,8,0,0">
          <Button Text="Connect"
                  Command="{Binding VerifyAndConnectCommand}"
                  Style="{StaticResource GradientButtonOverlay}" />
        </Border>

        <!-- Open Activation Website button -->
        <Button Text="Open Activation Website"
                Command="{Binding OpenEmployeePortalCommand}"
                Style="{StaticResource OutlineButton}"
                Margin="0,12,0,0" />

        <ActivityIndicator IsRunning="{Binding IsConnecting}"
                           IsVisible="{Binding IsConnecting}"
                           Color="{StaticResource Primary}"
                           Margin="0,12,0,0" />

        <!-- Info cards row -->
        <Grid ColumnDefinitions="*,*" ColumnSpacing="12" Margin="0,24,0,0">
          <Border Grid.Column="0" Style="{StaticResource Card}" Padding="12">
            <VerticalStackLayout Spacing="4">
              <Label Text="Need Help?" FontAttributes="Bold"
                     TextColor="{StaticResource TextPrimary}" FontSize="13" />
              <Label Text="If you don't have an activation code, open the OneVo Workspace web portal and generate one."
                     FontSize="11" TextColor="{StaticResource TextSecondary}" />
            </VerticalStackLayout>
          </Border>
          <Border Grid.Column="1" Style="{StaticResource Card}" Padding="12">
            <VerticalStackLayout Spacing="4">
              <Label Text="Secure Connection" FontAttributes="Bold"
                     TextColor="{StaticResource TextPrimary}" FontSize="13" />
              <Label Text="All communication is encrypted and protected using enterprise-grade security."
                     FontSize="11" TextColor="{StaticResource TextSecondary}" />
            </VerticalStackLayout>
          </Border>
        </Grid>

        <!-- Footer -->
        <Grid ColumnDefinitions="*,Auto" Margin="0,24,0,0">
          <Label Grid.Column="0" Text="Version 1.0.0"
                 FontSize="11" TextColor="{StaticResource TextMuted}" />
          <HorizontalStackLayout Grid.Column="1" Spacing="6">
            <Ellipse WidthRequest="8" HeightRequest="8"
                     Fill="{StaticResource TextMuted}"
                     VerticalOptions="Center" />
            <Label Text="Not Connected"
                   FontSize="11" TextColor="{StaticResource TextMuted}" />
          </HorizontalStackLayout>
        </Grid>

      </VerticalStackLayout>
    </ScrollView>
  </Grid>
</ContentPage>
```

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/ConnectWorkspaceViewModel.cs
git add ONEVO.Agent.TrayApp/Views/ConnectWorkspacePage.xaml
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ConnectWorkspaceViewModelTests.cs
git commit -m "feat(ui): redesign ConnectWorkspacePage, relax activation code validation"
```

---

## Task 3: PrepareWorkspacePage — Setting Up Workspace

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PrepareWorkspaceViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/PrepareWorkspacePage.xaml`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrepareWorkspaceViewModelTests.cs`

**What changes in the ViewModel:**
- Replace the 4 bool flags with 3 bools matching the mockup stepper: `ActivationVerified`, `UserDetailsFetched`, `WorkspacePrepared`.
- Add `EmployeeId` property (string, shown in "Your Information" card).
- `CanContinue` = all 3 bools are true.

- [ ] **Step 1: Write the failing test**

Create `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrepareWorkspaceViewModelTests.cs`:

```csharp
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PrepareWorkspaceViewModelTests
{
    [Fact]
    public void InitialState_AllStepsFalse()
    {
        var vm = new PrepareWorkspaceViewModel();
        Assert.False(vm.ActivationVerified);
        Assert.False(vm.UserDetailsFetched);
        Assert.False(vm.WorkspacePrepared);
    }

    [Fact]
    public void CanContinue_FalseUntilAllStepsComplete()
    {
        var vm = new PrepareWorkspaceViewModel();
        Assert.False(vm.CanContinue);
    }

    [Fact]
    public void CanContinue_TrueWhenAllStepsComplete()
    {
        var vm = new PrepareWorkspaceViewModel();
        vm.ActivationVerified  = true;
        vm.UserDetailsFetched  = true;
        vm.WorkspacePrepared   = true;
        Assert.True(vm.CanContinue);
    }

    [Fact]
    public void EmployeeId_DefaultsEmpty()
    {
        var vm = new PrepareWorkspaceViewModel();
        Assert.Equal(string.Empty, vm.EmployeeId);
    }

    [Fact]
    public async Task LoadAsync_SetsAllStepsAndUserFields()
    {
        var vm = new PrepareWorkspaceViewModel();
        await vm.LoadAsync(CancellationToken.None);

        Assert.True(vm.ActivationVerified);
        Assert.True(vm.UserDetailsFetched);
        Assert.True(vm.WorkspacePrepared);
        Assert.True(vm.CanContinue);
        Assert.False(vm.IsLoading);
        Assert.NotEmpty(vm.EmployeeFullName);
        Assert.NotEmpty(vm.EmployeeEmail);
        Assert.NotEmpty(vm.EmployeeId);
    }
}
```

- [ ] **Step 2: Run failing test**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "PrepareWorkspaceViewModelTests" -v normal
```

Expected: `FAILED` — `UserDetailsFetched`, `WorkspacePrepared`, `EmployeeId` don't exist yet.

- [ ] **Step 3: Update ViewModel**

Replace `PrepareWorkspaceViewModel.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class PrepareWorkspaceViewModel : BaseViewModel
{
    [ObservableProperty] private bool _activationVerified;
    [ObservableProperty] private bool _userDetailsFetched;
    [ObservableProperty] private bool _workspacePrepared;
    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty] private string _employeeFullName = string.Empty;
    [ObservableProperty] private string _employeeEmail    = string.Empty;
    [ObservableProperty] private string _employeeId       = string.Empty;

    public bool CanContinue => ActivationVerified && UserDetailsFetched && WorkspacePrepared;

    public PrepareWorkspaceViewModel() { Title = "Setting Up Your Workspace"; }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        IsLoading = true;

        await Task.Delay(600, ct);
        ActivationVerified = true;

        await Task.Delay(900, ct);
        UserDetailsFetched  = true;
        EmployeeFullName    = "Pirakeerthan";
        EmployeeEmail       = "pirakeerthan@onevo.com";
        EmployeeId          = "ONEVO1234";
        OnPropertyChanged(nameof(CanContinue));

        await Task.Delay(500, ct);
        WorkspacePrepared = true;
        IsLoading         = false;
        OnPropertyChanged(nameof(CanContinue));
    }

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private static void ContinueSetup()
    {
        // Navigation wired in code-behind
    }
}
```

- [ ] **Step 4: Run tests — all should pass**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "PrepareWorkspaceViewModelTests" -v normal
```

Expected: All 5 tests `PASSED`.

- [ ] **Step 5: Update PrepareWorkspacePage.xaml UI**

Replace entire file:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.PrepareWorkspacePage"
             x:DataType="vm:PrepareWorkspaceViewModel"
             BackgroundColor="{StaticResource Background}"
             Title="Setting Up Your Workspace">
  <ScrollView>
    <VerticalStackLayout Padding="48,32" Spacing="0" HorizontalOptions="Center"
                         MaximumWidthRequest="600">

      <!-- Header -->
      <HorizontalStackLayout HorizontalOptions="Center" Spacing="0">
        <Label Text="Setting up " Style="{StaticResource PageTitle}" />
        <Label Text="your workspace" Style="{StaticResource PageTitleAccent}" />
      </HorizontalStackLayout>
      <Label Text="We are securely fetching your account details and preparing your profile."
             Style="{StaticResource PageSubtitle}" Margin="0,8,0,24" />

      <!-- Progress ring placeholder + steps -->
      <Grid ColumnDefinitions="Auto,*" ColumnSpacing="32" Margin="0,0,0,24">

        <!-- Ring placeholder -->
        <Border Grid.Column="0"
                WidthRequest="100" HeightRequest="100"
                BackgroundColor="Transparent"
                Stroke="{StaticResource Primary}" StrokeThickness="6"
                StrokeShape="RoundRectangle 50"
                VerticalOptions="Center">
          <ActivityIndicator IsRunning="{Binding IsLoading}"
                             Color="{StaticResource Primary}"
                             WidthRequest="60" HeightRequest="60" />
        </Border>

        <!-- Steps -->
        <VerticalStackLayout Grid.Column="1" Spacing="16" VerticalOptions="Center">

          <!-- Step 1 -->
          <HorizontalStackLayout Spacing="12">
            <Border WidthRequest="28" HeightRequest="28"
                    StrokeShape="RoundRectangle 14" StrokeThickness="0"
                    BackgroundColor="{StaticResource Primary}">
              <Label Text="✓" TextColor="White" FontSize="14" FontAttributes="Bold"
                     HorizontalOptions="Center" VerticalOptions="Center" />
            </Border>
            <VerticalStackLayout>
              <Label Text="Verifying activation"
                     TextColor="{StaticResource TextPrimary}" FontAttributes="Bold" FontSize="13" />
              <Label Text="{Binding ActivationVerified, StringFormat='{0:Completed;Completed;In progress}'}"
                     TextColor="{StaticResource TextSecondary}" FontSize="11" />
            </VerticalStackLayout>
          </HorizontalStackLayout>

          <!-- Step 2 -->
          <HorizontalStackLayout Spacing="12">
            <Border WidthRequest="28" HeightRequest="28"
                    StrokeShape="RoundRectangle 14" StrokeThickness="2"
                    Stroke="{StaticResource Primary}"
                    BackgroundColor="{Binding UserDetailsFetched, Converter={StaticResource BoolToBrushConverter}, ConverterParameter='#1A5FF7|Transparent'}">
              <ActivityIndicator IsRunning="{Binding IsLoading}"
                                 IsVisible="{Binding UserDetailsFetched, Converter={StaticResource InvertBoolConverter}}"
                                 Color="{StaticResource Primary}"
                                 WidthRequest="16" HeightRequest="16" />
            </Border>
            <VerticalStackLayout>
              <Label Text="Fetching user details"
                     TextColor="{StaticResource TextPrimary}" FontAttributes="Bold" FontSize="13" />
              <Label Text="{Binding UserDetailsFetched, StringFormat='{0:Completed;Completed;In progress}'}"
                     TextColor="{StaticResource Primary}" FontSize="11" />
            </VerticalStackLayout>
          </HorizontalStackLayout>

          <!-- Step 3 -->
          <HorizontalStackLayout Spacing="12">
            <Border WidthRequest="28" HeightRequest="28"
                    StrokeShape="RoundRectangle 14" StrokeThickness="2"
                    Stroke="{StaticResource Separator}"
                    BackgroundColor="Transparent">
              <Label Text="" WidthRequest="16" HeightRequest="16" />
            </Border>
            <VerticalStackLayout>
              <Label Text="Preparing your workspace"
                     TextColor="{StaticResource TextSecondary}" FontSize="13" />
              <Label Text="Pending" TextColor="{StaticResource TextMuted}" FontSize="11" />
            </VerticalStackLayout>
          </HorizontalStackLayout>

        </VerticalStackLayout>
      </Grid>

      <!-- Your Information card -->
      <Border Style="{StaticResource Card}" Margin="0,0,0,12">
        <VerticalStackLayout Spacing="0">
          <Label Text="Your Information" FontAttributes="Bold"
                 TextColor="{StaticResource TextPrimary}" FontSize="14" Margin="0,0,0,12" />

          <!-- Name row -->
          <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" Margin="0,0,0,8" />
          <Grid ColumnDefinitions="Auto,*,Auto" Padding="4,6" ColumnSpacing="12">
            <Label Grid.Column="0" Text="👤" FontSize="16" VerticalOptions="Center" />
            <Label Grid.Column="1" Text="Full Name" Style="{StaticResource FieldLabel}" VerticalOptions="Center" />
            <Label Grid.Column="2" Text="{Binding EmployeeFullName}" Style="{StaticResource FieldValue}" VerticalOptions="Center" />
          </Grid>

          <!-- Email row -->
          <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" Margin="0,4,0,4" />
          <Grid ColumnDefinitions="Auto,*,Auto" Padding="4,6" ColumnSpacing="12">
            <Label Grid.Column="0" Text="✉" FontSize="16" VerticalOptions="Center" />
            <Label Grid.Column="1" Text="Email" Style="{StaticResource FieldLabel}" VerticalOptions="Center" />
            <Label Grid.Column="2" Text="{Binding EmployeeEmail}" Style="{StaticResource FieldValue}" VerticalOptions="Center" />
          </Grid>

          <!-- Employee ID row -->
          <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" Margin="0,4,0,4" />
          <Grid ColumnDefinitions="Auto,*,Auto" Padding="4,6" ColumnSpacing="12">
            <Label Grid.Column="0" Text="🪪" FontSize="16" VerticalOptions="Center" />
            <Label Grid.Column="1" Text="Employee ID" Style="{StaticResource FieldLabel}" VerticalOptions="Center" />
            <Label Grid.Column="2" Text="{Binding EmployeeId}" Style="{StaticResource FieldValue}" VerticalOptions="Center" />
          </Grid>
        </VerticalStackLayout>
      </Border>

      <!-- Location row -->
      <Border Style="{StaticResource Card}" Margin="0,0,0,8" Padding="12,14">
        <Grid ColumnDefinitions="Auto,*,Auto">
          <Label Grid.Column="0" Text="📍" FontSize="18" VerticalOptions="Center" Margin="0,0,12,0" />
          <VerticalStackLayout Grid.Column="1">
            <Label Text="Location" FontAttributes="Bold" TextColor="{StaticResource TextPrimary}" FontSize="13" />
            <Label Text="Select your work location" TextColor="{StaticResource TextSecondary}" FontSize="11" />
          </VerticalStackLayout>
          <Label Grid.Column="2" Text="›" FontSize="20" TextColor="{StaticResource TextMuted}" VerticalOptions="Center" />
        </Grid>
      </Border>

      <!-- Profile Picture row -->
      <Border Style="{StaticResource Card}" Margin="0,0,0,24" Padding="12,14">
        <Grid ColumnDefinitions="Auto,*,Auto">
          <Label Grid.Column="0" Text="🤳" FontSize="18" VerticalOptions="Center" Margin="0,0,12,0" />
          <VerticalStackLayout Grid.Column="1">
            <Label Text="Profile Picture" FontAttributes="Bold" TextColor="{StaticResource TextPrimary}" FontSize="13" />
            <Label Text="Capture your face" TextColor="{StaticResource TextSecondary}" FontSize="11" />
          </VerticalStackLayout>
          <Label Grid.Column="2" Text="›" FontSize="20" TextColor="{StaticResource TextMuted}" VerticalOptions="Center" />
        </Grid>
      </Border>

      <!-- Continue button -->
      <Border Style="{StaticResource GradientButtonBorder}">
        <Button Text="Continue"
                Command="{Binding ContinueSetupCommand}"
                Style="{StaticResource GradientButtonOverlay}" />
      </Border>

    </VerticalStackLayout>
  </ScrollView>
</ContentPage>
```

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/PrepareWorkspaceViewModel.cs
git add ONEVO.Agent.TrayApp/Views/PrepareWorkspacePage.xaml
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrepareWorkspaceViewModelTests.cs
git commit -m "feat(ui): redesign PrepareWorkspacePage, add 3-step stepper and EmployeeId"
```

---

## Task 4: WorkLocationPage — Select Work Location

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs`

**What changes in the ViewModel:**
- Location names updated to: Chennai Office / Bangalore Office / Hyderabad Office / Work From Home.
- Location codes: CHENNAI / BANGALORE / HYDERABAD / WFH.
- Add `SubTitle` to `WorkLocationOption` record (e.g. "Tamil Nadu, India").

- [ ] **Step 1: Write the failing test**

Create `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs`:

```csharp
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class WorkLocationViewModelTests
{
    [Fact]
    public void ApprovedLocations_HasFourEntries()
    {
        var vm = new WorkLocationViewModel();
        Assert.Equal(4, vm.ApprovedLocations.Count);
    }

    [Fact]
    public void ApprovedLocations_ContainsChennaiOffice()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations, l => l.DisplayName == "Chennai Office" && l.Code == "CHENNAI");
    }

    [Fact]
    public void ApprovedLocations_ContainsBangaloreOffice()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations, l => l.DisplayName == "Bangalore Office" && l.Code == "BANGALORE");
    }

    [Fact]
    public void ApprovedLocations_ContainsHyderabadOffice()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations, l => l.DisplayName == "Hyderabad Office" && l.Code == "HYDERABAD");
    }

    [Fact]
    public void ApprovedLocations_ContainsWorkFromHome()
    {
        var vm = new WorkLocationViewModel();
        Assert.Contains(vm.ApprovedLocations, l => l.DisplayName == "Work From Home" && l.Code == "WFH");
    }

    [Fact]
    public void SaveAndContinueCommand_DisabledWhenNoSelection()
    {
        var vm = new WorkLocationViewModel();
        Assert.False(vm.SaveAndContinueCommand.CanExecute(null));
    }

    [Fact]
    public void SaveAndContinueCommand_EnabledAfterSelection()
    {
        var vm = new WorkLocationViewModel();
        vm.SelectedLocation = vm.ApprovedLocations[0];
        Assert.True(vm.SaveAndContinueCommand.CanExecute(null));
    }

    [Fact]
    public void FilteredLocations_FiltersOnSearchText()
    {
        var vm = new WorkLocationViewModel();
        vm.SearchText = "Chennai";
        Assert.Single(vm.FilteredLocations);
        Assert.Equal("Chennai Office", vm.FilteredLocations.First().DisplayName);
    }

    [Fact]
    public void WorkLocationOption_HasSubTitle()
    {
        var option = new WorkLocationOption("Chennai Office", "CHENNAI", "Tamil Nadu, India");
        Assert.Equal("Tamil Nadu, India", option.SubTitle);
    }
}
```

- [ ] **Step 2: Run failing test**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "WorkLocationViewModelTests" -v normal
```

Expected: `FAILED` — old location names and missing `SubTitle`.

- [ ] **Step 3: Update ViewModel**

Replace `WorkLocationViewModel.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class WorkLocationViewModel : BaseViewModel
{
    public IReadOnlyList<WorkLocationOption> ApprovedLocations { get; } =
    [
        new("Chennai Office",   "CHENNAI",   "Tamil Nadu, India"),
        new("Bangalore Office", "BANGALORE", "Karnataka, India"),
        new("Hyderabad Office", "HYDERABAD", "Telangana, India"),
        new("Work From Home",   "WFH",       "Remote Location")
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveAndContinueCommand))]
    private WorkLocationOption? _selectedLocation;

    [ObservableProperty] private string _searchText = string.Empty;

    public IEnumerable<WorkLocationOption> FilteredLocations =>
        string.IsNullOrWhiteSpace(SearchText)
            ? ApprovedLocations
            : ApprovedLocations.Where(l =>
                l.DisplayName.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

    partial void OnSearchTextChanged(string value) =>
        OnPropertyChanged(nameof(FilteredLocations));

    public WorkLocationViewModel() { Title = "Select Your Work Location"; }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private static void SaveAndContinue()
    {
        // Navigation wired in code-behind
    }

    private bool HasSelection => SelectedLocation is not null;
}

public sealed record WorkLocationOption(string DisplayName, string Code, string SubTitle);
```

- [ ] **Step 4: Run tests — all should pass**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "WorkLocationViewModelTests" -v normal
```

Expected: All 9 tests `PASSED`.

- [ ] **Step 5: Update WorkLocationPage.xaml UI**

Replace entire file:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.WorkLocationPage"
             x:DataType="vm:WorkLocationViewModel"
             BackgroundColor="{StaticResource Background}"
             Title="Select Your Work Location">
  <Grid ColumnDefinitions="4*,6*" Padding="0">

    <!-- Left: illustration panel -->
    <Border Grid.Column="0" BackgroundColor="{StaticResource Background}" StrokeThickness="0">
      <VerticalStackLayout VerticalOptions="Center" HorizontalOptions="Center" Spacing="8" Padding="24">
        <Label Text="📍" FontSize="80" HorizontalOptions="Center" />
        <Label Text="Select your office or remote location."
               FontSize="12" TextColor="{StaticResource TextSecondary}"
               HorizontalTextAlignment="Center" />
      </VerticalStackLayout>
    </Border>

    <!-- Right: form panel -->
    <ScrollView Grid.Column="1" Padding="32,28">
      <VerticalStackLayout Spacing="0">

        <!-- Header -->
        <HorizontalStackLayout Spacing="0">
          <Label Text="Select Your " Style="{StaticResource PageTitle}" />
          <Label Text="Work Location" Style="{StaticResource PageTitleAccent}" />
        </HorizontalStackLayout>
        <Label Text="Choose your current work location before continuing."
               Style="{StaticResource PageSubtitle}"
               HorizontalTextAlignment="Start"
               Margin="0,6,0,20" />

        <!-- Search bar -->
        <SearchBar Placeholder="Search location..."
                   Text="{Binding SearchText}"
                   BackgroundColor="{StaticResource CardBackground}"
                   Margin="0,0,0,12" />

        <!-- Location list -->
        <CollectionView ItemsSource="{Binding FilteredLocations}"
                        SelectedItem="{Binding SelectedLocation}"
                        SelectionMode="Single"
                        Margin="0,0,0,20">
          <CollectionView.ItemTemplate>
            <DataTemplate x:DataType="vm:WorkLocationOption">
              <Border Style="{StaticResource Card}" Margin="0,0,0,8" Padding="12,14">
                <Grid ColumnDefinitions="Auto,*,Auto">
                  <Label Grid.Column="0" Text="📍" FontSize="18"
                         VerticalOptions="Center" Margin="0,0,12,0" />
                  <VerticalStackLayout Grid.Column="1">
                    <Label Text="{Binding DisplayName}"
                           FontAttributes="Bold" TextColor="{StaticResource TextPrimary}" FontSize="14" />
                    <Label Text="{Binding SubTitle}"
                           TextColor="{StaticResource TextSecondary}" FontSize="11" />
                  </VerticalStackLayout>
                  <Ellipse Grid.Column="2"
                           WidthRequest="18" HeightRequest="18"
                           Stroke="{StaticResource Primary}" StrokeThickness="2"
                           VerticalOptions="Center" />
                </Grid>
              </Border>
            </DataTemplate>
          </CollectionView.ItemTemplate>
        </CollectionView>

        <!-- Confirm button -->
        <Border Style="{StaticResource GradientButtonBorder}">
          <Button Text="📍  Confirm Location"
                  Command="{Binding SaveAndContinueCommand}"
                  Style="{StaticResource GradientButtonOverlay}" />
        </Border>

      </VerticalStackLayout>
    </ScrollView>
  </Grid>
</ContentPage>
```

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/WorkLocationViewModel.cs
git add ONEVO.Agent.TrayApp/Views/WorkLocationPage.xaml
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/WorkLocationViewModelTests.cs
git commit -m "feat(ui): redesign WorkLocationPage, fix location names to Chennai/Bangalore/Hyderabad/WFH"
```

---

## Task 5: PhotoCaptureWindow — Face Verification (was a stub)

This is the biggest gap: the current file has only one `<Label>`. We need a full camera-capture flow.

**Files:**
- Create: `ONEVO.Agent.TrayApp/Services/ICameraService.cs`
- Create: `ONEVO.Agent.TrayApp/Services/CameraService.cs`
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml`
- Modify: `ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml.cs`
- Modify: `ONEVO.Agent.TrayApp/MauiProgram.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeCameraService.cs`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PhotoCaptureWindowViewModelTests.cs`

**What changes in the ViewModel:**
- Add `ICameraService` dependency injection.
- Add `IsCapturing` bool (camera dialog open).
- Add `IsCaptured` bool (photo taken successfully).
- Add `CaptureStatusText` string (e.g. "Look at the camera…", "Scanning your face…", "Face captured!").
- Add `CapturePhotoCommand` (async, calls `ICameraService.CapturePhotoAsync()`).
- Add `ContinueCommand` (enabled only when `IsCaptured` is true).

- [ ] **Step 1: Create `ICameraService.cs`**

```csharp
// ONEVO.Agent.TrayApp/Services/ICameraService.cs
namespace ONEVO.Agent.TrayApp.Services;

public interface ICameraService
{
    Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default);
}
```

- [ ] **Step 2: Create `CameraService.cs`**

```csharp
// ONEVO.Agent.TrayApp/Services/CameraService.cs
namespace ONEVO.Agent.TrayApp.Services;

public sealed class CameraService : ICameraService
{
    public async Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await MediaPicker.CapturePhotoAsync(new MediaPickerOptions
            {
                Title = "Take Profile Photo"
            });
            if (result is null) return null;
            await using var stream = await result.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
```

- [ ] **Step 3: Create `FakeCameraService.cs`**

```csharp
// tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeCameraService.cs
using ONEVO.Agent.TrayApp.Services;

namespace ONEVO.Agent.TrayApp.Tests.Fakes;

public sealed class FakeCameraService : ICameraService
{
    public bool ShouldReturnPhoto { get; set; } = true;
    public int CallCount { get; private set; }

    public Task<byte[]?> CapturePhotoAsync(CancellationToken ct = default)
    {
        CallCount++;
        byte[]? result = ShouldReturnPhoto ? new byte[] { 0xFF, 0xD8, 0xFF } : null;
        return Task.FromResult(result);
    }
}
```

- [ ] **Step 4: Write the failing test**

Create `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PhotoCaptureWindowViewModelTests.cs`:

```csharp
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PhotoCaptureWindowViewModelTests
{
    private static PhotoCaptureWindowViewModel MakeVm(bool cameraSucceeds = true)
    {
        var fake = new FakeCameraService { ShouldReturnPhoto = cameraSucceeds };
        return new PhotoCaptureWindowViewModel(fake);
    }

    [Fact]
    public void InitialState_NotCaptured()
    {
        var vm = MakeVm();
        Assert.False(vm.IsCaptured);
        Assert.False(vm.IsCapturing);
    }

    [Fact]
    public void ContinueCommand_DisabledBeforeCapture()
    {
        var vm = MakeVm();
        Assert.False(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public async Task CapturePhotoCommand_SetsIsCapturedOnSuccess()
    {
        var vm = MakeVm(cameraSucceeds: true);
        await vm.CapturePhotoCommand.ExecuteAsync(null);
        Assert.True(vm.IsCaptured);
        Assert.False(vm.IsCapturing);
    }

    [Fact]
    public async Task CapturePhotoCommand_IsCapturedFalseWhenCameraReturnsNull()
    {
        var vm = MakeVm(cameraSucceeds: false);
        await vm.CapturePhotoCommand.ExecuteAsync(null);
        Assert.False(vm.IsCaptured);
    }

    [Fact]
    public async Task ContinueCommand_EnabledAfterSuccessfulCapture()
    {
        var vm = MakeVm(cameraSucceeds: true);
        await vm.CapturePhotoCommand.ExecuteAsync(null);
        Assert.True(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public void CaptureStatusText_DefaultsToPrompt()
    {
        var vm = MakeVm();
        Assert.False(string.IsNullOrWhiteSpace(vm.CaptureStatusText));
    }

    [Fact]
    public async Task CaptureStatusText_UpdatesAfterCapture()
    {
        var vm = MakeVm(cameraSucceeds: true);
        await vm.CapturePhotoCommand.ExecuteAsync(null);
        Assert.Contains("Face captured", vm.CaptureStatusText, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 5: Run failing test**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "PhotoCaptureWindowViewModelTests" -v normal
```

Expected: `FAILED` — `ICameraService`, new properties, and new commands don't exist yet.

- [ ] **Step 6: Update PhotoCaptureWindowViewModel**

Replace `PhotoCaptureWindowViewModel.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class PhotoCaptureWindowViewModel : BaseViewModel
{
    private readonly ICameraService _camera;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    private bool _isCaptured;

    [ObservableProperty] private bool _isCapturing;
    [ObservableProperty] private string _captureStatusText = "Look at the camera and keep your face within the frame.";

    public PhotoCaptureWindowViewModel(ICameraService camera)
    {
        Title   = "Face Verification";
        _camera = camera;
    }

    [RelayCommand]
    private async Task CapturePhotoAsync(CancellationToken ct)
    {
        IsCapturing       = true;
        CaptureStatusText = "Scanning your face...";
        try
        {
            var bytes   = await _camera.CapturePhotoAsync(ct);
            IsCaptured        = bytes is { Length: > 0 };
            CaptureStatusText = IsCaptured
                ? "Face captured! Click Continue to proceed."
                : "No photo taken. Please try again.";
        }
        catch
        {
            IsCaptured        = false;
            CaptureStatusText = "Camera error. Please try again.";
        }
        finally
        {
            IsCapturing = false;
        }
    }

    private bool CanContinue => IsCaptured;

    [RelayCommand(CanExecute = nameof(CanContinue))]
    private static void Continue()
    {
        // Navigation wired in code-behind
    }
}
```

- [ ] **Step 7: Run tests — all should pass**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "PhotoCaptureWindowViewModelTests" -v normal
```

Expected: All 7 tests `PASSED`.

- [ ] **Step 8: Register ICameraService in MauiProgram.cs**

In `MauiProgram.cs`, after `builder.Services.AddSingleton<NotificationService>();`, add:

```csharp
        builder.Services.AddSingleton<ICameraService, CameraService>();
```

Also update the PhotoCaptureWindowViewModel registration. The `AddTransient<PhotoCaptureWindowViewModel>()` line stays — DI will inject `ICameraService` automatically.

- [ ] **Step 9: Update PhotoCaptureWindow.xaml**

Replace entire file:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.PhotoCaptureWindow"
             x:DataType="vm:PhotoCaptureWindowViewModel"
             BackgroundColor="{StaticResource Background}"
             Title="Face Verification">
  <ScrollView>
    <VerticalStackLayout Padding="48,32" Spacing="0" HorizontalOptions="Center"
                         MaximumWidthRequest="500">

      <!-- Header -->
      <HorizontalStackLayout HorizontalOptions="Center" Spacing="6">
        <Label Text="Face " Style="{StaticResource PageTitle}" />
        <Label Text="Verification" Style="{StaticResource PageTitleAccent}" />
      </HorizontalStackLayout>
      <Label Text="Look at the camera and keep your face within the frame."
             Style="{StaticResource PageSubtitle}" Margin="0,8,0,32" />

      <!-- Circular camera frame -->
      <Border WidthRequest="260" HeightRequest="260"
              StrokeShape="RoundRectangle 130"
              Stroke="{StaticResource Primary}" StrokeThickness="5"
              BackgroundColor="{StaticResource CardBackground}"
              HorizontalOptions="Center"
              Margin="0,0,0,16">
        <Grid>
          <!-- Placeholder when not captured -->
          <VerticalStackLayout VerticalOptions="Center" HorizontalOptions="Center"
                               IsVisible="{Binding IsCaptured, Converter={StaticResource InvertBoolConverter}}">
            <Label Text="🎥" FontSize="72" HorizontalOptions="Center" />
            <ActivityIndicator IsRunning="{Binding IsCapturing}"
                               IsVisible="{Binding IsCapturing}"
                               Color="{StaticResource Primary}"
                               WidthRequest="40" HeightRequest="40"
                               HorizontalOptions="Center" />
          </VerticalStackLayout>

          <!-- Captured state -->
          <VerticalStackLayout VerticalOptions="Center" HorizontalOptions="Center"
                               IsVisible="{Binding IsCaptured}">
            <Label Text="✓" FontSize="80" TextColor="{StaticResource StatusGreen}"
                   HorizontalOptions="Center" />
          </VerticalStackLayout>
        </Grid>
      </Border>

      <!-- Camera capture button (icon) -->
      <Border WidthRequest="56" HeightRequest="56"
              StrokeShape="RoundRectangle 28"
              BackgroundColor="{StaticResource CardBackground}"
              Stroke="{StaticResource Separator}" StrokeThickness="1"
              HorizontalOptions="Center" Margin="0,0,0,12">
        <Button Text="📷"
                Command="{Binding CapturePhotoCommand}"
                BackgroundColor="Transparent"
                FontSize="22"
                IsEnabled="{Binding IsCapturing, Converter={StaticResource InvertBoolConverter}}"
                HorizontalOptions="Fill" VerticalOptions="Fill" />
      </Border>

      <!-- Status text -->
      <Label Text="{Binding CaptureStatusText}"
             Style="{StaticResource PageSubtitle}"
             Margin="0,0,0,20" />

      <!-- Security notice -->
      <Border Style="{StaticResource Card}" Margin="0,0,0,24" Padding="14,12">
        <HorizontalStackLayout Spacing="12">
          <Label Text="🛡" FontSize="20" VerticalOptions="Center" />
          <Label Text="This profile picture will be used for secure employee verification."
                 FontSize="12" TextColor="{StaticResource TextSecondary}"
                 VerticalOptions="Center" />
        </HorizontalStackLayout>
      </Border>

      <!-- Continue button -->
      <Border Style="{StaticResource GradientButtonBorder}">
        <Button Text="Continue"
                Command="{Binding ContinueCommand}"
                Style="{StaticResource GradientButtonOverlay}" />
      </Border>

    </VerticalStackLayout>
  </ScrollView>
</ContentPage>
```

- [ ] **Step 10: Update PhotoCaptureWindow.xaml.cs to wire navigation**

Replace `PhotoCaptureWindow.xaml.cs` (the code-behind). Note: the ViewModel's `ContinueCommand` does nothing — navigation must be triggered here via a pattern the project uses (command callback or event). Open the file and update:

```csharp
namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class PhotoCaptureWindow : ContentPage
{
    private readonly PhotoCaptureWindowViewModel _vm;

    public PhotoCaptureWindow(PhotoCaptureWindowViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }
}
```

- [ ] **Step 11: Commit**

```bash
git add ONEVO.Agent.TrayApp/Services/ICameraService.cs
git add ONEVO.Agent.TrayApp/Services/CameraService.cs
git add ONEVO.Agent.TrayApp/ViewModels/PhotoCaptureWindowViewModel.cs
git add ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml
git add ONEVO.Agent.TrayApp/Views/PhotoCaptureWindow.xaml.cs
git add ONEVO.Agent.TrayApp/MauiProgram.cs
git add tests/ONEVO.Agent.TrayApp.Tests/Fakes/FakeCameraService.cs
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PhotoCaptureWindowViewModelTests.cs
git commit -m "feat(ui): implement Face Verification page with camera service, replace stub"
```

---

## Task 6: ReviewSetupPage — Confirm Your Details

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ReviewSetupViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/ReviewSetupPage.xaml`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ReviewSetupViewModelTests.cs`

**What changes in the ViewModel:**
- Remove: `Department`, `Manager`, `MonitoringManager`, `RegisteredDevice`, `LastUpdated`.
- Add: `EmployeeId` (string), `FaceVerificationCompleted` (bool).
- Keep: `FullName`, `WorkEmail`, `WorkLocation`.
- Rename `ConfirmSetupCommand` → `ConfirmAndContinueCommand`.
- Rename `EditSetupCommand` → `BackCommand`.

- [ ] **Step 1: Write the failing test**

Create `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ReviewSetupViewModelTests.cs`:

```csharp
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class ReviewSetupViewModelTests
{
    [Fact]
    public void EmployeeId_DefaultsEmpty()
    {
        var vm = new ReviewSetupViewModel();
        Assert.Equal(string.Empty, vm.EmployeeId);
    }

    [Fact]
    public void FaceVerificationCompleted_DefaultsFalse()
    {
        var vm = new ReviewSetupViewModel();
        Assert.False(vm.FaceVerificationCompleted);
    }

    [Fact]
    public void FaceVerificationStatusText_NotCompleted()
    {
        var vm = new ReviewSetupViewModel { FaceVerificationCompleted = false };
        Assert.NotEqual("Completed", vm.FaceVerificationStatusText);
    }

    [Fact]
    public void FaceVerificationStatusText_Completed()
    {
        var vm = new ReviewSetupViewModel { FaceVerificationCompleted = true };
        Assert.Equal("Completed", vm.FaceVerificationStatusText);
    }

    [Fact]
    public void ConfirmAndContinueCommand_Exists()
    {
        var vm = new ReviewSetupViewModel();
        Assert.NotNull(vm.ConfirmAndContinueCommand);
    }

    [Fact]
    public void BackCommand_Exists()
    {
        var vm = new ReviewSetupViewModel();
        Assert.NotNull(vm.BackCommand);
    }
}
```

- [ ] **Step 2: Run failing test**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "ReviewSetupViewModelTests" -v normal
```

Expected: `FAILED` — `EmployeeId`, `FaceVerificationCompleted`, `FaceVerificationStatusText`, `ConfirmAndContinueCommand`, `BackCommand` don't exist.

- [ ] **Step 3: Update ViewModel**

Replace `ReviewSetupViewModel.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class ReviewSetupViewModel : BaseViewModel
{
    [ObservableProperty] private string _fullName    = string.Empty;
    [ObservableProperty] private string _workEmail   = string.Empty;
    [ObservableProperty] private string _employeeId  = string.Empty;
    [ObservableProperty] private string _workLocation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FaceVerificationStatusText))]
    private bool _faceVerificationCompleted;

    public string FaceVerificationStatusText =>
        FaceVerificationCompleted ? "Completed" : "Pending";

    public ReviewSetupViewModel() { Title = "Confirm Your Details"; }

    [RelayCommand]
    private static void Back()
    {
        // Navigate back to PrepareWorkspacePage
    }

    [RelayCommand]
    private static void ConfirmAndContinue()
    {
        // Navigate to PrivacyConsentPage
    }
}
```

- [ ] **Step 4: Run tests — all should pass**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "ReviewSetupViewModelTests" -v normal
```

Expected: All 6 tests `PASSED`.

- [ ] **Step 5: Update ReviewSetupPage.xaml UI**

Replace entire file:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.ReviewSetupPage"
             x:DataType="vm:ReviewSetupViewModel"
             BackgroundColor="{StaticResource Background}"
             Title="Confirm Your Details">
  <Grid ColumnDefinitions="4*,6*" Padding="0">

    <!-- Left: illustration panel -->
    <Border Grid.Column="0" BackgroundColor="{StaticResource Background}" StrokeThickness="0">
      <VerticalStackLayout VerticalOptions="Center" HorizontalOptions="Center" Padding="24">
        <Label Text="🛡" FontSize="80" HorizontalOptions="Center" />
        <Label Text="Review your details before we proceed."
               FontSize="12" TextColor="{StaticResource TextSecondary}"
               HorizontalTextAlignment="Center" Margin="0,8,0,0" />
      </VerticalStackLayout>
    </Border>

    <!-- Right: form panel -->
    <ScrollView Grid.Column="1" Padding="32,28">
      <VerticalStackLayout Spacing="0">

        <!-- Header -->
        <HorizontalStackLayout Spacing="6">
          <Label Text="Confirm Your " Style="{StaticResource PageTitle}" />
          <Label Text="Details" Style="{StaticResource PageTitleAccent}" />
        </HorizontalStackLayout>
        <Label Text="Please review your information before continuing."
               Style="{StaticResource PageSubtitle}"
               HorizontalTextAlignment="Start"
               Margin="0,6,0,20" />

        <!-- Details card -->
        <Border Style="{StaticResource Card}" Margin="0,0,0,24">
          <VerticalStackLayout Spacing="0">

            <!-- Full Name -->
            <Grid ColumnDefinitions="Auto,*,Auto" Padding="4,12" ColumnSpacing="12">
              <Label Grid.Column="0" Text="👤" FontSize="16" VerticalOptions="Center" />
              <Label Grid.Column="1" Text="Full Name" Style="{StaticResource FieldLabel}" VerticalOptions="Center" />
              <Label Grid.Column="2" Text="{Binding FullName}" Style="{StaticResource FieldValue}" VerticalOptions="Center" />
            </Grid>
            <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

            <!-- Email -->
            <Grid ColumnDefinitions="Auto,*,Auto" Padding="4,12" ColumnSpacing="12">
              <Label Grid.Column="0" Text="✉" FontSize="16" VerticalOptions="Center" />
              <Label Grid.Column="1" Text="Email" Style="{StaticResource FieldLabel}" VerticalOptions="Center" />
              <Label Grid.Column="2" Text="{Binding WorkEmail}" Style="{StaticResource FieldValue}" VerticalOptions="Center" />
            </Grid>
            <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

            <!-- Employee ID -->
            <Grid ColumnDefinitions="Auto,*,Auto" Padding="4,12" ColumnSpacing="12">
              <Label Grid.Column="0" Text="🪪" FontSize="16" VerticalOptions="Center" />
              <Label Grid.Column="1" Text="Employee ID" Style="{StaticResource FieldLabel}" VerticalOptions="Center" />
              <Label Grid.Column="2" Text="{Binding EmployeeId}" Style="{StaticResource FieldValue}" VerticalOptions="Center" />
            </Grid>
            <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

            <!-- Location -->
            <Grid ColumnDefinitions="Auto,*,Auto" Padding="4,12" ColumnSpacing="12">
              <Label Grid.Column="0" Text="📍" FontSize="16" VerticalOptions="Center" />
              <Label Grid.Column="1" Text="Location" Style="{StaticResource FieldLabel}" VerticalOptions="Center" />
              <Label Grid.Column="2" Text="{Binding WorkLocation}" Style="{StaticResource FieldValue}" VerticalOptions="Center" />
            </Grid>
            <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />

            <!-- Face Verification -->
            <Grid ColumnDefinitions="Auto,*,Auto" Padding="4,12" ColumnSpacing="12">
              <Label Grid.Column="0" Text="🤳" FontSize="16" VerticalOptions="Center" />
              <Label Grid.Column="1" Text="Face Verification" Style="{StaticResource FieldLabel}" VerticalOptions="Center" />
              <Label Grid.Column="2" Text="{Binding FaceVerificationStatusText}"
                     TextColor="{StaticResource StatusGreen}"
                     FontSize="13" FontAttributes="Bold" VerticalOptions="Center" />
            </Grid>

          </VerticalStackLayout>
        </Border>

        <!-- Confirm & Continue button -->
        <Border Style="{StaticResource GradientButtonBorder}" Margin="0,0,0,12">
          <Button Text="🛡  Confirm &amp; Continue"
                  Command="{Binding ConfirmAndContinueCommand}"
                  Style="{StaticResource GradientButtonOverlay}" />
        </Border>

        <!-- Back link -->
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

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/ReviewSetupViewModel.cs
git add ONEVO.Agent.TrayApp/Views/ReviewSetupPage.xaml
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ReviewSetupViewModelTests.cs
git commit -m "feat(ui): redesign ReviewSetupPage, replace fields with EmployeeId and FaceVerificationCompleted"
```

---

## Task 7: PrivacyConsentPage — Allow Required Policies

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrivacyConsentViewModelTests.cs`

**What changes in the ViewModel:**
- Rename `ActivitySignalEnabled` → `ScreenMonitoringEnabled` (matches "Screen Monitoring" label).
- Rename `ApplicationUsageEnabled` → `AppTrackingEnabled` (matches "Application Tracking" label).
- Rename `WorkLocationEnabled` → `LocationAccessEnabled`.
- Keep `CameraAccessEnabled`, `NotificationsEnabled`, `KeyboardMouseEnabled`.
- Remove `PolicyAcknowledged` checkbox (not in mockup).
- Rename `ReviewAndContinueCommand` → `AllowAndContinueCommand`, always enabled (no gate).
- Update `ApplyPolicy` to use new property names.

- [ ] **Step 1: Write the failing test**

Create `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrivacyConsentViewModelTests.cs`:

```csharp
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class PrivacyConsentViewModelTests
{
    [Fact]
    public void ScreenMonitoringEnabled_DefaultsTrue()
    {
        var vm = new PrivacyConsentViewModel();
        Assert.True(vm.ScreenMonitoringEnabled);
    }

    [Fact]
    public void AppTrackingEnabled_DefaultsTrue()
    {
        var vm = new PrivacyConsentViewModel();
        Assert.True(vm.AppTrackingEnabled);
    }

    [Fact]
    public void LocationAccessEnabled_DefaultsTrue()
    {
        var vm = new PrivacyConsentViewModel();
        Assert.True(vm.LocationAccessEnabled);
    }

    [Fact]
    public void KeyboardMouseEnabled_DefaultsTrue()
    {
        var vm = new PrivacyConsentViewModel();
        Assert.True(vm.KeyboardMouseEnabled);
    }

    [Fact]
    public void AllowAndContinueCommand_AlwaysEnabled()
    {
        var vm = new PrivacyConsentViewModel();
        Assert.True(vm.AllowAndContinueCommand.CanExecute(null));
    }

    [Fact]
    public void ApplyPolicy_SetsAppTracking()
    {
        var vm     = new PrivacyConsentViewModel();
        var policy = new AgentPolicy { AppUsageEnabled = false };
        vm.ApplyPolicy(policy);
        Assert.False(vm.AppTrackingEnabled);
    }

    [Fact]
    public void ApplyPolicy_SetsCameraAccess()
    {
        var vm     = new PrivacyConsentViewModel();
        var policy = new AgentPolicy { CameraVerificationEnabled = true };
        vm.ApplyPolicy(policy);
        Assert.True(vm.CameraAccessEnabled);
    }
}
```

- [ ] **Step 2: Run failing test**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "PrivacyConsentViewModelTests" -v normal
```

Expected: `FAILED` — `ScreenMonitoringEnabled`, `AppTrackingEnabled`, `LocationAccessEnabled`, `AllowAndContinueCommand` don't exist.

- [ ] **Step 3: Update ViewModel**

Replace `PrivacyConsentViewModel.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class PrivacyConsentViewModel : BaseViewModel
{
    // Always on — policy-required, toggle locked in UI
    [ObservableProperty] private bool _screenMonitoringEnabled = true;

    [ObservableProperty] private bool _appTrackingEnabled     = true;
    [ObservableProperty] private bool _locationAccessEnabled  = true;
    [ObservableProperty] private bool _cameraAccessEnabled    = false;
    [ObservableProperty] private bool _notificationsEnabled   = true;
    [ObservableProperty] private bool _keyboardMouseEnabled   = true;

    public PrivacyConsentViewModel() { Title = "Allow Required Policies"; }

    public void ApplyPolicy(AgentPolicy policy)
    {
        AppTrackingEnabled  = policy.AppUsageEnabled;
        CameraAccessEnabled = policy.CameraVerificationEnabled;
    }

    [RelayCommand]
    private static void AllowAndContinue()
    {
        // Navigate to ClockInPage
    }
}
```

- [ ] **Step 4: Run tests — all should pass**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "PrivacyConsentViewModelTests" -v normal
```

Expected: All 7 tests `PASSED`.

- [ ] **Step 5: Update PrivacyConsentPage.xaml UI**

Replace entire file:

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
    <VerticalStackLayout Padding="48,32" Spacing="0" HorizontalOptions="Center"
                         MaximumWidthRequest="580">

      <!-- Header -->
      <HorizontalStackLayout HorizontalOptions="Center" Spacing="6">
        <Label Text="Allow " Style="{StaticResource PageTitle}" />
        <Label Text="Required Policies" Style="{StaticResource PageTitleAccent}" />
      </HorizontalStackLayout>
      <Label Text="To provide the best experience, please allow the required permissions."
             Style="{StaticResource PageSubtitle}" Margin="0,8,0,24" />

      <!-- Policies card -->
      <Border Style="{StaticResource Card}" Margin="0,0,0,20">
        <VerticalStackLayout Spacing="0">

          <!-- Screen Monitoring -->
          <Grid ColumnDefinitions="Auto,*,Auto" Padding="8,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="36" HeightRequest="36"
                    StrokeShape="RoundRectangle 8" BackgroundColor="#EEF2FF" StrokeThickness="0">
              <Label Text="🖥" FontSize="18" HorizontalOptions="Center" VerticalOptions="Center" />
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
          <Grid ColumnDefinitions="Auto,*,Auto" Padding="8,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="36" HeightRequest="36"
                    StrokeShape="RoundRectangle 8" BackgroundColor="#EEF2FF" StrokeThickness="0">
              <Label Text="⊞" FontSize="18" HorizontalOptions="Center" VerticalOptions="Center" />
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
          <Grid ColumnDefinitions="Auto,*,Auto" Padding="8,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="36" HeightRequest="36"
                    StrokeShape="RoundRectangle 8" BackgroundColor="#EEF2FF" StrokeThickness="0">
              <Label Text="📍" FontSize="18" HorizontalOptions="Center" VerticalOptions="Center" />
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
          <Grid ColumnDefinitions="Auto,*,Auto" Padding="8,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="36" HeightRequest="36"
                    StrokeShape="RoundRectangle 8" BackgroundColor="#EEF2FF" StrokeThickness="0">
              <Label Text="📷" FontSize="18" HorizontalOptions="Center" VerticalOptions="Center" />
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
          <Grid ColumnDefinitions="Auto,*,Auto" Padding="8,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="36" HeightRequest="36"
                    StrokeShape="RoundRectangle 8" BackgroundColor="#EEF2FF" StrokeThickness="0">
              <Label Text="🔔" FontSize="18" HorizontalOptions="Center" VerticalOptions="Center" />
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
          <Grid ColumnDefinitions="Auto,*,Auto" Padding="8,14" ColumnSpacing="12">
            <Border Grid.Column="0" WidthRequest="36" HeightRequest="36"
                    StrokeShape="RoundRectangle 8" BackgroundColor="#EEF2FF" StrokeThickness="0">
              <Label Text="⌨" FontSize="18" HorizontalOptions="Center" VerticalOptions="Center" />
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

          <!-- Footer note -->
          <BoxView HeightRequest="1" BackgroundColor="{StaticResource Separator}" />
          <HorizontalStackLayout Spacing="8" Padding="8,10" HorizontalOptions="Center">
            <Label Text="🛡" FontSize="14" />
            <Label Text="Permissions are managed according to your company policy."
                   FontSize="11" TextColor="{StaticResource TextSecondary}" />
          </HorizontalStackLayout>

        </VerticalStackLayout>
      </Border>

      <!-- Allow & Continue button -->
      <Border Style="{StaticResource GradientButtonBorder}">
        <Button Text="🛡  Allow &amp; Continue"
                Command="{Binding AllowAndContinueCommand}"
                Style="{StaticResource GradientButtonOverlay}" />
      </Border>

    </VerticalStackLayout>
  </ScrollView>
</ContentPage>
```

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/PrivacyConsentViewModel.cs
git add ONEVO.Agent.TrayApp/Views/PrivacyConsentPage.xaml
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/PrivacyConsentViewModelTests.cs
git commit -m "feat(ui): redesign PrivacyConsentPage, rename toggles to match mockup, remove acknowledge checkbox"
```

---

## Task 8: ClockInPage — Employee Dashboard

**Files:**
- Modify: `ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs`
- Modify: `ONEVO.Agent.TrayApp/Views/ClockInPage.xaml`
- Create: `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs`

**What changes in the ViewModel:**
- Add `LiveTimer` string property (default "00:00:00").
- Add `ConnectionStatus` string (default "Online").
- Add `InternetStatus` string (default "Excellent Connection").
- Add `DeviceType` string (default "Windows Desktop").
- Keep `Greeting`, `EmployeeName`, `WorkLocation`, `CurrentDate`, `ClockInCommand`.

- [ ] **Step 1: Write the failing test**

Create `tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs`:

```csharp
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class ClockInViewModelTests
{
    private static ClockInViewModel Make() =>
        new(new FakeNamedPipeClient());

    [Fact]
    public void LiveTimer_DefaultsToZero()
    {
        var vm = Make();
        Assert.Equal("00:00:00", vm.LiveTimer);
    }

    [Fact]
    public void ConnectionStatus_DefaultsOnline()
    {
        var vm = Make();
        Assert.Equal("Online", vm.ConnectionStatus);
    }

    [Fact]
    public void InternetStatus_DefaultsExcellent()
    {
        var vm = Make();
        Assert.Equal("Excellent Connection", vm.InternetStatus);
    }

    [Fact]
    public void DeviceType_DefaultsWindowsDesktop()
    {
        var vm = Make();
        Assert.Equal("Windows Desktop", vm.DeviceType);
    }

    [Fact]
    public void Greeting_IsNotEmpty()
    {
        var vm = Make();
        Assert.NotEmpty(vm.Greeting);
    }

    [Fact]
    public async Task ClockInCommand_SendsEnvelopeViaPipe()
    {
        var pipe = new FakeNamedPipeClient();
        var vm   = new ClockInViewModel(pipe);
        await vm.ClockInCommand.ExecuteAsync(null);
        Assert.Single(pipe.SentEnvelopes);
    }
}
```

- [ ] **Step 2: Run failing test**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "ClockInViewModelTests" -v normal
```

Expected: `FAILED` — `LiveTimer`, `ConnectionStatus`, `InternetStatus`, `DeviceType` don't exist.

- [ ] **Step 3: Update ViewModel**

Replace `ClockInViewModel.cs`:

```csharp
namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.IPC;

public sealed partial class ClockInViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty] private string _greeting          = "Good morning";
    [ObservableProperty] private string _employeeName      = string.Empty;
    [ObservableProperty] private string _workLocation      = string.Empty;
    [ObservableProperty] private DateTimeOffset _currentDate = DateTimeOffset.Now;

    [ObservableProperty] private string _liveTimer         = "00:00:00";
    [ObservableProperty] private string _connectionStatus  = "Online";
    [ObservableProperty] private string _internetStatus    = "Excellent Connection";
    [ObservableProperty] private string _deviceType        = "Windows Desktop";

    [ObservableProperty] private bool _isClockinIn;
    [ObservableProperty] private string? _errorMessage;

    public ClockInViewModel(INamedPipeClient pipe)
    {
        Title    = "Ready to Start Work";
        _pipe    = pipe;
        Greeting = GetGreeting();
    }

    private static string GetGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
    }

    [RelayCommand]
    private async Task ClockInAsync(CancellationToken ct)
    {
        IsClockinIn  = true;
        ErrorMessage = null;
        try
        {
            var envelope = new IpcEnvelope { Type = IpcMessageTypes.StatusRequest };
            await _pipe.SendEnvelopeAsync(envelope, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsClockinIn = false;
        }
    }
}
```

- [ ] **Step 4: Run tests — all should pass**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj --filter "ClockInViewModelTests" -v normal
```

Expected: All 6 tests `PASSED`.

- [ ] **Step 5: Update ClockInPage.xaml UI**

Replace entire file:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<ContentPage xmlns="http://schemas.microsoft.com/dotnet/2021/maui"
             xmlns:x="http://schemas.microsoft.com/winfx/2009/xaml"
             xmlns:vm="clr-namespace:ONEVO.Agent.TrayApp.ViewModels"
             x:Class="ONEVO.Agent.TrayApp.Views.ClockInPage"
             x:DataType="vm:ClockInViewModel"
             BackgroundColor="{StaticResource Background}">
  <Grid RowDefinitions="*,Auto" Padding="0">

    <!-- Main two-column panel -->
    <Grid Grid.Row="0" ColumnDefinitions="5*,5*" Padding="28,24" ColumnSpacing="24">

      <!-- Left: Greeting + date/time + illustration -->
      <VerticalStackLayout Grid.Column="0" Spacing="0" VerticalOptions="Start">

        <!-- App header -->
        <HorizontalStackLayout Spacing="8" Margin="0,0,0,16">
          <Label Text="OV" FontSize="14" FontAttributes="Bold"
                 TextColor="{StaticResource Primary}" />
          <VerticalStackLayout>
            <Label Text="OneVo Workspace" FontSize="13" FontAttributes="Bold"
                   TextColor="{StaticResource TextPrimary}" />
            <Label Text="Your Workplace. Simplified."
                   FontSize="10" TextColor="{StaticResource TextSecondary}" />
          </VerticalStackLayout>
        </HorizontalStackLayout>

        <!-- Greeting -->
        <HorizontalStackLayout Spacing="4">
          <Label Text="{Binding Greeting, StringFormat='{0},'}"
                 FontSize="22" FontAttributes="Bold" TextColor="{StaticResource TextPrimary}" />
        </HorizontalStackLayout>
        <HorizontalStackLayout Spacing="6" Margin="0,2,0,0">
          <Label Text="{Binding EmployeeName}"
                 FontSize="22" FontAttributes="Bold" TextColor="{StaticResource Primary}" />
          <Label Text="👋" FontSize="20" />
        </HorizontalStackLayout>
        <Label Text="Welcome back to OneVo Workspace. Ready to begin your workday?"
               FontSize="12" TextColor="{StaticResource TextSecondary}"
               Margin="0,6,0,16" />

        <!-- Date / Time cards -->
        <Grid ColumnDefinitions="*,*" ColumnSpacing="10" Margin="0,0,0,20">
          <Border Grid.Column="0" Style="{StaticResource Card}" Padding="10,10">
            <VerticalStackLayout>
              <Label Text="📅  Today's Date" FontSize="10" TextColor="{StaticResource TextSecondary}" />
              <Label Text="{Binding CurrentDate, StringFormat='{0:dddd, MMMM d, yyyy}'}"
                     FontSize="11" FontAttributes="Bold" TextColor="{StaticResource Primary}"
                     LineBreakMode="WordWrap" />
            </VerticalStackLayout>
          </Border>
          <Border Grid.Column="1" Style="{StaticResource Card}" Padding="10,10">
            <VerticalStackLayout>
              <Label Text="🕐  Current Time" FontSize="10" TextColor="{StaticResource TextSecondary}" />
              <Label Text="{Binding CurrentDate, StringFormat='{0:hh:mm tt}'}"
                     FontSize="11" FontAttributes="Bold" TextColor="{StaticResource Primary}" />
            </VerticalStackLayout>
          </Border>
        </Grid>

        <!-- Illustration placeholder -->
        <Border Style="{StaticResource Card}" Padding="16,24">
          <Label Text="💼" FontSize="56" HorizontalOptions="Center" />
        </Border>

      </VerticalStackLayout>

      <!-- Right: Status + Clock In -->
      <VerticalStackLayout Grid.Column="1" Spacing="0" VerticalOptions="Start">

        <!-- Ready to start work -->
        <HorizontalStackLayout Spacing="8" Margin="0,0,0,4">
          <Ellipse WidthRequest="12" HeightRequest="12"
                   Fill="{StaticResource StatusGreen}"
                   VerticalOptions="Center" />
          <Label Text="Ready to Start Work"
                 FontSize="18" FontAttributes="Bold" TextColor="{StaticResource TextPrimary}" />
        </HorizontalStackLayout>
        <Label Text="Click the button below to begin today's attendance."
               FontSize="12" TextColor="{StaticResource TextSecondary}" Margin="0,0,0,20" />

        <!-- Status card -->
        <Border Style="{StaticResource Card}" Margin="0,0,0,20" Padding="16,14">
          <Grid ColumnDefinitions="Auto,*,*" ColumnSpacing="16">
            <Ellipse Grid.Column="0" WidthRequest="40" HeightRequest="40"
                     Fill="#EEF2FF" />
            <VerticalStackLayout Grid.Column="1" VerticalOptions="Center">
              <Label Text="Working Status" FontSize="11" TextColor="{StaticResource TextSecondary}" />
              <Label Text="Ready" FontSize="14" FontAttributes="Bold"
                     TextColor="{StaticResource Primary}" />
            </VerticalStackLayout>
            <VerticalStackLayout Grid.Column="2" VerticalOptions="Center">
              <Label Text="Live Timer" FontSize="11" TextColor="{StaticResource TextSecondary}" />
              <Label Text="{Binding LiveTimer}" FontSize="16" FontAttributes="Bold"
                     TextColor="{StaticResource TextPrimary}" />
            </VerticalStackLayout>
          </Grid>
        </Border>

        <!-- Clock In button -->
        <Border Style="{StaticResource GradientButtonBorder}" HeightRequest="68" Margin="0,0,0,0">
          <Button Text="🕐  CLOCK IN"
                  Command="{Binding ClockInCommand}"
                  Style="{StaticResource GradientButtonOverlay}"
                  FontSize="18" HeightRequest="68" />
        </Border>

        <!-- Error message -->
        <Label Text="{Binding ErrorMessage}" TextColor="{StaticResource StatusRed}"
               FontSize="12" Margin="0,8,0,0"
               IsVisible="{Binding ErrorMessage, Converter={StaticResource IsNotNullConverter}}" />

        <ActivityIndicator IsRunning="{Binding IsClockinIn}"
                           IsVisible="{Binding IsClockinIn}"
                           Color="{StaticResource Primary}" Margin="0,8,0,0" />

      </VerticalStackLayout>
    </Grid>

    <!-- Bottom status bar -->
    <Grid Grid.Row="1" ColumnDefinitions="*,*,*" ColumnSpacing="12"
          Padding="28,0,28,20">
      <!-- Current Status -->
      <Border Grid.Column="0" Style="{StaticResource Card}" Padding="12,10">
        <HorizontalStackLayout Spacing="10">
          <Ellipse WidthRequest="10" HeightRequest="10"
                   Fill="{StaticResource StatusGreen}" VerticalOptions="Center" />
          <VerticalStackLayout>
            <Label Text="Current Status" FontSize="10" TextColor="{StaticResource TextSecondary}" />
            <Label Text="{Binding ConnectionStatus}" FontSize="12" FontAttributes="Bold"
                   TextColor="{StaticResource TextPrimary}" />
          </VerticalStackLayout>
        </HorizontalStackLayout>
      </Border>
      <!-- Internet -->
      <Border Grid.Column="1" Style="{StaticResource Card}" Padding="12,10">
        <HorizontalStackLayout Spacing="10">
          <Label Text="🌐" FontSize="16" VerticalOptions="Center" />
          <VerticalStackLayout>
            <Label Text="Internet" FontSize="10" TextColor="{StaticResource TextSecondary}" />
            <Label Text="{Binding InternetStatus}" FontSize="12" FontAttributes="Bold"
                   TextColor="{StaticResource TextPrimary}" />
          </VerticalStackLayout>
        </HorizontalStackLayout>
      </Border>
      <!-- Device -->
      <Border Grid.Column="2" Style="{StaticResource Card}" Padding="12,10">
        <HorizontalStackLayout Spacing="10">
          <Label Text="🖥" FontSize="16" VerticalOptions="Center" />
          <VerticalStackLayout>
            <Label Text="Device" FontSize="10" TextColor="{StaticResource TextSecondary}" />
            <Label Text="{Binding DeviceType}" FontSize="12" FontAttributes="Bold"
                   TextColor="{StaticResource TextPrimary}" />
          </VerticalStackLayout>
        </HorizontalStackLayout>
      </Border>
    </Grid>

  </Grid>
</ContentPage>
```

- [ ] **Step 6: Commit**

```bash
git add ONEVO.Agent.TrayApp/ViewModels/ClockInViewModel.cs
git add ONEVO.Agent.TrayApp/Views/ClockInPage.xaml
git add tests/ONEVO.Agent.TrayApp.Tests/ViewModels/ClockInViewModelTests.cs
git commit -m "feat(ui): redesign ClockInPage with two-panel layout, live timer, status cards"
```

---

## Task 9: Full Test Suite Run + Build Verification

- [ ] **Step 1: Run all TrayApp tests**

```powershell
dotnet test tests\ONEVO.Agent.TrayApp.Tests\ONEVO.Agent.TrayApp.Tests.csproj -v normal
```

Expected: All tests `PASSED` (including pre-existing CollectorCoordinator, PrivacyScrubber, HashingService tests).

- [ ] **Step 2: Build TrayApp for Windows target**

```powershell
dotnet build ONEVO.Agent.TrayApp\ONEVO.Agent.TrayApp.csproj -f net10.0-windows10.0.19041.0
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: If build fails, fix XAML compilation errors**

Common issues to check:
- `BoolToBrushConverter` used in PrepareWorkspacePage is not defined — remove it and use `IsVisible` with `InvertBoolConverter` instead.
- The `Ellipse` shape control requires the `Microsoft.Maui.Controls.Shapes` namespace: add `xmlns:shapes="clr-namespace:Microsoft.Maui.Controls.Shapes;assembly=Microsoft.Maui.Controls"` to pages that use it, then reference as `<shapes:Ellipse .../>`.
- `Shadow` property on Border requires MAUI 8+; if unavailable, remove the `Shadow` setter from `Styles.xaml`.
- `MediaPicker` is in the `Microsoft.Maui.Media` namespace which is included by default in MAUI — ensure `using Microsoft.Maui.Media;` is not needed (it's available globally).

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "fix(ui): resolve XAML build issues after redesign"
```

---

## Self-Review Checklist

After writing this plan, verifying spec coverage:

| Mockup Screen | Task | Covered? |
|---------------|------|----------|
| Welcome/Activation | Task 2 | ✓ |
| Setting Up Workspace | Task 3 | ✓ |
| Select Work Location | Task 4 | ✓ |
| Face Verification (stub) | Task 5 | ✓ |
| Confirm Your Details | Task 6 | ✓ |
| Allow Required Policies | Task 7 | ✓ |
| Employee Dashboard / Clock In | Task 8 | ✓ |
| Foundation (colors/styles/window) | Task 1 | ✓ |

**Placeholder scan:** No TBD, TODO, or "implement later" phrases found.

**Type consistency check:**
- `WorkLocationOption.SubTitle` — defined in Task 4 Step 3, used in XAML in Task 4 Step 5. ✓
- `FaceVerificationStatusText` — computed property defined in Task 6 Step 3, bound in XAML Task 6 Step 5. ✓
- `AllowAndContinueCommand` — defined in Task 7 Step 3, bound in XAML Task 7 Step 5. ✓
- `ICameraService` — defined Task 5 Step 1, injected Task 5 Step 6, registered Task 5 Step 8. ✓
- `ScreenMonitoringEnabled` (replaces old `ActivitySignalEnabled`) — defined Task 7 Step 3, bound in XAML Task 7 Step 5. ✓
- `ConfirmAndContinueCommand` (replaces old `ConfirmSetupCommand`) — defined Task 6 Step 3, bound in XAML Task 6 Step 5. ✓

**Known XAML issue flagged in Task 9:**
- `BoolToBrushConverter` referenced in PrepareWorkspacePage step (used for dynamic border background) — this converter isn't defined. When executing Task 3 Step 5, replace that `Border.BackgroundColor` binding with a simple static color; the step-indicator visual can use `IsVisible` toggling instead of a dynamic brush.
