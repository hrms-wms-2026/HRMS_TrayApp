namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class ConnectWorkspacePage : ContentPage
{
    public ConnectWorkspacePage(ConnectWorkspaceViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
