namespace ONEVO.Agent.TrayApp.ViewModels;

public sealed partial class StatusPopupViewModel : BaseViewModel
{
    [ObservableProperty]
    private MonitoringState _monitoringState = MonitoringState.Unenrolled;

    public StatusPopupViewModel()
    {
        Title = "ONEVO WorkPulse";
    }
}
