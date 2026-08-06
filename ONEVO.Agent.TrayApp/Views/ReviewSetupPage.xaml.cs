namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class ReviewSetupPage : ContentPage
{
    public ReviewSetupPage(ReviewSetupViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
