namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class WorkLocationPage : ContentPage
{
    public WorkLocationPage(WorkLocationViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is WorkLocationViewModel vm)
            await vm.OnAppearingAsync();
    }
}
