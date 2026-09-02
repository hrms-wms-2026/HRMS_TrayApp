namespace ONEVO.Agent.TrayApp.ViewModels;

using System.Runtime.InteropServices;
using ONEVO.Agent.TrayApp.Services;

public sealed partial class ConfirmDeviceViewModel : BaseViewModel
{
    private readonly IPreferencesStore _preferences;

    [ObservableProperty] private string _deviceName = "—";
    [ObservableProperty] private string _operatingSystemName = "—";
    [ObservableProperty] private string _deviceType = "Desktop";
    [ObservableProperty] private string _registeredTo = "—";
    [ObservableProperty] private string _organization = "—";

    public ConfirmDeviceViewModel(IPreferencesStore preferences)
    {
        Title = "Confirm This Device";
        _preferences = preferences;
    }

    public void OnAppearing()
    {
        DeviceName = EmployeeSession.DeviceName(_preferences);
        OperatingSystemName = DescribeOs();
        DeviceType = "Desktop";
        RegisteredTo = SetupFlow.DisplayOrDash(EmployeeSession.Name(_preferences));
        Organization = SetupFlow.DisplayOrDash(EmployeeSession.Organization(_preferences));
    }

    [RelayCommand]
    private async Task Back()
    {
        try { await Shell.Current.GoToAsync(SetupFlow.Privacy); }
        catch { /* unit tests */ }
    }

    [RelayCommand]
    private async Task ConfirmAndContinue()
    {
        try { await Shell.Current.GoToAsync(SetupFlow.AfterConfirmDevice); }
        catch { /* unit tests */ }
    }

    private static string DescribeOs()
    {
        if (OperatingSystem.IsWindows())
        {
            var version = Environment.OSVersion.Version;
            return version.Build >= 22000 ? "Windows 11" : "Windows 10";
        }

        return RuntimeInformation.OSDescription;
    }
}
