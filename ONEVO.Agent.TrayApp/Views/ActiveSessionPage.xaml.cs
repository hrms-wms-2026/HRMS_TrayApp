namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class ActiveSessionPage : ContentPage
{
    public ActiveSessionPage(ActiveSessionViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
