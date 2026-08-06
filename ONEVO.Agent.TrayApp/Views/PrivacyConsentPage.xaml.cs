namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class PrivacyConsentPage : ContentPage
{
    public PrivacyConsentPage(PrivacyConsentViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
