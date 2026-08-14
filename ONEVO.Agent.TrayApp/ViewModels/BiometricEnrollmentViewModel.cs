namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.Shared.IPC;
using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class BiometricEnrollmentViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private bool _isSessionReady;

    [ObservableProperty] private bool _isCompleting;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private Guid _attemptId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _awsSessionId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _region;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _challengeType;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _accessKeyId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _secretAccessKey;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SessionConfig))]
    private string? _sessionToken;

    public BiometricEnrollmentViewModel(INamedPipeClient pipe)
    {
        Title = "Face Enrollment";
        _pipe = pipe;
    }

    /// <summary>Bound to BiometricWebView.SessionConfig — null until StartSessionAsync succeeds, which tears the capture surface down again on any later failure.</summary>
    public BiometricSessionConfig? SessionConfig =>
        IsSessionReady && AwsSessionId is not null && Region is not null && ChallengeType is not null
            && AccessKeyId is not null && SecretAccessKey is not null && SessionToken is not null
            ? new BiometricSessionConfig(AwsSessionId, Region, ChallengeType, AccessKeyId, SecretAccessKey, SessionToken)
            : null;

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

    /// <summary>Bound to BiometricWebView.CaptureFinishedCommand — invoked by the platform handler when the JS bridge posts a completion/error message.</summary>
    [RelayCommand]
    private Task CaptureFinished(BiometricCaptureOutcome outcome) =>
        ReportCaptureFinishedAsync(outcome.Succeeded, outcome.ErrorCode, CancellationToken.None);

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
