namespace ONEVO.Agent.TrayApp.Controls;

using System.Windows.Input;

/// <summary>Config handed to the packaged React FaceLivenessDetector via the WebView2 JS bridge. Lives only in memory — never Preferences, SQLite, or logs (architecture doc S8.2).</summary>
public sealed record BiometricSessionConfig(
    string AwsSessionId, string Region, string ChallengeType,
    string AccessKeyId, string SecretAccessKey, string SessionToken);

/// <summary>Reported by the JS bridge once the FaceLivenessDetector's analysis-complete or error event fires.</summary>
public sealed record BiometricCaptureOutcome(bool Succeeded, string? ErrorCode);

/// <summary>Cross-platform placeholder for the WebView2-hosted biometric liveness capture surface. The
/// platform handler owns virtual-host mapping, camera-permission gating, and the JS postMessage bridge.</summary>
public sealed class BiometricWebView : View
{
    public static readonly BindableProperty SessionConfigProperty =
        BindableProperty.Create(nameof(SessionConfig), typeof(BiometricSessionConfig), typeof(BiometricWebView), null);

    public static readonly BindableProperty CaptureFinishedCommandProperty =
        BindableProperty.Create(nameof(CaptureFinishedCommand), typeof(ICommand), typeof(BiometricWebView), null);

    /// <summary>Set once StartBiometricEnrollmentAsync returns; the handler injects it into the JS runtime and navigates. Set to null to tear down the capture surface.</summary>
    public BiometricSessionConfig? SessionConfig
    {
        get => (BiometricSessionConfig?)GetValue(SessionConfigProperty);
        set => SetValue(SessionConfigProperty, value);
    }

    /// <summary>Invoked by the handler with a <see cref="BiometricCaptureOutcome"/> when the JS bridge posts a completion/error message.</summary>
    public ICommand? CaptureFinishedCommand
    {
        get => (ICommand?)GetValue(CaptureFinishedCommandProperty);
        set => SetValue(CaptureFinishedCommandProperty, value);
    }
}
