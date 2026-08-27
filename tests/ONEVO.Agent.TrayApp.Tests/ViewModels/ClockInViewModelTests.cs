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

    [Fact]
    public void LiveTimer_DefaultsToZeroUntilClockIn()
    {
        // Pre-clock-in mockup shows session timer at 00:00:00 (wall clock is separate).
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
    public void InternetStatus_DefaultsExcellentConnection()
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
    public async Task ClockInCommand_SendsLifecycleClockIn()
    {
        var pipe = new FakeNamedPipeClient();
        var vm   = new ClockInViewModel(pipe, new FakePreferencesStore());
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
}
