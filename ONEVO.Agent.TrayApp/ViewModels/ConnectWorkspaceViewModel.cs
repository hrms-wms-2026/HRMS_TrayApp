namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        Regex.IsMatch(ActivationCode.Trim(), @"^[A-Za-z0-9]{6}$");

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAndConnectAsync(CancellationToken ct)
    {
        IsConnecting = true;
        ErrorMessage = null;

        try
        {
            var payload = new ActivationCodeSubmitPayload(ActivationCode.Trim().ToUpperInvariant());
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
    private static void OpenEmployeePortal()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = "https://app.onevo.com",
            UseShellExecute = true
        });
    }
}
