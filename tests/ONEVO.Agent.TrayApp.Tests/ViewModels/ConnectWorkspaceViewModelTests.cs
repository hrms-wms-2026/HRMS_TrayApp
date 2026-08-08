using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public sealed class ConnectWorkspaceViewModelTests
{
    private static ConnectWorkspaceViewModel Make() =>
        new(new FakeNamedPipeClient(), new FakePreferencesStore());

    [Fact]
    public void ActivationCode_DefaultsToEmpty()
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
    public void VerifyAndConnectCommand_EnabledWhenSixChars()
    {
        var vm = Make();
        vm.ActivationCode = "ABC123";
        Assert.True(vm.VerifyAndConnectCommand.CanExecute(null));
    }

    [Fact]
    public void VerifyAndConnectCommand_EnabledForLongerPastedCode()
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

    [Fact]
    public async Task VerifyAndConnectCommand_SendsActivationToPipe()
    {
        var pipe = new FakeNamedPipeClient();
        var vm   = new ConnectWorkspaceViewModel(pipe, new FakePreferencesStore());
        vm.ActivationCode = "ABC123";
        await vm.VerifyAndConnectCommand.ExecuteAsync(null);
        Assert.Single(pipe.SentEnvelopes);
        Assert.Equal(ONEVO.Agent.Shared.IPC.IpcMessageTypes.ActivationCodeSubmit, pipe.SentEnvelopes[0].Type);
    }

    [Fact]
    public async Task VerifyAndConnectCommand_OnFailure_SetsError()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextEnrollmentResult = new ONEVO.Agent.Shared.IPC.EnrollmentResultPayload
            {
                Success = false,
                ErrorCode = "INVALID_CODE"
            }
        };
        var vm = new ConnectWorkspaceViewModel(pipe, new FakePreferencesStore());
        vm.ActivationCode = "BAD";
        // length < 6 disables command — use long enough invalid path via canned fail with 6 chars
        vm.ActivationCode = "BADBAD";
        await vm.VerifyAndConnectCommand.ExecuteAsync(null);
        Assert.NotNull(vm.ErrorMessage);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public async Task VerifyAndConnectCommand_OnSuccess_CachesEmployeeEmailAndId()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextEnrollmentResult = new ONEVO.Agent.Shared.IPC.EnrollmentResultPayload
            {
                Success = true,
                EmployeeName = "Priya Employee",
                EmployeeEmail = "priya@test.dev",
                EmployeeNumber = "EMP-0001"
            }
        };
        var preferences = new FakePreferencesStore();
        var vm = new ConnectWorkspaceViewModel(pipe, preferences);
        vm.ActivationCode = "ABC123";

        await vm.VerifyAndConnectCommand.ExecuteAsync(null);

        Assert.Equal("Priya Employee", preferences.Get("onevo.employee_display_name", string.Empty));
        Assert.Equal("priya@test.dev", preferences.Get("onevo.employee_email", string.Empty));
        Assert.Equal("EMP-0001", preferences.Get("onevo.employee_id", string.Empty));
    }
}
