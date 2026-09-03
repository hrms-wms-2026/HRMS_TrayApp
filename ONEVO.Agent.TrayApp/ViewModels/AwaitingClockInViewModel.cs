namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class AwaitingClockInViewModel : BaseViewModel
{
    public AwaitingClockInViewModel()
    {
        Title = "Waiting for Clock In";
        Message = "Clock in from the ONEVO web portal to start your work session. " +
                   "This device isn't set up to clock in directly for your work mode.";
    }

    public string Message { get; }
}
