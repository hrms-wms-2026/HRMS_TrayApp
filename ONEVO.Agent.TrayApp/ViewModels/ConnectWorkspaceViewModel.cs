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
        "Paste the activation code copied from the OneXso Workspace web portal.";

    public ConnectWorkspaceViewModel(INamedPipeClient pipe, IPreferencesStore preferences)
    {
        Title = "Connect OneXso Workspace";
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
        ActivationCode.Trim().Length >= 6;

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
                    "INVALID_CODE" => "Invalid or expired activation code. Get a new one from the employee portal.",
                    "LOCKED" => "Device is locked. Contact your admin.",
                    "SERVICE_UNAVAILABLE" => "Can't reach the OneXso backend right now. Check your connection and try again.",
                    _ => result.ErrorCode ?? "Activation failed."
                };
                IsConnected = false;
                ConnectionLabel = "Not Connected";
                return;
            }

            if (!string.IsNullOrWhiteSpace(result.EmployeeName))
                _preferences.Set("onevo.employee_display_name", result.EmployeeName);
            if (!string.IsNullOrWhiteSpace(result.EmployeeEmail))
                _preferences.Set("onevo.employee_email", result.EmployeeEmail);
            if (!string.IsNullOrWhiteSpace(result.EmployeeNumber))
                _preferences.Set("onevo.employee_id", result.EmployeeNumber);

            IsConnected = true;
            ConnectionLabel = "Connected";
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
                    ActivationCode = text.Trim().ToUpperInvariant();
            }
        }
        catch
        {
            // Clipboard unavailable in unit tests / restricted hosts.
        }
    }

    [RelayCommand]
    private static void OpenEmployeePortal() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = WorkspaceLinks.PortalUrl,
            UseShellExecute = true
        });
}
