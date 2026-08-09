namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ActiveSessionPage : ContentPage
{
    public ActiveSessionPage(ActiveSessionViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ActiveSessionViewModel vm)
            vm.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is ActiveSessionViewModel vm)
            vm.OnDisappearing();
    }
}
