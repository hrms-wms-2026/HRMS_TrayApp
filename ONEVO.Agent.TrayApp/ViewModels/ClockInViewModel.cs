namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.Shared.IPC;

public sealed partial class ClockInViewModel : BaseViewModel
{
    private readonly INamedPipeClient _pipe;

    [ObservableProperty] private string _greeting         = "Good morning";
    [ObservableProperty] private string _employeeName     = string.Empty;
    [ObservableProperty] private string _workLocation     = string.Empty;
    [ObservableProperty] private DateTimeOffset _currentDate = DateTimeOffset.Now;

    [ObservableProperty] private string _liveTimer        = "00:00:00";
    [ObservableProperty] private string _connectionStatus = "Online";
    [ObservableProperty] private string _internetStatus   = "Excellent Connection";
    [ObservableProperty] private string _deviceType       = "Windows Desktop";

    [ObservableProperty] private bool _isClockinIn;
    [ObservableProperty] private string? _errorMessage;

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

    [RelayCommand]
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
