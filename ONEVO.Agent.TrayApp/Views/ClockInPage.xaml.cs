namespace ONEVO.Agent.TrayApp.Views;

using ONEVO.Agent.TrayApp.Controls;
using ONEVO.Agent.TrayApp.ViewModels;

public partial class ClockInPage : ContentPage
{
    public ClockInPage(ClockInViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        ResponsiveTwoPane.Attach(this, PaneGrid, LeftPane, RightPane);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is ClockInViewModel vm)
            vm.OnAppearing();
    }
}
