namespace ONEVO.Agent.TrayApp.Views;

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
        await _vm.StartSessionCommand.ExecuteAsync(null);
    }
}
