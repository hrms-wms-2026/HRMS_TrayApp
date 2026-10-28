namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Services;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class BiometricEnrollmentPage : ContentPage
{
    private readonly BiometricEnrollmentViewModel _vm;

    public BiometricEnrollmentPage(BiometricEnrollmentViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await UiTasks.RunSafelyAsync(
            () => _vm.StartSessionCommand.ExecuteAsync(null), $"{nameof(BiometricEnrollmentPage)}.{nameof(OnAppearing)}");
    }
}
