namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class AwaitingClockInPage : ContentPage
{
    public AwaitingClockInPage(AwaitingClockInViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
