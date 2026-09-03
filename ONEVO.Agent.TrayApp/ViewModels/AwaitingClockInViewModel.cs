namespace ONEVO.Agent.TrayApp.ViewModels;

using ONEVO.Agent.TrayApp.Services;

public sealed partial class AwaitingClockInViewModel : BaseViewModel
{
    public AwaitingClockInViewModel()
    {
        Title = "Waiting for Clock In";
        Message = "Clock in from the ONEVO web portal to start your work session. " +
                   "This device isn't set up to clock in directly for your work mode.";
    }

    public string Message { get; }

    [RelayCommand]
    private async Task BackAsync()
    {
        try { await Shell.Current.GoToAsync(SetupFlow.ClockIn); }
        catch { /* unit tests */ }
    }
}
