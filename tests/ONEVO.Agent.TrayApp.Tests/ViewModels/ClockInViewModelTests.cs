using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.Shared.Models;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class ClockInViewModelTests
{
    private static ClockInViewModel Make(FakeNamedPipeClient? pipe = null, IPreferencesStore? preferences = null) =>
        new(pipe ?? new FakeNamedPipeClient(), preferences ?? new FakePreferencesStore());

    // AUD-011: the Service refuses sign-out while clocked in (e.g. a web clock-in while this
    // screen was still showing). Explain it instead of showing the raw error code, and keep the session.
    [Fact]
    public async Task SignOut_RefusedWhileClockedIn_ExplainsAndKeepsTheSession()
    {
        var pipe = new FakeNamedPipeClient { NextLogoutResult = new LogoutResultPayload(false, "CLOCKED_IN") };
        var preferences = new FakePreferencesStore();
        preferences.Set(SessionPreferenceKeys.EmployeeDisplayName, "Alice");
        var vm = Make(pipe, preferences);

        await vm.SignOutCommand.ExecuteAsync(null);

        Assert.Equal("You're still clocked in. Clock out before signing out.", vm.ErrorMessage);
        Assert.Equal("Alice", preferences.Get(SessionPreferenceKeys.EmployeeDisplayName, ""));
    }

    [Fact]
    public void LiveTimer_DefaultsToZeroUntilClockIn()
    {
        // Pre-clock-in mockup shows session timer at 00:00:00 (wall clock is separate).
        var vm = Make();
        Assert.Equal("00:00:00", vm.LiveTimer);
    }

    [Fact]
    public void ConnectionStatus_StartsConnecting_UntilServiceReplies()
    {
        var pipe = new FakeNamedPipeClient();
        var vm = Make(pipe);
        Assert.False(vm.IsConnected);
        Assert.Equal("Connecting…", vm.ConnectionStatus);
        Assert.Equal("Connecting…", vm.WorkspaceStatus);

        pipe.SimulateStatus(new StatusResponsePayload(MonitoringState.Stopped, DateTimeOffset.UtcNow));
        Assert.True(vm.IsConnected);
        Assert.Equal("Connected", vm.ConnectionStatus);
        Assert.Equal("Connected", vm.WorkspaceStatus);
    }

    [Fact]
    public void PoliciesStatus_WaitsForPolicy_ThenShowsLoaded()
    {
        var pipe = new FakeNamedPipeClient();
        var vm = Make(pipe);
        Assert.Equal("Waiting for policy", vm.PoliciesStatus);

        pipe.SimulatePolicy(new AgentPolicy { Version = "1" });
        Assert.Equal("Policy loaded", vm.PoliciesStatus);
    }

    [Fact]
    public void InternetStatus_DoesNotClaimQualityItCannotMeasure()
    {
        var vm = Make();
        Assert.DoesNotContain("Excellent", vm.InternetStatus);
    }

    [Fact]
    public void DeviceType_IsDetected_NotAssumed()
    {
        var vm = Make();
        Assert.Contains(vm.DeviceType, new[] { "Windows Desktop", "Windows Laptop", "Windows PC" });
    }

    [Fact]
    public void Greeting_IsNotEmpty()
    {
        var vm = Make();
        Assert.NotEmpty(vm.Greeting);
    }

    [Fact]
    public async Task ClockInCommand_SendsLifecycleClockIn()
    {
        var pipe = new FakeNamedPipeClient();
        var vm   = new ClockInViewModel(pipe, new FakePreferencesStore());
        await vm.ClockInCommand.ExecuteAsync(null);
        Assert.Contains(LifecycleAction.ClockIn, pipe.LifecycleActions);
    }

    [Fact]
    public async Task ClockInCommand_WhenWorkModePhotoRequired_DoesNotSendLifecycle()
    {
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new AgentPolicy
            {
                Version = "v1",
                PhotoRequiredEnabled = true,
                CameraVerificationEnabled = false
            }
        };
        var vm = new ClockInViewModel(pipe, new FakePreferencesStore());
        await vm.ClockInCommand.ExecuteAsync(null);
        Assert.DoesNotContain(LifecycleAction.ClockIn, pipe.LifecycleActions);
    }

    [Fact]
    public async Task ClockInCommand_WhenCameraVerificationOnButPhotoNotRequired_StillSendsLifecycle()
    {
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new AgentPolicy
            {
                Version = "v1",
                PhotoRequiredEnabled = false,
                CameraVerificationEnabled = true
            }
        };
        var vm = new ClockInViewModel(pipe, new FakePreferencesStore());
        await vm.ClockInCommand.ExecuteAsync(null);
        Assert.Contains(LifecycleAction.ClockIn, pipe.LifecycleActions);
    }

    [Fact]
    public async Task ClockInCommand_OnFailure_SetsErrorMessage()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextLifecycleResult = new LifecycleResultPayload(
                false, "ALREADY_CLOCKED_IN", "You are already clocked in.",
                MonitoringState.Active, null)
        };
        var vm = new ClockInViewModel(pipe, new FakePreferencesStore());
        await vm.ClockInCommand.ExecuteAsync(null);
        Assert.Equal("You are already clocked in.", vm.ErrorMessage);
    }

    [Fact]
    public void ClockInCommand_AlwaysEnabled()
    {
        var vm = Make();
        Assert.True(vm.ClockInCommand.CanExecute(null));
    }

    [Fact]
    public void EmployeeName_RefreshesOnAppearing_AfterSignOutAndReLogin()
    {
        // Regression: ClockInPage is a cached Shell tab, so its ViewModel is
        // constructed once and reused across sign-out/re-activation cycles.
        // EmployeeName must be re-read from preferences on every OnAppearing,
        // not only once at construction, or the previous employee's name
        // survives into the next employee's session.
        var preferences = new FakePreferencesStore();
        preferences.Set(SessionPreferenceKeys.EmployeeDisplayName, "Alice");
        var vm = Make(preferences: preferences);
        Assert.Equal("Alice", vm.EmployeeName);

        // Simulate sign-out clearing the session and a different employee
        // activating on the same device.
        preferences.Set(SessionPreferenceKeys.EmployeeDisplayName, "Bob");
        vm.OnAppearing();

        Assert.Equal("Bob", vm.EmployeeName);
    }

    [Fact]
    public void WorkLocation_NoSavedDisplayAndFixedOfficeWorkMode_ShowsOffice()
    {
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new AgentPolicy
            {
                Version = "v1", AllowsDailyLocationChoice = false, SelfRegistersLocation = false,
                ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
            }
        };
        var vm = Make(pipe: pipe);

        vm.OnAppearing();

        Assert.Equal("Office", vm.WorkLocation);
    }

    [Fact]
    public void WorkLocation_NoSavedDisplayAndFixedSelfRegisteredWorkMode_ShowsWorkFromHome()
    {
        var pipe = new FakeNamedPipeClient
        {
            LastKnownPolicy = new AgentPolicy
            {
                Version = "v1", AllowsDailyLocationChoice = false, SelfRegistersLocation = true,
                ValidUntil = DateTimeOffset.UtcNow.AddHours(1)
            }
        };
        var vm = Make(pipe: pipe);

        vm.OnAppearing();

        Assert.Equal("Work From Home", vm.WorkLocation);
    }

    [Fact]
    public async Task ClockInAsync_AlreadyClockedOut_DoesNotShowRawError()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextLifecycleResult = new LifecycleResultPayload(
                false, ClockActionErrorCodes.AlreadyClockedOut, "You've already clocked out for today.",
                MonitoringState.Stopped, null)
        };
        var vm = Make(pipe);

        await vm.ClockInCommand.ExecuteAsync(null);

        Assert.Null(vm.ErrorMessage);
        Assert.False(vm.IsClockinIn);
    }
}
