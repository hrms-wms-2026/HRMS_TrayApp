namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.ViewModels;

public partial class PrepareWorkspacePage : ContentPage
{
    public PrepareWorkspacePage(PrepareWorkspaceViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is PrepareWorkspaceViewModel vm)
            await vm.LoadAsync();
    }
}
