namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class EndSessionPage : ContentPage
{
    public EndSessionPage(EndSessionViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
