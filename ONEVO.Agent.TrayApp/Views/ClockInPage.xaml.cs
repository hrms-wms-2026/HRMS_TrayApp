namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class ClockInPage : ContentPage
{
    public ClockInPage(ClockInViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
