namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class WorkLocationPage : ContentPage
{
    public WorkLocationPage(WorkLocationViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
