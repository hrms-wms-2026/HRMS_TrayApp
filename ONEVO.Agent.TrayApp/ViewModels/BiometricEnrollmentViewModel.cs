namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class BiometricEnrollmentViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty] private bool _isSessionReady;
    [ObservableProperty] private bool _isCompleting;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private Guid _attemptId;
    [ObservableProperty] private string? _awsSessionId;
    [ObservableProperty] private string? _region;
    [ObservableProperty] private string? _challengeType;
    [ObservableProperty] private string? _accessKeyId;
    [ObservableProperty] private string? _secretAccessKey;
    [ObservableProperty] private string? _sessionToken;

    public BiometricEnrollmentViewModel(INamedPipeClient pipe)
    {
        Title = "Face Enrollment";
        _pipe = pipe;
    }

    [RelayCommand]
    private async Task StartSessionAsync(CancellationToken ct)
    {
        ErrorMessage = null;
        var result = await _pipe.StartBiometricEnrollmentAsync(ct);

        if (result is null || !result.Success)
        {
            ErrorMessage = result?.ErrorCode ?? "No response from OneXso Agent Service.";
            IsSessionReady = false;
            return;
        }

        AttemptId = result.AttemptId;
        AwsSessionId = result.AwsSessionId;
        Region = result.Region;
        ChallengeType = result.ChallengeType;
        AccessKeyId = result.AccessKeyId;
        SecretAccessKey = result.SecretAccessKey;
        SessionToken = result.SessionToken;
        IsSessionReady = true;
    }

    /// <summary>Called by the WebView2 host once the JS FaceLivenessDetector fires its analysis-complete or error event.</summary>
    public async Task ReportCaptureFinishedAsync(bool captureSucceeded, string? clientErrorCode, CancellationToken ct)
    {
        IsCompleting = true;
        try
        {
            var result = await _pipe.CompleteBiometricEnrollmentAsync(AttemptId, captureSucceeded, clientErrorCode, ct);

            if (result is null || !result.Success)
            {
                ErrorMessage = result?.ErrorCode ?? "Enrollment could not be completed.";
                return;
            }

            try { await Shell.Current.GoToAsync("//review"); }
            catch { /* unit tests */ }
        }
        finally
        {
            IsCompleting = false;
        }
    }
}
