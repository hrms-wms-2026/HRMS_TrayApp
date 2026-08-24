namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ConnectWorkspacePage : ContentPage
{
    public ConnectWorkspacePage(ConnectWorkspaceViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane, narrowLeftMaxHeight: 280);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await PageAnimations.EntranceAsync(LeftPane, RightPane);
    }
}
