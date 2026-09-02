namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class PrivacyConsentPage : ContentPage
{
    public PrivacyConsentPage(PrivacyConsentViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane, narrowLeftMaxHeight: 240);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PrivacyConsentViewModel vm)
            vm.OnAppearing();
    }
}
