namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class IdentityVerificationPage : ContentPage
{
    private readonly IdentityVerificationViewModel _vm;

    public IdentityVerificationPage(IdentityVerificationViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.LoadCapturedPhoto();
    }
}
