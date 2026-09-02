namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class PrepareWorkspacePage : ContentPage
{
    public PrepareWorkspacePage(PrepareWorkspaceViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, FinalPaneGrid, FinalLeftPane, FinalRightPane, narrowLeftMaxHeight: 280);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PrepareWorkspaceViewModel vm)
        {
            vm.RefreshLocationStatus();
            await vm.LoadAsync();
        }
    }
}
