using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Tests.Fakes;
using ONEVO.Agent.TrayApp.ViewModels;

namespace ONEVO.Agent.TrayApp.Tests.ViewModels;

public class BiometricEnrollmentViewModelTests
{
    private static BiometricEnrollmentViewModel Make(FakeNamedPipeClient? pipe = null) =>
        new(pipe ?? new FakeNamedPipeClient());

    [Fact]
    public async Task StartSessionAsync_OnSuccess_PopulatesCaptureCredentials()
    {
        var attemptId = Guid.NewGuid();
        var pipe = new FakeNamedPipeClient
        {
            NextEnrollmentSessionResult = new BiometricEnrollmentSessionReadyPayload(
                true, null, attemptId, "aws-session-1", "ap-south-1", "FaceMovementAndLightChallenge",
                "AKIA", "secret", "token", DateTimeOffset.UtcNow.AddMinutes(15))
        };
        var vm = Make(pipe);

        await vm.StartSessionCommand.ExecuteAsync(null);

        Assert.True(vm.IsSessionReady);
        Assert.Equal(attemptId, vm.AttemptId);
        Assert.Equal("aws-session-1", vm.AwsSessionId);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task StartSessionAsync_OnFailure_SetsErrorMessage()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextEnrollmentSessionResult = new BiometricEnrollmentSessionReadyPayload(
                false, "NO_DEVICE_CREDENTIAL", Guid.Empty, null, null, null, null, null, null, null)
        };
        var vm = Make(pipe);

        await vm.StartSessionCommand.ExecuteAsync(null);

        Assert.False(vm.IsSessionReady);
        Assert.Equal("NO_DEVICE_CREDENTIAL", vm.ErrorMessage);
    }

    [Fact]
    public async Task ReportCaptureFinishedAsync_OnSuccess_ClearsCompletingFlag()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextEnrollmentCompletionResult = new BiometricEnrollmentResultPayload(true, null, "active")
        };
        var vm = Make(pipe);

        await vm.ReportCaptureFinishedAsync(captureSucceeded: true, clientErrorCode: null, CancellationToken.None);

        Assert.False(vm.IsCompleting);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task ReportCaptureFinishedAsync_OnBackendFailure_SetsErrorMessage()
    {
        var pipe = new FakeNamedPipeClient
        {
            NextEnrollmentCompletionResult = new BiometricEnrollmentResultPayload(false, "liveness_failed", null)
        };
        var vm = Make(pipe);

        await vm.ReportCaptureFinishedAsync(captureSucceeded: false, clientErrorCode: "CAMERA_DENIED", CancellationToken.None);

        Assert.False(vm.IsCompleting);
        Assert.Equal("liveness_failed", vm.ErrorMessage);
    }
}
