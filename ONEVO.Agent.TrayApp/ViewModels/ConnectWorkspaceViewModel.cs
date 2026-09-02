namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.IPC;

public sealed partial class ConnectWorkspaceViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;
    private readonly IPreferencesStore _preferences;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(VerifyAndConnectCommand))]
    private string _activationCode = string.Empty;

    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isConnecting;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionLabel = "Not Connected";
    [ObservableProperty] private string _versionText = "Version 1.0.0";
    [ObservableProperty] private string _hintText =
        "Paste the activation code copied from the ONEVO web portal.";

    public ConnectWorkspaceViewModel(INamedPipeClient pipe, IPreferencesStore preferences)
    {
        Title = "Connect ONEVO Workspace";
        _pipe = pipe;
        _preferences = preferences;
        _pipe.OnDisconnected += () =>
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsConnected = false;
                    ConnectionLabel = "Not Connected";
                });
            }
            catch
            {
                IsConnected = false;
                ConnectionLabel = "Not Connected";
            }
        };
        _pipe.OnStateReceived += _ =>
        {
            try
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    IsConnected = true;
                    ConnectionLabel = "Connected";
                });
            }
            catch
            {
                IsConnected = true;
                ConnectionLabel = "Connected";
            }
        };
    }

    private bool CanVerify =>
        !IsConnecting &&
        IsValidActivationCode(ActivationCode.Trim().ToUpperInvariant());

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAndConnectAsync(CancellationToken ct)
    {
        IsConnecting = true;
        ErrorMessage = null;
        try
        {
            var code = ActivationCode.Trim().ToUpperInvariant();
            var result = await _pipe.SendActivationAsync(code, ct);

            if (result is null)
            {
                ErrorMessage = "No response from OneXso Agent Service. Is the service running?";
                IsConnected = false;
                ConnectionLabel = "Not Connected";
                return;
            }

            if (!result.Success)
            {
                ErrorMessage = result.ErrorCode switch
                {
                    "INVALID_CODE" => "Invalid or expired code. Generate a new code in the web portal.",
                    "LOCKED" => "The tray is locked. Restart the ONEVO service and try again.",
                    "ALREADY_ENROLLED" => "This tray is already connected. Use the existing connected session.",
                    "SERVICE_UNAVAILABLE" => "Can't reach the ONEVO backend right now. Check your connection and try again.",
                    _ => result.ErrorCode ?? "Activation failed."
                };
                IsConnected = false;
                ConnectionLabel = "Not Connected";
                return;
            }

            // A new activation is a new employee/setup session. Clear any stale
            // onboarding values before writing the new employee details so the
            // subsequent steps cannot reuse the previous user's data.
            SessionPreferenceKeys.ClearAll(_preferences);
            if (!string.IsNullOrWhiteSpace(result.EmployeeName))
                _preferences.Set(SessionPreferenceKeys.EmployeeDisplayName, result.EmployeeName);
            if (!string.IsNullOrWhiteSpace(result.EmployeeEmail))
                _preferences.Set(SessionPreferenceKeys.EmployeeEmail, result.EmployeeEmail);
            if (!string.IsNullOrWhiteSpace(result.EmployeeNumber))
                _preferences.Set(SessionPreferenceKeys.EmployeeId, result.EmployeeNumber);

            IsConnected = true;
            ConnectionLabel = result.EmployeeProfileStatus == "company_context_required"
                ? "Connected — select a company in ONEVO to load your employee profile"
                : BuildConnectedLabel(result.EmployeeNumber, result.EmployeeName);
            try { await Shell.Current.GoToAsync("//prepare"); }
            catch { /* unit tests */ }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
            IsConnected = false;
            ConnectionLabel = "Not Connected";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    [RelayCommand]
    private async Task PasteActivationCodeAsync()
    {
        try
        {
            if (Clipboard.Default.HasText)
            {
                var text = await Clipboard.Default.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    // Clipboard content often includes a trailing newline or spaces.
                    // Normalize it once so CanVerify and the service receive the same code.
                    ActivationCode = text.Trim().ToUpperInvariant();
                }
            }
        }
        catch
        {
            // Clipboard unavailable in unit tests / restricted hosts.
        }
    }

    // Client-side gate only decides whether there's enough input to bother
    // submitting — the server (AgentWorker.IsValidActivationCode) enforces the
    // real 8-char restricted-alphabet format and returns INVALID_CODE for a
    // bad value, so a longer pasted code (e.g. with formatting dashes) isn't
    // blocked here before the user even gets that feedback.
    private static bool IsValidActivationCode(string code) => code.Length >= 6;

    private static string BuildConnectedLabel(string? employeeNumber, string? employeeName)
    {
        var identity = string.Join(" · ", new[] { employeeNumber, employeeName }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(identity) ? "Connected" : $"Connected — {identity}";
    }
}
