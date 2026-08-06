namespace ONEVO.Agent.TrayApp.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.IPC;

public sealed partial class ClockInViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty] private string _greeting         = "Good morning";
    [ObservableProperty] private string _employeeName     = string.Empty;
    [ObservableProperty] private string _workLocation     = string.Empty;
    [ObservableProperty] private DateTimeOffset _currentDate = DateTimeOffset.Now;

    [ObservableProperty] private bool _identityChecked    = true;
    [ObservableProperty] private bool _permissionsReady   = true;
    [ObservableProperty] private bool _requiredChecksPass = true;

    [ObservableProperty] private bool _isClockinIn;
    [ObservableProperty] private string? _errorMessage;

    public bool ReadyToClockIn => IdentityChecked && PermissionsReady && RequiredChecksPass;

    public ClockInViewModel(INamedPipeClient pipe)
    {
        Title    = "Ready to Start Work";
        _pipe    = pipe;
        Greeting = GetGreeting();
    }

    private static string GetGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
    }

    [RelayCommand(CanExecute = nameof(ReadyToClockIn))]
    private async Task ClockInAsync(CancellationToken ct)
    {
        IsClockinIn  = true;
        ErrorMessage = null;
        try
        {
            var envelope = new IpcEnvelope { Type = IpcMessageTypes.StatusRequest };
            await _pipe.SendEnvelopeAsync(envelope, ct);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsClockinIn = false;
        }
    }
}
