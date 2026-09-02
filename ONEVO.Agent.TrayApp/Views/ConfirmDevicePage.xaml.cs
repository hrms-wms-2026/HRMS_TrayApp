namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class ConfirmDevicePage : ContentPage
{
    public ConfirmDevicePage(ConfirmDeviceViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ConfirmDeviceViewModel vm)
            vm.OnAppearing();
    }
}
